using System.Numerics;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Controllers;

namespace Content.Shared._KS14.CrewVehicle.Drive;

/// <summary>
///     Wheels grip: a driven hull moves only along its facing, never sideways.
/// </summary>
/// <remarks>
///     Runs after the mover, which accelerates along the steered wish but leaves whatever sideways speed a turn left
///         behind, and on a client only for the hull it predicts, its own player's. What a hull drives into is the
///         push controller's, which runs after this.
/// </remarks>
public sealed partial class KsCrewVehicleGripController : VirtualController
{
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    /// <summary>
    ///     Below this, in m/s, the hull is standing still.
    /// </summary>
    private const float MinSpeed = 0.001f;

    public override void Initialize()
    {
        UpdatesAfter.Add(typeof(SharedMoverController));

        base.Initialize();
    }

    public override void UpdateBeforeSolve(bool prediction, float frameTime)
    {
        base.UpdateBeforeSolve(prediction, frameTime);

        // Rarest first: a relay target exists only while someone drives.
        var query = EntityQueryEnumerator<KsCrewVehicleDriveComponent, MovementRelayTargetComponent, PhysicsComponent>();
        while (query.MoveNext(out var uid, out _, out _, out var physicsComponent))
        {
            if (prediction && !physicsComponent.Predict ||
                physicsComponent.BodyType != BodyType.KinematicController)
            {
                continue;
            }

            var velocity = physicsComponent.LinearVelocity;
            if (velocity.LengthSquared() < MinSpeed * MinSpeed)
                continue;

            var forward = KsCrewVehicleHullFrame.Forward(_transformSystem.GetWorldRotation(uid));
            PhysicsSystem.SetLinearVelocity(uid, forward * Vector2.Dot(velocity, forward), body: physicsComponent);
        }
    }
}
