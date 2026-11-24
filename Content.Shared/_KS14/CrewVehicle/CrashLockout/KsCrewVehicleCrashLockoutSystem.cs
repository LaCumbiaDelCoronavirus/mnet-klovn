using Content.Shared._KS14.CrewVehicle.Smash;
using Content.Shared._KS14.PopupLocalization;
using Content.Shared.Popups;
using Content.Shared.Vehicle;
using Content.Shared.Vehicle.Components;
using Content.Shared.Vehicle.Systems;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.CrewVehicle.CrashLockout;

/// <summary>
///     A hard crash into a wall stalls a crew vehicle for <see cref="KsCrewVehicleCrashLockoutComponent.Duration"/>:
///         the upstream vehicle system's can-run check says no until it is over.
/// </summary>
public sealed partial class KsCrewVehicleCrashLockoutSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private INetManager _netManager = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private VehicleSystem _vehicleSystem = default!;

    [Dependency] private EntityQuery<VehicleComponent> _vehicleQuery = default!;

    [SubscribeLocalEvent]
    private void OnCrashed(Entity<KsCrewVehicleCrashLockoutComponent> vehicle, ref KsCrewVehicleCrashedEvent args)
    {
        if (args.ImpactSpeed < vehicle.Comp.MinImpactSpeed)
            return;

        var lockedComponent = EnsureComp<KsCrewVehicleCrashLockedComponent>(vehicle.Owner);
        var until = _gameTiming.CurTime + vehicle.Comp.Duration;
        if (until > lockedComponent.Until)
            lockedComponent.Until = until;
        Dirty(vehicle.Owner, lockedComponent);

        _vehicleSystem.RefreshCanRun(vehicle.Owner);

        if (_vehicleQuery.CompOrNull(vehicle.Owner)?.Operator is { } driverUid)
            _popupSystem.PopupEntity(KsPopupMessage.Create(vehicle.Comp.Popup), vehicle.Owner, driverUid, PopupType.MediumCaution);
    }

    [SubscribeLocalEvent]
    private void OnCanRun(Entity<KsCrewVehicleCrashLockedComponent> vehicle, ref VehicleCanRunEvent args)
    {
        // Still stored for the rest of the tick once it is let go, and then it no longer holds anything back.
        if (vehicle.Comp.LifeStage <= ComponentLifeStage.Running)
            args.CanRun = false;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // When it is over is the server's to say; the state that ends it reaches a predicting client as it does anyone.
        if (_netManager.IsClient)
            return;

        var query = EntityQueryEnumerator<KsCrewVehicleCrashLockedComponent>();
        while (query.MoveNext(out var uid, out var lockedComponent))
        {
            if (lockedComponent.LifeStage > ComponentLifeStage.Running || _gameTiming.CurTime < lockedComponent.Until)
                continue;

            // Removed at the end of the tick, as this is enumerating them; shut down now, so the refresh sees it gone.
            RemCompDeferred(uid, lockedComponent);
            _vehicleSystem.RefreshCanRun(uid);
        }
    }
}
