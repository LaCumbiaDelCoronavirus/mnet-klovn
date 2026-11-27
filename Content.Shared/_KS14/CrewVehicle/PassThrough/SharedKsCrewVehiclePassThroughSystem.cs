using Content.Shared._KS14.CrewVehicle.Interior;
using Content.Shared._KS14.Projectiles;
using Robust.Shared.Physics.Events;

namespace Content.Shared._KS14.CrewVehicle.PassThrough;

/// <summary>
///     A round that penetrates a crew vehicle's hull does not go on outside: it goes into the interior, at the point and
///         the way that match where it hit, hits whoever is in its way there, and hits the hull again from inside at the
///         far side of the cabin, out of which it comes if it can still go on (<see cref="KsCrewVehicleInteriorComponent.PenetrationPassThrough"/>).
/// </summary>
/// <remarks>
///     Entry is decided here, on both sides, so a client's kept copy of its own shot ends at the hull as the server's
///         round does. What flies inside, and out again, is the server's alone (<c>KsCrewVehiclePassThroughSystem</c>).
/// </remarks>
public abstract partial class SharedKsCrewVehiclePassThroughSystem : EntitySystem
{
    [Dependency] private EntityQuery<KsCrewVehiclePassThroughRoundComponent> _roundQuery = default!;
    [Dependency] private EntityQuery<KsCrewVehicleInteriorPartComponent> _interiorPartQuery = default!;

    [SubscribeLocalEvent]
    private void OnPenetrateAttempt(Entity<KsCrewVehicleInteriorComponent> hull, ref KsProjectilePenetrateAttemptEvent args)
    {
        if (args.Cancelled || !hull.Comp.PenetrationPassThrough)
            return;

        // Going out through the hull it is inside: it flies on, and whoever brought it to the far side carries it out.
        if (_roundQuery.TryComp(args.Projectile, out var roundComponent) && roundComponent.Vehicle == hull.Owner)
            return;

        args.Cancelled = true;
        QueueEntry(hull, args);
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
