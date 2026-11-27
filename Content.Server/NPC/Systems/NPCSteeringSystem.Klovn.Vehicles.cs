using Content.Shared.Vehicle.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;

namespace Content.Server.NPC.Systems;

/// <summary>
///     Vehicles in the way of NPCs that steer.
/// </summary>
public sealed partial class NPCSteeringSystem
{
    [Dependency] private EntityQuery<VehicleComponent> _vehicleQuery = default!;

    /// <summary>
    ///     Whether collision avoidance leaves <paramref name="uid"/> out as a moving body that gets out of the way, a mob:
    ///         a kinematic controller, but not a vehicle. A driven vehicle is one too, and a hull three tiles long, or a
    ///         mech, does not step aside; it is avoided as it is parked.
    /// </summary>
    internal bool KsIsIgnoredController(EntityUid uid, PhysicsComponent physicsComponent)
    {
        return physicsComponent.BodyType == BodyType.KinematicController && !_vehicleQuery.HasComp(uid);
    }
}
