using System.Numerics;
using Content.Shared._KS14.CrewVehicle.Drive;
using Content.Shared.Movement.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Controllers;

namespace Content.Shared._KS14.CrewVehicle.Push;

/// <summary>
///     A driven hull shoves what it drives into. Physics can't: a driven hull is a kinematic controller, which the solver
///         never lets move a loose (Dynamic) body, only be stopped by it, and which passes straight through another
///         kinematic controller, another driven hull.
/// </summary>
/// <remarks>
///     <para>
///         After everyone's speed for the step is set (the mover, then <see cref="KsCrewVehicleGripController"/>), each
///             thing a driven hull pushes and the hull meet at a common speed along the line between them, as an
///             inelastic collision does: the hull gives away the momentum the object takes, by mass, and the mover speeds
///             it up again next step, so a heavy load is a slow push. Only that line changes: an object struck off-centre
///             slides off along the hull's side.
///     </para>
///     <para>
///         A loose object pushed into a wall is stopped by it and then stops the hull, both by physics. Another driven
///             hull can't stop this one by physics, so if it hasn't got out of the way (it is against a wall, say) and the
///             gap closes to <see cref="KsCrewVehiclePushComponent.StuckDistance"/>, this one stops against it instead.
///     </para>
///     <para>
///         Each body is written only where it is predicted: on the server, or on the client predicting it. The driver's
///             client predicts its hull, and an object it pushes through <see cref="KsCrewVehiclePushedComponent"/>.
///     </para>
/// </remarks>
public sealed partial class KsCrewVehiclePushController : VirtualController
{
    [Dependency] private KsCrewVehiclePushSystem _pushSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    [Dependency] private EntityQuery<KsCrewVehicleDriveComponent> _driveQuery = default!;
    [Dependency] private EntityQuery<MovementRelayTargetComponent> _relayTargetQuery = default!;

    public override void Initialize()
    {
        UpdatesAfter.Add(typeof(KsCrewVehicleGripController));

        base.Initialize();
    }

    public override void UpdateBeforeSolve(bool prediction, float frameTime)
    {
        base.UpdateBeforeSolve(prediction, frameTime);

        // Rarest first: a relay target exists only while someone drives.
        var query = EntityQueryEnumerator<KsCrewVehiclePushComponent, MovementRelayTargetComponent, PhysicsComponent>();
        while (query.MoveNext(out var uid, out var pushComponent, out _, out var hullBody))
        {
            if (hullBody.BodyType != BodyType.KinematicController)
                continue;

            var position = _transformSystem.GetWorldPosition(uid);
            var contacts = PhysicsSystem.GetContacts(uid);
            while (contacts.MoveNext(out var contact))
            {
                if (!contact.IsTouching)
                    continue;

                var otherUid = contact.OtherEnt(uid);
                var (_, otherFixture) = contact.OtherFixture(uid);
                var ourFixtureId = contact.OurFixture(uid).Id;
                var otherBody = contact.OtherBody(uid);

                if (ourFixtureId == pushComponent.Fixture && contact.Hard &&
                    _pushSystem.IsPushable(otherUid, otherBody, otherFixture))
                {
                    // Touching, the bodies' nearest points say nothing about which way; the contact's normal does.
                    contact.GetWorldManifold(PhysicsSystem.GetPhysicsTransform(contact.EntityA),
                        PhysicsSystem.GetPhysicsTransform(contact.EntityB),
                        out var normal);
                    if (contact.EntityA != uid)
                        normal = -normal;

                    Shove((uid, hullBody), position, (otherUid, otherBody), pushComponent, prediction, stuckDistance: null,
                        normal.LengthSquared() > 0.25f ? Vector2.Normalize(normal) : null);
                }
                else if (_driveQuery.TryComp(uid, out var driveComponent) &&
                         ourFixtureId == driveComponent.ContactFixture &&
                         otherFixture.Hard &&
                         otherBody.BodyType == BodyType.KinematicController &&
                         _relayTargetQuery.HasComp(otherUid) &&
                         _driveQuery.HasComp(otherUid) &&
                         // Each pair once on the server; a client resolves it from the hull it predicts.
                         (prediction || uid.Id < otherUid.Id))
                {
                    Shove((uid, hullBody), position, (otherUid, otherBody), pushComponent, prediction, pushComponent.StuckDistance,
                        normal: null);
                }
            }
        }
    }

    /// <summary>
    ///     Brings the hull and what it pushes to a common speed along the line between them, by mass.
    /// </summary>
    /// <param name="stuckDistance">For another driven hull: how close counts as it not getting out of the way.</param>
    /// <param name="normal">The line between them, from the hull, if the contact gives one.</param>
    private void Shove(Entity<PhysicsComponent> hull,
        Vector2 hullPosition,
        Entity<PhysicsComponent> other,
        KsCrewVehiclePushComponent pushComponent,
        bool prediction,
        float? stuckDistance,
        Vector2? normal)
    {
        if ((normal ?? KsCrewVehicleHullFrame.TryGetSeparation(PhysicsSystem, _transformSystem, hull.Owner, hullPosition, other.Owner)) is not { } toward)
            return;

        var hullVelocity = hull.Comp.LinearVelocity;
        var otherVelocity = other.Comp.LinearVelocity;
        var hullAlong = Vector2.Dot(hullVelocity, toward);
        var otherAlong = Vector2.Dot(otherVelocity, toward);
        if (hullAlong - otherAlong <= 0f)
            return;

        var writeHull = !prediction || hull.Comp.Predict;
        var writeOther = !prediction || other.Comp.Predict;

        // Stuck together: neither gets any closer to the other.
        if (stuckDistance is { } stuck &&
            PhysicsSystem.TryGetDistance(hull.Owner, other.Owner, out var gap) &&
            gap <= stuck)
        {
            if (writeHull && hullAlong > 0f)
                PhysicsSystem.SetLinearVelocity(hull.Owner, hullVelocity - toward * hullAlong, body: hull.Comp);
            if (writeOther && otherAlong < 0f)
                PhysicsSystem.SetLinearVelocity(other.Owner, otherVelocity - toward * otherAlong, body: other.Comp);
            return;
        }

        var hullMass = hull.Comp.Mass;
        var otherMass = other.Comp.Mass * pushComponent.PushedMassFactor;
        if (hullMass + otherMass <= 0f)
            return;

        var common = (hullMass * hullAlong + otherMass * otherAlong) / (hullMass + otherMass);

        if (writeHull)
            PhysicsSystem.SetLinearVelocity(hull.Owner, hullVelocity + toward * (common - hullAlong), body: hull.Comp);

        if (writeOther)
            PhysicsSystem.SetLinearVelocity(other.Owner, otherVelocity + toward * (common - otherAlong), body: other.Comp);
    }
}
