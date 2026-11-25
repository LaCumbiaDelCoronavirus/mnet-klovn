using Content.Shared._KS14.CrewVehicle.Drive;
using Content.Shared._KS14.CrewVehicle.RunOver;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Controllers;

namespace Content.Shared._KS14.CrewVehicle.Floor;

/// <summary>
///     Stops a hull at the edge of the floor as at a wall: a step that would put more of its rim over no floor is
///         shortened until it doesn't, or not taken.
/// </summary>
/// <remarks>
///     <para>
///         Runs last, after everyone's speed for the step is set (mover, grip, push, run-over), so a hull pushed by
///             another stops at the edge too, and the one pushing it then stops against it. Driven hulls, parked ones
///             being pushed and parked ones coasting alike: any hull that is moving. One standing still costs a velocity
///             check and no tile lookup.
///     </para>
///     <para>
///         A refused step leaves the hull where it is, so it doesn't jitter at the edge: the next step is refused alike.
///             Written only where the hull is predicted, as the other controllers do.
///     </para>
/// </remarks>
public sealed partial class KsCrewVehicleFloorController : VirtualController
{
    [Dependency] private KsCrewVehicleFloorSystem _floorSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    /// <summary>
    ///     How many times a refused step is halved before giving it up: it stops within an eighth of a step of the edge.
    /// </summary>
    private const int Halvings = 3;

    /// <summary>
    ///     Below this, in m/s, the hull is standing still.
    /// </summary>
    private const float MinSpeed = 0.0001f;

    public override void Initialize()
    {
        UpdatesAfter.Add(typeof(KsCrewVehicleRunOverController));

        base.Initialize();
    }

    public override void UpdateBeforeSolve(bool prediction, float frameTime)
    {
        base.UpdateBeforeSolve(prediction, frameTime);

        // Rarest first: a handful of hulls.
        var query = EntityQueryEnumerator<KsCrewVehicleDriveComponent, PhysicsComponent>();
        while (query.MoveNext(out var uid, out var driveComponent, out var physicsComponent))
        {
            if (prediction && !physicsComponent.Predict ||
                !physicsComponent.Awake ||
                physicsComponent.LinearVelocity.LengthSquared() < MinSpeed * MinSpeed ||
                !_floorSystem.KeepsToFloor((uid, driveComponent)))
            {
                continue;
            }

            var hull = new Entity<KsCrewVehicleDriveComponent>(uid, driveComponent);
            var (position, rotation) = _transformSystem.GetWorldPositionRotation(uid);
            var velocity = physicsComponent.LinearVelocity;
            var offNow = _floorSystem.OffFloorCount(hull, position, rotation, out var overhang);

            // Already hanging over, only away from the overhang: between two of the rim's points it could otherwise
            //      creep further off a little at a time.
            var allowed = offNow == 0 || System.Numerics.Vector2.Dot(velocity, overhang) <= 0f;
            var scale = 1f;
            for (var halving = 0; allowed && halving <= Halvings; halving++, scale *= 0.5f)
            {
                if (_floorSystem.OffFloorCount(hull, position + velocity * (scale * frameTime), rotation, out _) <= offNow)
                    break;

                if (halving == Halvings)
                    allowed = false;
            }

            if (allowed && scale >= 1f)
                continue;

            PhysicsSystem.SetLinearVelocity(uid, allowed ? velocity * scale : System.Numerics.Vector2.Zero, body: physicsComponent);
        }
    }
}
