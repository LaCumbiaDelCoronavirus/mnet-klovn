using System.Diagnostics.CodeAnalysis;
using Content.Shared._KS14.CrewVehicle.AmmoPort;
using Content.Shared._KS14.PopupLocalization;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;

namespace Content.Server._KS14.CrewVehicle.AmmoPort;

/// <summary>
///     Loads a mounted weapon from inside its vehicle: ammunition used on the port goes into the port's weapon after its
///         load delay. A loose cartridge, a stack of them, or a box or magazine they are moved out of round by round, for
///         as long as the weapon has room; what does not fit stays where it was.
/// </summary>
/// <remarks>
///     Not predicted: the do-after is started here, and its bar reaches the client in its state.
/// </remarks>
public sealed partial class KsCrewVehicleAmmoPortSystem : SharedKsCrewVehicleAmmoPortSystem
{
    [Dependency] private SharedDoAfterSystem _doAfterSystem = default!;
    [Dependency] private SharedGunSystem _gunSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private EntityQuery<BallisticAmmoProviderComponent> _ballisticQuery = default!;
    [Dependency] private EntityQuery<CartridgeAmmoComponent> _cartridgeQuery = default!;
    [Dependency] private EntityQuery<StackComponent> _stackQuery = default!;

    /// <remarks>
    ///     A by-value class event: the engine has <c>Entity&lt;T&gt;</c> handlers only for ref events.
    /// </remarks>
    [SubscribeLocalEvent]
    private void OnInteractUsing(EntityUid uid, KsCrewVehicleAmmoPortComponent component, InteractUsingEvent args)
    {
        if (args.Handled || !_cartridgeQuery.HasComp(args.Used) && !_ballisticQuery.HasComp(args.Used))
            return;

        args.Handled = true;
        if (!CanLoad((uid, component), args.Used, args.User, out _))
            return;

        _doAfterSystem.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, component.LoadDelay,
            new KsCrewVehicleAmmoPortDoAfterEvent(), uid, target: uid, used: args.Used)
        {
            BreakOnMove = true,
            BreakOnHandChange = true,
            NeedHand = true,
        });
    }

    /// <remarks>
    ///     A by-value class event: the engine has <c>Entity&lt;T&gt;</c> handlers only for ref events.
    /// </remarks>
    [SubscribeLocalEvent]
    private void OnLoadDoAfter(EntityUid uid, KsCrewVehicleAmmoPortComponent component, KsCrewVehicleAmmoPortDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || args.Used is not { } usedUid)
            return;

        args.Handled = true;
        if (!CanLoad((uid, component), usedUid, args.User, out var weapon))
            return;

        var loaded = 0;
        var capacity = weapon.Value.Comp.Capacity;
        if (_ballisticQuery.TryComp(usedUid, out var sourceComponent))
        {
            while (loaded < capacity && _gunSystem.KsTryTransferBallisticRound((usedUid, sourceComponent), weapon.Value, args.User))
            {
                loaded++;
            }
        }
        else
        {
            // A loose cartridge goes in once; a stack gives one at a time until it runs out or the weapon is full.
            var stacked = _stackQuery.HasComp(usedUid);
            while (loaded < capacity && !TerminatingOrDeleted(usedUid) && _gunSystem.TryBallisticInsert(weapon.Value, usedUid, args.User))
            {
                loaded++;
                if (!stacked)
                    break;
            }
        }

        _popupSystem.PopupEntity(loaded > 0
                ? KsPopupMessage.Create("ks-crew-vehicle-ammo-port-loaded", ("count", loaded))
                : KsPopupMessage.Create("ks-crew-vehicle-ammo-port-wrong-ammo"),
            uid,
            args.User);
    }

    /// <summary>
    ///     Whether <paramref name="usedUid"/> could go into the port's weapon now, telling <paramref name="userUid"/> why
    ///         not.
    /// </summary>
    private bool CanLoad(Entity<KsCrewVehicleAmmoPortComponent> port,
        EntityUid usedUid,
        EntityUid userUid,
        [NotNullWhen(true)] out Entity<BallisticAmmoProviderComponent>? weapon)
    {
        weapon = null;
        if (!TryGetWeapon(port, out var weaponUid) || !_ballisticQuery.TryComp(weaponUid, out var weaponComponent))
        {
            _popupSystem.PopupEntity(KsPopupMessage.Create("ks-crew-vehicle-ammo-port-no-weapon"), port.Owner, userUid);
            return false;
        }

        if (_gunSystem.IsFull((weaponUid.Value, weaponComponent)))
        {
            _popupSystem.PopupEntity(KsPopupMessage.Create("ks-crew-vehicle-ammo-port-full"), port.Owner, userUid);
            return false;
        }

        // A box's rounds are checked one at a time as they are moved; a cartridge here.
        if (_cartridgeQuery.HasComp(usedUid) && !_gunSystem.CanInsertBallistic((weaponUid.Value, weaponComponent), usedUid))
        {
            _popupSystem.PopupEntity(KsPopupMessage.Create("ks-crew-vehicle-ammo-port-wrong-ammo"), port.Owner, userUid);
            return false;
        }

        weapon = (weaponUid.Value, weaponComponent);
        return true;
    }
}
