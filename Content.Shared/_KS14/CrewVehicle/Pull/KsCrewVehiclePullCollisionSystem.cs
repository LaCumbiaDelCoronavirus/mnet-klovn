using Content.Shared._KS14.CrewVehicle.Drive;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Events;
using Robust.Shared.Physics;

namespace Content.Shared._KS14.CrewVehicle.Pull;

/// <summary>
///     Pulling joins the puller and the pulled by a joint that stops the two colliding. A crew vehicle is a solid hull
///         the puller must not be able to walk through, so once a pull of one starts its joint collides again. The joint
///         is networked, so the client's predicted pull and the server's agree.
/// </summary>
public sealed partial class KsCrewVehiclePullCollisionSystem : EntitySystem
{
    [Dependency] private EntityQuery<JointComponent> _jointQuery = default!;

    /// <remarks>
    ///     A by-value class event: the engine has <c>Entity&lt;T&gt;</c> handlers only for ref events.
    /// </remarks>
    [SubscribeLocalEvent]
    private void OnPullStarted(EntityUid uid, KsCrewVehicleDriveComponent component, PullStartedMessage args)
    {
        // Applying a server state brings the joint along as it was set here, so there may be nothing to change.
        if (args.PulledUid != uid
            || !TryComp<PullableComponent>(uid, out var pullableComponent)
            || pullableComponent.PullJointId is not { } jointId
            || !_jointQuery.TryComp(uid, out var jointComponent)
            || !jointComponent.GetJoints.TryGetValue(jointId, out var joint))
        {
            return;
        }

        joint.CollideConnected = true;
    }
}
