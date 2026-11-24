using System.Numerics;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Controllers;
using Robust.Shared.Physics.Systems;

namespace Content.Shared._KS14.CrewVehicle.Drive;

/// <summary>
///     Wheels grip: a driven hull moves only along its facing, never sideways, and doesn't drive into another hull.
/// </summary>
/// <remarks>
///     <para>
///         Runs after the mover, which accelerates along the steered wish but leaves whatever sideways speed a turn
///             left behind, and on a client only for the hull it predicts, its own player's.
///     </para>
///     <para>
///         Two driven hulls are both kinematic controllers, and physics never lets two of those collide, so each would
///             drive through the other. The hull's non-hard contact fixture still touches the other's body: a driven
///             hull loses the part of its velocity that would take it further into another hull it touches. It stops
///             against it, as against a parked one, and shoves neither.
///     </para>
/// </remarks>
public sealed partial class KsCrewVehicleGripController : VirtualController
{
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    [Dependency] private EntityQuery<KsCrewVehicleDriveComponent> _driveQuery = default!;

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
        while (query.MoveNext(out var uid, out var driveComponent, out _, out var physicsComponent))
        {
            if (prediction && !physicsComponent.Predict ||
                physicsComponent.BodyType != BodyType.KinematicController)
            {
                continue;
            }

            var velocity = physicsComponent.LinearVelocity;
            if (velocity.LengthSquared() < MinSpeed * MinSpeed)
                continue;

            var (position, rotation) = _transformSystem.GetWorldPositionRotation(uid);
            var forward = KsCrewVehicleHullFrame.Forward(rotation);
            velocity = forward * Vector2.Dot(velocity, forward);
            velocity = StopAgainstOtherHulls((uid, driveComponent), position, velocity);

            PhysicsSystem.SetLinearVelocity(uid, velocity, body: physicsComponent);
        }
    }

    private Vector2 StopAgainstOtherHulls(Entity<KsCrewVehicleDriveComponent> hull, Vector2 position, Vector2 velocity)
    {
        var contacts = PhysicsSystem.GetContacts(hull.Owner);
        while (contacts.MoveNext(out var contact))
        {
            if (!contact.IsTouching || contact.OurFixture(hull.Owner).Id != hull.Comp.ContactFixture)
                continue;

            var otherUid = contact.OtherEnt(hull.Owner);
            if (!_driveQuery.HasComp(otherUid) || !contact.OtherFixture(hull.Owner).Item2.Hard)
                continue;

            var away = _transformSystem.GetWorldPosition(otherUid) - position;
            if (away.LengthSquared() < MinSpeed * MinSpeed)
                continue;

            var toward = Vector2.Normalize(away);
            var closing = Vector2.Dot(velocity, toward);
            if (closing > 0f)
                velocity -= toward * closing;
        }

        return velocity;
    }
}
