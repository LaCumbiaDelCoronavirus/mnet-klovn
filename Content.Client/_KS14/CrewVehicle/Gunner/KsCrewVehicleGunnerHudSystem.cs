using Content.Client.Alerts;
using Content.Shared._KS14.CrewVehicle.Gunner;
using Content.Shared._KS14.CrewVehicle.Interior;
using Content.Shared._KS14.CrewVehicle.Mounts;
using Content.Shared.Actions.Components;
using Content.Shared.Alert.Components;
using Content.Shared.Examine;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Client.GameObjects;
using Robust.Client.Player;

namespace Content.Client._KS14.CrewVehicle.Gunner;

/// <summary>
///     What a gunner reads off their screen: the selected weapon's rounds on the ammo alert, and each weapon's rounds on
///         its select action (the selected weapon's on a cycle action). Both are counted here, on the client, from the weapons' own networked ammo, every frame
///         they are drawn; the server sends nothing for them.
/// </summary>
public sealed partial class KsCrewVehicleGunnerHudSystem : EntitySystem
{
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private SharedKsCrewVehicleInteriorSystem _interiorSystem = default!;
    [Dependency] private SharedKsCrewVehicleMountSystem _mountSystem = default!;

    [Dependency] private EntityQuery<ActionComponent> _actionQuery = default!;
    [Dependency] private EntityQuery<KsCrewVehicleGunnerComponent> _gunnerQuery = default!;
    [Dependency] private EntityQuery<KsCrewVehicleGunnerSeatComponent> _gunnerSeatQuery = default!;
    [Dependency] private EntityQuery<SpriteComponent> _spriteQuery = default!;

    /// <summary>
    ///     The selected weapon's rounds, for the gunner's ammo alert.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnGetGunnerAlertCount(Entity<KsCrewVehicleGunnerComponent> gunner, ref GetGenericAlertCounterAmountEvent args)
    {
        if (args.Handled || args.Alert.ID != gunner.Comp.AmmoAlert || gunner.Comp.Selected is not { } weaponUid)
            return;

        args.Amount = CountRounds(weaponUid);
    }

    /// <summary>
    ///     A select action's own weapon's rounds, drawn on the action, which is its own icon.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnGetActionCount(Entity<KsCrewVehicleSelectWeaponActionComponent> action, ref GetGenericAlertCounterAmountEvent args)
    {
        if (args.Handled || GetWeapon(action) is not { } weaponUid)
            return;

        args.Amount = CountRounds(weaponUid);
    }

    /// <summary>
    ///     The action's tooltip names its weapon, in this client's language.
    /// </summary>
    /// <remarks>
    ///     A by-value class event: the engine has <c>Entity&lt;T&gt;</c> handlers only for ref events.
    /// </remarks>
    [SubscribeLocalEvent]
    private void OnActionExamined(EntityUid uid, KsCrewVehicleSelectWeaponActionComponent component, ExaminedEvent args)
    {
        if (GetWeapon((uid, component)) is not { } weaponUid)
            return;

        args.PushMarkup(Loc.GetString("ks-crew-vehicle-select-weapon-examine",
            ("weapon", Name(weaponUid)),
            ("rounds", CountRounds(weaponUid))));
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (_playerManager.LocalEntity is not { } playerUid ||
            !_gunnerQuery.TryComp(playerUid, out var gunnerComponent) ||
            !_gunnerSeatQuery.TryComp(gunnerComponent.Seat, out var seatComponent) ||
            seatComponent.SelectActions.Count == 0 ||
            !ProtoMan.TryIndex(gunnerComponent.AmmoAlert, out var alertPrototype))
            return;

        // The stock counter draws the digits, asking the action itself for the number.
        foreach (var actionUid in seatComponent.SelectActions)
        {
            if (!HasComp<GenericCounterAlertComponent>(actionUid) || !_spriteQuery.TryComp(actionUid, out var spriteComponent))
                continue;

            var ev = new UpdateAlertSpriteEvent((actionUid, spriteComponent), actionUid, alertPrototype);
            RaiseLocalEvent(actionUid, ref ev);
        }
    }

    private EntityUid? GetWeapon(Entity<KsCrewVehicleSelectWeaponActionComponent> action)
    {
        // A cycle action stands for whatever its holder has selected.
        if (action.Comp.Cycle)
        {
            return _actionQuery.TryComp(action, out var cycleActionComponent) &&
                   _gunnerQuery.TryComp(cycleActionComponent.AttachedEntity, out var gunnerComponent)
                ? gunnerComponent.Selected
                : null;
        }

        if (!_actionQuery.TryComp(action, out var actionComponent) ||
            actionComponent.Container is not { } seatUid ||
            !_interiorSystem.TryGetVehicle(seatUid, out var vehicle) ||
            !_mountSystem.TryGetWeapon(vehicle.Value.Owner, action.Comp.MountId, out var weaponUid))
            return null;

        return weaponUid;
    }

    private int CountRounds(EntityUid weaponUid)
    {
        var ev = new GetAmmoCountEvent();
        RaiseLocalEvent(weaponUid, ref ev);
        return ev.Count;
    }
}
