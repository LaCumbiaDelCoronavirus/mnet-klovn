using Content.Shared._Trauma.Projectiles;
using Content.Shared.Vehicle.Systems;
using Robust.Shared.Physics.Events;

namespace Content.Shared._KS14.Vehicle;

/// <summary>
///     Keeps shots from hitting the vehicle they are fired from: a vehicle operator's, which spawn at the operator and
///         so inside whatever they ride, and those of anything else that says it fires from a vehicle
///         (<see cref="KsGetShooterVehicleEvent"/>), such as a gun mounted on a hull.
/// </summary>
public sealed partial class KsRiderProjectileSystem : EntitySystem
{
    [Dependency] private CrewVehicle.Ride.KsCrewVehicleRideSystem _rideSystem = default!;
    [Dependency] private VehicleSystem _vehicleSystem = default!;

    /// <summary>
    ///     The vehicle <paramref name="shooterUid"/> fires from, whose body its shots pass through: the one it operates,
    ///         the hull it rides on, or whatever <see cref="KsGetShooterVehicleEvent"/> answers.
    /// </summary>
    /// <remarks>
    ///     A rider is asked here, not through the event, which NPCs also ask to find whom to shoot back at: a rider is in
    ///         plain sight, not the hull.
    /// </remarks>
    public EntityUid? GetShooterVehicle(EntityUid shooterUid)
    {
        if (_vehicleSystem.GetOperatedVehicleOrNull(shooterUid) is { } operatedUid)
            return operatedUid;

        if (_rideSystem.GetRiddenHull(shooterUid) is { } riddenUid)
            return riddenUid;

        var ev = new KsGetShooterVehicleEvent();
        RaiseLocalEvent(shooterUid, ref ev);
        return ev.Vehicle;
    }

    /// <summary>
    ///     Whether <paramref name="hullUid"/> is the hull <paramref name="targetUid"/> rides, which a shot aimed at the
    ///         rider passes.
    /// </summary>
    public bool IsRiddenBy(EntityUid hullUid, EntityUid? targetUid)
    {
        return _rideSystem.IsRiderOf(targetUid, hullUid);
    }

    [SubscribeLocalEvent]
    private void OnPlayerShotProjectile(ref PlayerShotProjectileEvent args)
    {
        if (GetShooterVehicle(args.User) is not { } vehicleUid)
            return;

        var riderProjectileComponent = EnsureComp<KsRiderProjectileComponent>(args.Projectile);
        riderProjectileComponent.Vehicle = vehicleUid;
        Dirty(args.Projectile, riderProjectileComponent);
    }

    [SubscribeLocalEvent]
    private void OnPreventCollide(Entity<KsRiderProjectileComponent> entity, ref PreventCollideEvent args)
    {
        if (args.OtherEntity == entity.Comp.Vehicle)
            args.Cancelled = true;
    }
}
