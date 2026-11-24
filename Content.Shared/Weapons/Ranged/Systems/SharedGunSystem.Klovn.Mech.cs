using Content.Shared.Mech.Components;
using Content.Shared.Vehicle.Components;

namespace Content.Shared.Weapons.Ranged.Systems;

public abstract partial class SharedGunSystem
{
    [Dependency] private EntityQuery<MechComponent> _ksMechQuery = default!;
    [Dependency] private EntityQuery<VehicleComponent> _ksVehicleQuery = default!;

    /// <summary>
    ///     The entity whose client predicts a shot fired by <paramref name="shooterUid"/>: the requesting player for a
    ///         shot fired as something else (a controlled gun, see <see cref="_KS14.Weapons.Ranged.KsGetControlledGunEvent"/>, while its request
    ///         is handled), a mech's pilot for the mech, and otherwise the shooter itself. Such a shot is fired as an
    ///         entity no player is attached to, so what is kept from the predicting client (the server's gunshot, its
    ///         muzzle flash) or shown only to it (an empty-gun popup), and whose ping a shot is compensated by, goes by
    ///         this instead.
    /// </summary>
    public EntityUid? KsGetPredictingUser(EntityUid? shooterUid)
    {
        if (shooterUid != null && shooterUid == _ksShotShooter)
            return _ksShotPlayer;

        if (shooterUid is { } mechUid &&
            _ksMechQuery.HasComp(mechUid) &&
            _ksVehicleQuery.TryComp(mechUid, out var vehicleComponent) &&
            vehicleComponent.Operator is { } pilotUid)
        {
            return pilotUid;
        }

        return shooterUid;
    }
}
