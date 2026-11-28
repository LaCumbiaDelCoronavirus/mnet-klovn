using Content.Shared._KS14.CrewVehicle.Interior;
using Content.Shared._KS14.Projectiles;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Events;

namespace Content.Shared._KS14.CrewVehicle.PassThrough;

/// <summary>
///     A round that penetrates a crew vehicle's hull does not go on outside: it goes into the interior, at the point and
///         the way that match where it hit, hits whoever is in its way there, and hits the hull again from inside at the
///         far side of the cabin, out of which it comes if it can still go on (<see cref="KsCrewVehicleInteriorComponent.PenetrationPassThrough"/>).
///         One whose way misses the crew compartment (<see cref="KsCrewVehicleInteriorComponent.PassThroughHullBounds"/>)
///         goes on outside instead.
/// </summary>
/// <remarks>
///     Entry is decided here, on both sides, so a client's kept copy of its own shot ends at the hull as the server's
///         round does. What flies inside, and out again, is the server's alone (<c>KsCrewVehiclePassThroughSystem</c>).
/// </remarks>
public abstract partial class SharedKsCrewVehiclePassThroughSystem : EntitySystem
{
    [Dependency] private EntityQuery<KsCrewVehiclePassThroughRoundComponent> _roundQuery = default!;
    [Dependency] private EntityQuery<KsCrewVehicleInteriorPartComponent> _interiorPartQuery = default!;
    [Dependency] private EntityQuery<FixturesComponent> _fixturesQuery = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    /// <summary>
    ///     The hull fixture a round goes through, if the hull has one by this name; otherwise its first hard one.
    /// </summary>
    private const string HullFixture = "fix1";

    [SubscribeLocalEvent]
    private void OnPenetrateAttempt(Entity<KsCrewVehicleInteriorComponent> hull, ref KsProjectilePenetrateAttemptEvent args)
    {
        if (args.Cancelled || !hull.Comp.PenetrationPassThrough)
            return;

        // Going out through the hull it is inside: it flies on, and whoever brought it to the far side carries it out.
        if (_roundQuery.TryComp(args.Projectile, out var roundComponent) && roundComponent.Vehicle == hull.Owner)
            return;

        // Through the engine bay or the side armour, clear of the crew compartment: it goes on outside.
        if (!CrossesCompartment(hull, args))
            return;

        args.Cancelled = true;
        QueueEntry(hull, args);
    }

    /// <summary>
    ///     Whether the round's way through the hull goes through the part of it the cabin is
    ///         (<see cref="KsCrewVehicleInteriorComponent.PassThroughHullBounds"/>).
    /// </summary>
    private bool CrossesCompartment(Entity<KsCrewVehicleInteriorComponent> hull, KsProjectilePenetrateAttemptEvent args)
    {
        if (hull.Comp.PassThroughHullBounds is not { } compartment)
            return true;

        var speed = args.WorldVelocity.Length();
        if (speed < 0.001f)
            return true;

        var (hullPosition, hullRotation) = _transformSystem.GetWorldPositionRotation(hull.Owner);
        return KsCrewVehiclePassThroughFrame.Crosses(compartment,
            KsCrewVehicleHullFrame.WorldToHull(hullPosition, hullRotation, args.Point.Position),
            (-hullRotation).RotateVec(args.WorldVelocity / speed));
    }

    /// <summary>
    ///     The hull's body, in its own frame: what a round goes through.
    /// </summary>
    protected bool TryGetHullBox(EntityUid hullUid, out Box2 box)
    {
        box = default;
        if (!_fixturesQuery.TryComp(hullUid, out var fixturesComponent))
            return false;

        if (!fixturesComponent.Fixtures.TryGetValue(HullFixture, out var fixture) || !fixture.Hard)
        {
            fixture = null;
            foreach (var candidate in fixturesComponent.Fixtures.Values)
            {
                if (!candidate.Hard)
                    continue;

                fixture = candidate;
                break;
            }
        }

        if (fixture == null)
            return false;

        box = fixture.Shape.ComputeAABB(Robust.Shared.Physics.Transform.Empty, 0);
        return box.Width > 0f && box.Height > 0f;
    }

    /// <summary>
    ///     The part of the hull's body, in its own frame, that the cabin is laid over: its crew compartment, or all of it.
    /// </summary>
    protected static Box2 GetCompartment(KsCrewVehicleInteriorComponent interiorComponent, Box2 hullBox)
    {
        return interiorComponent.PassThroughHullBounds ?? hullBox;
    }

    /// <summary>
    ///     A round that has gone through the hull, ending outside, is to go on inside. Called from a hit, which may be inside
    ///         a physics callback.
    /// </summary>
    protected virtual void QueueEntry(Entity<KsCrewVehicleInteriorComponent> hull, KsProjectilePenetrateAttemptEvent args)
    {
    }

    /// <summary>
    ///     Inside, it flies through the interior's own walls, windows, seats and fittings: the hull is what it went through.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnRoundPreventCollide(Entity<KsCrewVehiclePassThroughRoundComponent> round, ref PreventCollideEvent args)
    {
        if (_interiorPartQuery.HasComp(args.OtherEntity))
            args.Cancelled = true;
    }

    /// <summary>
    ///     Marks <paramref name="roundUid"/> as flying through <paramref name="hullUid"/>'s cabin.
    /// </summary>
    protected void MarkRound(EntityUid roundUid, EntityUid hullUid, EntityUid gridUid, Box2 bounds)
    {
        var roundComponent = EnsureComp<KsCrewVehiclePassThroughRoundComponent>(roundUid);
        roundComponent.Vehicle = hullUid;
        roundComponent.Grid = gridUid;
        roundComponent.Bounds = bounds;
    }
}
