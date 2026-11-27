using System.Diagnostics.CodeAnalysis;
using Content.Shared._KS14.CrewVehicle.Interior;
using Content.Shared._KS14.CrewVehicle.Mounts;
using Content.Shared._KS14.PopupLocalization;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Localizations;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Content.Shared.Verbs;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.CrewVehicle.AmmoPort;

/// <summary>
///     Loads a mounted weapon from inside its vehicle: ammunition used on the port goes into the port's weapon after its
///         load delay. A loose cartridge, a stack of them, or a box or magazine they are moved out of round by round, for
///         as long as the weapon has room; what does not fit stays where it was. Examining the port shows the weapon's
///         rounds.
/// </summary>
/// <remarks>
///     All of it is predicted: the interaction, the do-after, why not, and the rounds moved
///         (<see cref="SharedGunSystem.KsTryTransferBallisticRound"/>, which leaves one case to the server).
/// </remarks>
public abstract partial class SharedKsCrewVehicleAmmoPortSystem : EntitySystem
{
    [Dependency] private SharedDoAfterSystem _doAfterSystem = default!;
    [Dependency] private SharedGunSystem _gunSystem = default!;
    [Dependency] private SharedKsCrewVehicleInteriorSystem _interiorSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private SharedHandsSystem _handsSystem = default!;
    [Dependency] private ContentLocalizationManager _contentLocalizationManager = default!;
    [Dependency] protected SharedKsCrewVehicleMountSystem MountSystem = default!;
    [Dependency] private EntityQuery<BallisticAmmoProviderComponent> _ballisticQuery = default!;
    [Dependency] private EntityQuery<CartridgeAmmoComponent> _cartridgeQuery = default!;
    [Dependency] private EntityQuery<StackComponent> _stackQuery = default!;

    /// <summary>
    ///     The weapon <paramref name="port"/> loads, if its vehicle has it mounted.
    /// </summary>
    public bool TryGetWeapon(Entity<KsCrewVehicleAmmoPortComponent> port, [NotNullWhen(true)] out EntityUid? weaponUid)
    {
        weaponUid = null;
        return _interiorSystem.TryGetVehicle(port.Owner, out var vehicle) &&
               MountSystem.TryGetWeapon(vehicle.Value.Owner, port.Comp.Mount, out weaponUid);
    }

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

        // A box whose rounds only the server can move (KsTryTransferBallisticRound) leaves the client with nothing
        //      to say: the server's popup is the one shown.
        if (loaded == 0 && sourceComponent != null && !_gunSystem.KsCanTransferBallisticPredicted((usedUid, sourceComponent), weapon.Value))
            return;

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

    /// <remarks>
    ///     A by-value class event: the engine has <c>Entity&lt;T&gt;</c> handlers only for ref events. Formatted here, in
    ///         the examiner's culture: the client builds it from what it has been sent, and the server answers an examine
    ///         request inside its culture scope.
    /// </remarks>
    [SubscribeLocalEvent]
    private void OnExamined(EntityUid uid, KsCrewVehicleAmmoPortComponent component, ExaminedEvent args)
    {
        if (!TryGetWeapon((uid, component), out var weaponUid))
            return;

        var ev = new GetAmmoCountEvent();
        RaiseLocalEvent(weaponUid.Value, ref ev);
        args.PushMarkup(Loc.GetString("ks-crew-vehicle-ammo-port-examine", ("count", ev.Count), ("capacity", ev.Capacity)));

        // A one-round weapon, a breech, says which round waits in it, so its crew can tell one kind from another.
        if (ev.Capacity == 1 && ev.Count > 0 && GetNextRoundPrototype(weaponUid.Value) is { } roundPrototype)
        {
            args.PushMarkup(Loc.GetString("ks-crew-vehicle-ammo-port-chambered",
                ("round", _contentLocalizationManager.GetLocalizedPrototypeName(roundPrototype))));
        }
    }

    /// <summary>
    ///     The prototype of the round <paramref name="weaponUid"/> would fire next: the last one put in, or its fill
    ///         round.
    /// </summary>
    private EntityPrototype? GetNextRoundPrototype(EntityUid weaponUid)
    {
        if (!_ballisticQuery.TryComp(weaponUid, out var ballisticComponent))
            return null;

        if (ballisticComponent.Entities.Count > 0)
            return EntityManager.MetaQuery.TryGetComponent(ballisticComponent.Entities[^1], out var metaDataComponent) ? metaDataComponent.EntityPrototype : null;

        return ballisticComponent.UnspawnedCount > 0 && ballisticComponent.Proto is { } proto && ProtoMan.TryIndex(proto, out var prototype)
            ? prototype
            : null;
    }

    // GetVerbsEvent is a by-value class event, which only the (uid, component, args) handler form accepts (RA0054).
    [SubscribeLocalEvent]
    private void OnGetVerbs(EntityUid uid, KsCrewVehicleAmmoPortComponent component, GetVerbsEvent<AlternativeVerb> args)
    {
        if (!component.CanUnload || !args.CanAccess || !args.CanInteract || args.Hands == null)
            return;

        var port = new Entity<KsCrewVehicleAmmoPortComponent>(uid, component);
        var user = args.User;
        var loaded = TryGetWeapon(port, out var weaponUid) &&
                     _ballisticQuery.TryComp(weaponUid, out var weaponComponent) &&
                     weaponComponent.Entities.Count + weaponComponent.UnspawnedCount > 0;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("ks-crew-vehicle-ammo-port-verb-unload"),
            Disabled = !loaded,
            Act = () => TryUnload(port, user),
        });
    }

    /// <summary>
    ///     Takes the round waiting in the port's weapon out into <paramref name="userUid"/>'s hands, or at their feet.
    /// </summary>
    public bool TryUnload(Entity<KsCrewVehicleAmmoPortComponent> port, EntityUid userUid)
    {
        if (!port.Comp.CanUnload ||
            !TryGetWeapon(port, out var weaponUid) ||
            !_ballisticQuery.TryComp(weaponUid, out var weaponComponent))
            return false;

        if (!_gunSystem.KsTryTakeBallisticRound((weaponUid.Value, weaponComponent), out var roundUid))
        {
            // Nothing in it; a round only the server can make says nothing here, the server's popup is the one shown.
            if (weaponComponent.Entities.Count + weaponComponent.UnspawnedCount == 0)
                _popupSystem.PopupEntity(KsPopupMessage.Create("ks-crew-vehicle-ammo-port-empty"), port.Owner, userUid);
            return false;
        }

        _handsSystem.PickupOrDrop(userUid, roundUid.Value);
        _popupSystem.PopupEntity(KsPopupMessage.Create("ks-crew-vehicle-ammo-port-unloaded", ("round", roundUid.Value)), port.Owner, userUid);
        return true;
    }

    [SubscribeLocalEvent]
    private void OnGetInteriorErrors(Entity<KsCrewVehicleAmmoPortComponent> port, ref KsGetCrewVehicleInteriorErrorsEvent args)
    {
        var name = ToPrettyString(port);
        var vehicleUid = args.Vehicle.Owner;
        if (!MountSystem.HasMount(vehicleUid, port.Comp.Mount))
        {
            args.Validation.AddError($"ammo port {name} loads mount '{port.Comp.Mount}', which {ToPrettyString(vehicleUid)} does not have");
            return;
        }

        if (MountSystem.TryGetWeapon(vehicleUid, port.Comp.Mount, out var weaponUid) && !_ballisticQuery.HasComp(weaponUid))
            args.Validation.AddError($"ammo port {name} loads mount '{port.Comp.Mount}', whose {ToPrettyString(weaponUid)} takes no rounds");
    }
}
