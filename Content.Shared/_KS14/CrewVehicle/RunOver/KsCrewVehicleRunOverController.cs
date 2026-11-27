using System.Numerics;
using Content.Shared._KS14.CrewVehicle.Push;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Components;
using Content.Shared.Standing;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Controllers;

namespace Content.Shared._KS14.CrewVehicle.RunOver;

/// <summary>
///     A standing mob a driven hull touches is carried along ahead of it, and can't walk into it: it never moves toward
///         the hull faster than the hull moves toward it. Fast enough, the hull runs it over instead
///         (<see cref="KsCrewVehicleRunOverSystem"/>), and from then it is no longer this one's.
/// </summary>
/// <remarks>
///     <para>
///         Physics can't do it: a driven hull and a mob are both kinematic controllers, which never collide. So after
///             everyone's speed for the step is set (the mover, the grip and the push controllers), the mob's speed along
///             the line from the hull is raised to the hull's own. The hull isn't slowed by it.
///     </para>
///     <para>
///         Only driven hulls are visited, a handful at most, and a mob is written only where it is predicted: on the
///             server, or on its own player's client, so a player walking into a hull doesn't rubber-band.
///     </para>
/// </remarks>
public sealed partial class KsCrewVehicleRunOverController : VirtualController
{
    [Dependency] private KsCrewVehicleRunOverSystem _runOverSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private StandingStateSystem _standingSystem = default!;

    [Dependency] private EntityQuery<MobStateComponent> _mobStateQuery = default!;
    [Dependency] private EntityQuery<PhysicsComponent> _physicsQuery = default!;
    [Dependency] private EntityQuery<Ride.KsCrewVehicleRiderComponent> _riderQuery = default!;

    public override void Initialize()
    {
        UpdatesAfter.Add(typeof(KsCrewVehiclePushController));

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

            var hullPosition = _transformSystem.GetWorldPosition(uid);
            var contacts = PhysicsSystem.GetContacts(uid);
            while (contacts.MoveNext(out var contact))
            {
                if (!contact.IsTouching || contact.OurFixture(uid).Id != runOverComponent.Fixture)
                    continue;

                var (_, otherFixture) = contact.OtherFixture(uid);
                var mobUid = contact.OtherEnt(uid);
                // A struck mob is carried too, at the leading edge, until the server's throw begins: the driver's client
                //      otherwise drives on into it for the round trip the throw takes to arrive. Never after: a throw stopped
                //      at once by a wall leaves the mob struck and down against the hull, and it would ride along on it.
                var awaitingThrow = _runOverSystem.IsAwaitingThrow(mobUid);
                if (!otherFixture.Hard ||
                    !_mobStateQuery.HasComp(mobUid) ||
                    // Riding: its speed is the deck's, and carrying it would fling it off the front.
                    _riderQuery.HasComp(mobUid) ||
                    _runOverSystem.IsUnder(mobUid, uid) ||
                    _runOverSystem.IsStruck(mobUid) && !awaitingThrow ||
                    _standingSystem.IsDown(mobUid) && !awaitingThrow)
                {
                    continue;
                }

                if (KsCrewVehicleHullFrame.TryGetSeparation(PhysicsSystem, _transformSystem, uid, hullPosition, mobUid) is not { } away)
                    continue;

                // Predicted with the hull on its driver's client, so it is seen held at the leading edge, not where the
                //      server had it a round trip ago, inside the hull.
                var movedEvent = new KsCrewVehicleMovesBodyEvent(uid, mobUid);
                RaiseLocalEvent(ref movedEvent);
                Carry(mobUid, away, MathF.Max(0f, Vector2.Dot(hullBody.LinearVelocity, away)), prediction);
            }
        }
    }

    /// <summary>
    ///     Raises <paramref name="mobUid"/>'s speed along <paramref name="away"/> to at least <paramref name="hullSpeed"/>.
    /// </summary>
    private void Carry(EntityUid mobUid, Vector2 away, float hullSpeed, bool prediction)
    {
        if (!_physicsQuery.TryComp(mobUid, out var mobBody) ||
            prediction && !mobBody.Predict)
        {
            return;
        }

        var velocity = mobBody.LinearVelocity;
        var along = Vector2.Dot(velocity, away);
        if (along < hullSpeed)
            PhysicsSystem.SetLinearVelocity(mobUid, velocity + away * (hullSpeed - along), body: mobBody);
    }
}
