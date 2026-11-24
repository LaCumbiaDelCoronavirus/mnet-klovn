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
///     A driven hull and a standing mob it touches stop against each other, as a parked hull and a mob do: the hull
///         while it is too slow to run the mob over, the mob always.
/// </summary>
/// <remarks>
///     <para>
///         Physics can't do it: a driven hull and a mob are both kinematic controllers, which never collide. So after
///             everyone's speed for the step is set (the mover, then <see cref="KsCrewVehicleGripController"/>), the part
///             of each one's velocity that would take it further into the other is taken away, for every standing mob a
///             driven hull's contact fixture touches. Neither pushes the other.
///     </para>
///     <para>
///         Only driven hulls are visited, a handful at most. On a client, the hull is stopped only if the client
///             predicts the hull (its driver's), and a mob only if it predicts the mob (its own player): each is
///             stopped where it is predicted, so neither its driver nor a player walking into it rubber-bands.
///     </para>
/// </remarks>
public sealed partial class KsCrewVehicleRunOverController : VirtualController
{
    [Dependency] private KsCrewVehicleRunOverSystem _runOverSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private StandingStateSystem _standingSystem = default!;

    [Dependency] private EntityQuery<MobStateComponent> _mobStateQuery = default!;
    [Dependency] private EntityQuery<PhysicsComponent> _physicsQuery = default!;

    /// <summary>
    ///     Speeds below this, in m/s, are standing still.
    /// </summary>
    private const float MinSpeed = 0.001f;

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
        while (query.MoveNext(out var uid, out var runOverComponent, out _, out var hullBody))
        {
            if (hullBody.BodyType != BodyType.KinematicController)
                continue;

            var stopHull = !prediction || hullBody.Predict;
            var hullVelocity = hullBody.LinearVelocity;
            var hullBlocked = false;
            var hullPosition = _transformSystem.GetWorldPosition(uid);

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

                if (KsCrewVehicleHullFrame.TryGetSeparation(PhysicsSystem, _transformSystem, uid, hullPosition, mobUid) is not { } toward)
                    continue;

                if (stopHull)
                {
                    var closing = Vector2.Dot(hullVelocity, toward);
                    if (closing > 0f && closing < runOverComponent.MinSpeed)
                    {
                        hullVelocity -= toward * closing;
                        hullBlocked = true;
                    }
                }

                StopMob(mobUid, -toward, prediction);
            }

            if (hullBlocked)
                PhysicsSystem.SetLinearVelocity(uid, hullVelocity, body: hullBody);
        }
    }

    /// <summary>
    ///     Takes away the part of <paramref name="mobUid"/>'s velocity along <paramref name="towardHull"/>.
    /// </summary>
    private void StopMob(EntityUid mobUid, Vector2 towardHull, bool prediction)
    {
        if (!_physicsQuery.TryComp(mobUid, out var mobBody) ||
            prediction && !mobBody.Predict)
        {
            return;
        }

        var velocity = mobBody.LinearVelocity;
        var closing = Vector2.Dot(velocity, towardHull);
        if (closing > 0f)
            PhysicsSystem.SetLinearVelocity(mobUid, velocity - towardHull * closing, body: mobBody);
    }
}
