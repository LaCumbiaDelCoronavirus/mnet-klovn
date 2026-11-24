using System.Numerics;
using Content.Shared._KS14.CrewVehicle.Drive;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Components;
using Content.Shared.Standing;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Controllers;

namespace Content.Shared._KS14.CrewVehicle.RunOver;

/// <summary>
///     A driven hull too slow to run a standing mob over is stopped by it, as a parked one is.
/// </summary>
/// <remarks>
///     Physics can't do it: a driven hull and a mob are both kinematic controllers, which never collide. So after the
///         hull's speed for the tick is set (<see cref="KsCrewVehicleGripController"/>), the part of it that would take
///         it further into a standing mob its contact fixture touches is taken away, while that is under
///         <see cref="KsCrewVehicleRunOverComponent.MinSpeed"/>. The mob isn't pushed: it walks into the hull's side and
///         through it, which only a parked hull stops.
/// </remarks>
public sealed partial class KsCrewVehicleRunOverController : VirtualController
{
    [Dependency] private KsCrewVehicleRunOverSystem _runOverSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private StandingStateSystem _standingSystem = default!;

    [Dependency] private EntityQuery<MobStateComponent> _mobStateQuery = default!;

    public override void Initialize()
    {
        UpdatesAfter.Add(typeof(KsCrewVehicleGripController));

        base.Initialize();
    }

    public override void UpdateBeforeSolve(bool prediction, float frameTime)
    {
        base.UpdateBeforeSolve(prediction, frameTime);

        // Rarest first: a relay target exists only while someone drives.
        var query = EntityQueryEnumerator<KsCrewVehicleRunOverComponent, MovementRelayTargetComponent, PhysicsComponent>();
        while (query.MoveNext(out var uid, out var runOverComponent, out _, out var physicsComponent))
        {
            if (prediction && !physicsComponent.Predict ||
                physicsComponent.BodyType != BodyType.KinematicController ||
                physicsComponent.LinearVelocity.LengthSquared() < 0.000001f)
            {
                continue;
            }

            var velocity = physicsComponent.LinearVelocity;
            var position = _transformSystem.GetWorldPosition(uid);
            var blocked = false;

            var contacts = PhysicsSystem.GetContacts(uid);
            while (contacts.MoveNext(out var contact))
            {
                if (!contact.IsTouching || contact.OurFixture(uid).Id != runOverComponent.Fixture)
                    continue;

                var (_, otherFixture) = contact.OtherFixture(uid);
                var mobUid = contact.OtherEnt(uid);
                if (!otherFixture.Hard ||
                    !_mobStateQuery.HasComp(mobUid) ||
                    _runOverSystem.IsUnder(mobUid, uid) ||
                    _standingSystem.IsDown(mobUid))
                {
                    continue;
                }

                var away = _transformSystem.GetWorldPosition(mobUid) - position;
                if (away.LengthSquared() < 0.0001f)
                    continue;

                var toward = Vector2.Normalize(away);
                var closing = Vector2.Dot(velocity, toward);
                if (closing <= 0f || closing >= runOverComponent.MinSpeed)
                    continue;

                velocity -= toward * closing;
                blocked = true;
            }

            if (blocked)
                PhysicsSystem.SetLinearVelocity(uid, velocity, body: physicsComponent);
        }
    }
}
