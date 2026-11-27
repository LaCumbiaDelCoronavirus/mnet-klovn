using Content.Shared.Climbing.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Controllers;

namespace Content.Shared._KS14.CrewVehicle.Ride;

/// <summary>
///     Slides a rider that has just climbed on to its place on the deck, in the hull's frame, and gets riders off after
///         the solve: those the step's contacts found walking off the edge, and those a crash threw off. Reparenting from
///         inside the contact callbacks that find them is unsafe (CONTRIBUTING §6). Done the same tick, so every
///         re-prediction of it does the same.
/// </summary>
public sealed partial class KsCrewVehicleRideController : VirtualController
{
    [Dependency] private KsCrewVehicleRideSystem _rideSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    public override void UpdateBeforeSolve(bool prediction, float frameTime)
    {
        base.UpdateBeforeSolve(prediction, frameTime);

        // Rarest first: riders.
        var query = EntityQueryEnumerator<KsCrewVehicleRiderComponent, ClimbingComponent, PhysicsComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var riderComponent, out var climbingComponent, out var physicsComponent, out var transformComponent))
        {
            if (climbingComponent.NextTransition == null ||
                transformComponent.ParentUid != riderComponent.Hull ||
                prediction && !physicsComponent.Predict)
            {
                continue;
            }

            var local = transformComponent.LocalPosition;
            var toTarget = riderComponent.MountTarget - local;
            var step = climbingComponent.TransitionRate * frameTime;
            var distance = toTarget.Length();
            if (distance < 0.0001f)
                continue;

            _transformSystem.SetLocalPosition(uid,
                distance <= step ? riderComponent.MountTarget : local + toTarget / distance * step,
                transformComponent);
        }
    }

    public override void UpdateAfterSolve(bool prediction, float frameTime)
    {
        base.UpdateAfterSolve(prediction, frameTime);

        _rideSystem.ProcessDeferred();
    }
}
