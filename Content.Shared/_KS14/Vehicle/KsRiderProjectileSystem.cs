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
    [Dependency] private VehicleSystem _vehicleSystem = default!;

    /// <summary>
    ///     The vehicle <paramref name="shooterUid"/> fires from, whose body its shots pass through: the one it operates,
    ///         or whatever <see cref="KsGetShooterVehicleEvent"/> answers.
    /// </summary>
    public EntityUid? GetShooterVehicle(EntityUid shooterUid)
    {
        if (_vehicleSystem.GetOperatedVehicleOrNull(shooterUid) is { } operatedUid)
            return operatedUid;

        var ev = new KsGetShooterVehicleEvent();
        RaiseLocalEvent(shooterUid, ref ev);
        return ev.Vehicle;
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
