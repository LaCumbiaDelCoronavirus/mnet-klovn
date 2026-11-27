using Content.Shared._KS14.CrewVehicle.Interior;
using Content.Shared._KS14.CrewVehicle.Mounts;
using Content.Shared._KS14.CrewVehicle.View;
using Content.Shared._KS14.Weapons.Ranged;
using Content.Shared.ActionBlocker;
using Content.Shared.Actions;
using Content.Shared.Alert;
using Content.Shared.Buckle.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.CrewVehicle.Gunner;

/// <summary>
///     Whoever sits in a gunner seat fires their selected mounted weapon with their ordinary shoot input: this answers
///         the gun system's <see cref="KsGetControlledGunEvent"/> for them, with the weapon as what fires, and refuses a
///         shot they could not have taken.
/// </summary>
/// <remarks>
///     A gunner looks out through the hull (a gunner seat is also a view seat), so the point they click is already on
///         the hull's map. The weapon is the shooter of its own shots: its rounds pass through the hull
///         (<see cref="SharedKsCrewVehicleMountSystem"/> answers which hull), and their damage is the weapon's doing,
///         which the server's kill credit pays to the gunner.
/// </remarks>
public sealed partial class KsCrewVehicleGunnerSystem : EntitySystem
{
    [Dependency] private ActionBlockerSystem _actionBlockerSystem = default!;
    [Dependency] private ActionContainerSystem _actionContainerSystem = default!;
    [Dependency] private AlertsSystem _alertsSystem = default!;
    [Dependency] private SharedActionsSystem _actionsSystem = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private SharedGunSystem _gunSystem = default!;
    [Dependency] private SharedKsCrewVehicleInteriorSystem _interiorSystem = default!;
    [Dependency] private SharedKsCrewVehicleMountSystem _mountSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    [Dependency] private EntityQuery<KsCrewVehicleGunnerComponent> _gunnerQuery = default!;
    [Dependency] private EntityQuery<KsCrewVehicleViewerComponent> _viewerQuery = default!;
    [Dependency] private EntityQuery<KsCrewVehicleViewSeatComponent> _viewSeatQuery = default!;
    [Dependency] private EntityQuery<KsCrewVehicleWeaponComponent> _weaponQuery = default!;
    [Dependency] private EntityQuery<StrapComponent> _strapQuery = default!;
    [Dependency] private EntityQuery<KsCrewVehicleGunnerSeatComponent> _gunnerSeatQuery = default!;
    [Dependency] private EntityQuery<KsCrewVehicleSelectWeaponActionComponent> _selectActionQuery = default!;

    private const string GunnerMountClaim = "gunner-mount";

    /// <summary>
    ///     A seat with several weapons makes one select action per mount, kept in its own action container and handed to
    ///         whoever sits there. Each shows its weapon's rounds: the action is its own icon.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnSeatMapInit(Entity<KsCrewVehicleGunnerSeatComponent> seat, ref MapInitEvent args)
    {
        if (seat.Comp.Mounts.Count < 2)
            return;

        foreach (var mountId in seat.Comp.Mounts)
        {
            EntityUid? actionUid = null;
            if (!_actionContainerSystem.EnsureAction(seat.Owner, ref actionUid, seat.Comp.SelectAction))
                continue;

            var selectComponent = EnsureComp<KsCrewVehicleSelectWeaponActionComponent>(actionUid.Value);
            selectComponent.MountId = mountId;
            Dirty(actionUid.Value, selectComponent);
            _actionsSystem.SetEntityIcon(actionUid.Value, actionUid.Value);
            seat.Comp.SelectActions.Add(actionUid.Value);
        }

        Dirty(seat);
    }

    [SubscribeLocalEvent]
    private void OnStrapped(Entity<KsCrewVehicleGunnerSeatComponent> seat, ref StrappedEvent args)
    {
        // A seat that arrives already occupied raises this again while the client applies it; the server's state
        //      already has the gunner.
        if (_gameTiming.ApplyingState)
            return;

        if (!_interiorSystem.TryGetVehicle(seat.Owner, out var vehicle))
            return;

        var gunnerComponent = new KsCrewVehicleGunnerComponent
        {
            Vehicle = vehicle.Value.Owner,
            Seat = seat.Owner,
        };

        foreach (var mountId in seat.Comp.Mounts)
        {
            if (_mountSystem.TryGetWeapon(vehicle.Value.Owner, mountId, out var weaponUid))
                gunnerComponent.Weapons.Add(weaponUid.Value);
        }

        if (gunnerComponent.Weapons.Count == 0)
            return;

        gunnerComponent.Selected = gunnerComponent.Weapons[0];
        gunnerComponent.AmmoAlert = seat.Comp.AmmoAlert;

        var occupantUid = args.Buckle.Owner;
        if (_gunnerQuery.TryComp(occupantUid, out var oldGunnerComponent))
            RemComp(occupantUid, oldGunnerComponent);

        AddComp(occupantUid, gunnerComponent);
        Dirty(occupantUid, gunnerComponent);

        _alertsSystem.ShowAlert(occupantUid, seat.Comp.AmmoAlert);
        foreach (var actionUid in seat.Comp.SelectActions)
        {
            _actionsSystem.AddActionDirect(occupantUid, actionUid);
        }

        RefreshSelectActions((occupantUid, gunnerComponent));
    }

    /// <summary>
    ///     Standing up, by any route, takes back the seat's actions and the ammo alert.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnGunnerShutdown(Entity<KsCrewVehicleGunnerComponent> gunner, ref ComponentShutdown args)
    {
        // The server's state carries both.
        if (_gameTiming.ApplyingState || TerminatingOrDeleted(gunner.Owner))
            return;

        _alertsSystem.ClearAlert(gunner.Owner, gunner.Comp.AmmoAlert);

        if (!_gunnerSeatQuery.TryComp(gunner.Comp.Seat, out var seatComponent))
            return;

        foreach (var actionUid in seatComponent.SelectActions)
        {
            // Removing an action from someone it isn't attached to logs an error.
            if (_actionsSystem.GetAction(actionUid, logError: false) is { } action && action.Comp.AttachedEntity == gunner.Owner)
                _actionsSystem.RemoveProvidedAction(gunner.Owner, gunner.Comp.Seat, actionUid);
        }
    }

    /// <remarks>
    ///     A by-value class event: the engine has <c>Entity&lt;T&gt;</c> handlers only for ref events.
    /// </remarks>
    [SubscribeLocalEvent]
    private void OnSelectWeapon(EntityUid uid, KsCrewVehicleGunnerSeatComponent component, KsCrewVehicleSelectWeaponActionEvent args)
    {
        if (args.Handled ||
            !_gunnerQuery.TryComp(args.Performer, out var gunnerComponent) ||
            gunnerComponent.Seat != uid ||
            !_selectActionQuery.TryComp(args.Action, out var selectComponent) ||
            !_mountSystem.TryGetWeapon(gunnerComponent.Vehicle, selectComponent.MountId, out var weaponUid) ||
            !gunnerComponent.Weapons.Contains(weaponUid.Value))
            return;

        args.Handled = true;
        if (gunnerComponent.Selected == weaponUid)
            return;

        gunnerComponent.Selected = weaponUid;
        Dirty(args.Performer, gunnerComponent);
        RefreshSelectActions((args.Performer, gunnerComponent));
    }

    /// <summary>
    ///     The selected weapon's action shows as toggled on, the others off.
    /// </summary>
    private void RefreshSelectActions(Entity<KsCrewVehicleGunnerComponent> gunner)
    {
        if (!_gunnerSeatQuery.TryComp(gunner.Comp.Seat, out var seatComponent))
            return;

        foreach (var actionUid in seatComponent.SelectActions)
        {
            var selected = _selectActionQuery.TryComp(actionUid, out var selectComponent) &&
                           _mountSystem.TryGetWeapon(gunner.Comp.Vehicle, selectComponent.MountId, out var weaponUid) &&
                           weaponUid == gunner.Comp.Selected;
            _actionsSystem.SetToggled(actionUid, selected);
        }
    }

    [SubscribeLocalEvent]
    private void OnUnstrapped(Entity<KsCrewVehicleGunnerSeatComponent> seat, ref UnstrappedEvent args)
    {
        if (_gameTiming.ApplyingState)
            return;

        if (_gunnerQuery.TryComp(args.Buckle.Owner, out var gunnerComponent) && gunnerComponent.Seat == seat.Owner)
            RemComp(args.Buckle.Owner, gunnerComponent);
    }

    /// <summary>
    ///     A seated gunner who looks out through the hull fires their selected weapon, from its muzzle, no further than
    ///         it may be aimed.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnGetControlledGun(Entity<KsCrewVehicleGunnerComponent> gunner, ref KsGetControlledGunEvent args)
    {
        if (args.Gun != null ||
            gunner.Comp.LifeStage > ComponentLifeStage.Running ||
            gunner.Comp.Selected is not { } weaponUid ||
            !_weaponQuery.TryComp(weaponUid, out var weaponComponent) ||
            TerminatingOrDeleted(weaponUid) ||
            !_viewerQuery.TryComp(gunner.Owner, out var viewerComponent) ||
            viewerComponent.Vehicle != gunner.Comp.Vehicle)
            return;

        args.Shooter = weaponUid;
        args.Gun = weaponUid;
        args.Muzzle = _mountSystem.GetMuzzleCoordinates((weaponUid, weaponComponent));
        args.MaxRange = weaponComponent.MaxAimRange;
        // Aimed on what the hull stands on: a point on the turning turret, or the turning hull, would swing with any
        //      difference between the gunner's predicted turn and the server's.
        args.AimFrame = Transform(gunner.Comp.Vehicle).ParentUid;
    }

    /// <summary>
    ///     Refuses a mounted weapon's shot that its gunner could not take, or that is aimed off the hull's map. Raised on
    ///         the weapon once as the gun and again as the shooter, which it is too; neither changes anything but the
    ///         event, apart from what the gunner's own refusal shows them.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnShotAttempted(Entity<KsCrewVehicleWeaponComponent> weapon, ref ShotAttemptedEvent args)
    {
        if (args.Cancelled)
            return;

        // A shot a player asked for: the gun only sees the weapon, which can always attack.
        if (_gunSystem.KsGetPredictingUser(args.User) is { } gunnerUid && gunnerUid != args.User)
        {
            if (!_actionBlockerSystem.CanAttack(gunnerUid))
            {
                args.Cancel();
                return;
            }

            // What stops the gunner shooting (pacifism) is on the gunner.
            var gunnerEv = new ShotAttemptedEvent
            {
                User = gunnerUid,
                Used = args.Used,
            };
            RaiseLocalEvent(gunnerUid, ref gunnerEv);
            if (gunnerEv.Cancelled)
            {
                args.Cancel();
                return;
            }
        }

        if (args.Used.Comp.ShootCoordinates is not { } aimCoordinates)
            return;

        if (!_transformSystem.IsValid(aimCoordinates))
        {
            args.Cancel();
            return;
        }

        var aimMap = _transformSystem.ToMapCoordinates(aimCoordinates);
        var transformComponent = Transform(weapon.Owner);
        if (aimMap.MapId != transformComponent.MapID)
        {
            args.Cancel();
            return;
        }

        // Outside its arc: a fixed weapon's, or a turret's, whose aim is kept inside it.
        var direction = aimMap.Position - _transformSystem.GetWorldPosition(transformComponent);
        if (direction.LengthSquared() > 0.0001f && !_mountSystem.IsInFireArc(weapon, direction.ToWorldAngle()))
            args.Cancel();
    }

    [SubscribeLocalEvent]
    private void OnGetInteriorErrors(Entity<KsCrewVehicleGunnerSeatComponent> seat, ref KsGetCrewVehicleInteriorErrorsEvent args)
    {
        var validation = args.Validation;
        var name = ToPrettyString(seat);
        var vehicleUid = args.Vehicle.Owner;

        if (!_strapQuery.HasComp(seat))
            validation.AddError($"gunner seat {name} has no Strap, so nobody can sit in it");

        if (!_viewSeatQuery.HasComp(seat))
            validation.AddError($"gunner seat {name} is not also a view seat, so its gunner could not see what they shoot at");

        if (seat.Comp.Mounts.Count == 0)
            validation.AddError($"gunner seat {name} names no mounts");

        foreach (var mountId in seat.Comp.Mounts)
        {
            if (!_mountSystem.HasMount(vehicleUid, mountId))
            {
                validation.AddError($"gunner seat {name} names mount '{mountId}', which {ToPrettyString(vehicleUid)} does not have");
                continue;
            }

            if (!_mountSystem.TryGetWeapon(vehicleUid, mountId, out _))
                validation.AddError($"gunner seat {name} names mount '{mountId}', which has no weapon on {ToPrettyString(vehicleUid)}");

            if (!validation.TryClaim(GunnerMountClaim, mountId, seat, out var otherSeat))
                validation.AddError($"gunner seat {name} names mount '{mountId}', already {ToPrettyString(otherSeat)}'s: a weapon has one gunner");
        }
    }
}
