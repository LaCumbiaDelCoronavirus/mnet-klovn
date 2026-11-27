using System.Numerics;
using Robust.Shared.Map;

namespace Content.Shared._KS14.RayCollision;

/// <summary>
///     Raised on an entity <see cref="KsRayCollisionSystem"/> looks ahead of, after the physics ray along its step, for
///         what it would collide with sooner that the ray cannot see: something with no hard fixture that stands in for one
///         (a lag-compensation ghost). Seeded with the physics ray's answer. A pure Get: handlers only lower
///         <see cref="Fraction"/>, setting <see cref="HitUid"/> with it, and change nothing else.
/// </summary>
/// <param name="MapId">The map the step is on.</param>
/// <param name="Origin">Where the step starts, in world space.</param>
/// <param name="Step">The step, in world space: the entity's velocity over the frame.</param>
/// <param name="Fraction">How far along <see cref="Step"/> the nearest hit so far is: 1 for none.</param>
/// <param name="HitUid">The nearest hit so far, if any.</param>
[ByRefEvent]
public record struct KsRayCollisionLookAheadEvent(MapId MapId, Vector2 Origin, Vector2 Step, float Fraction, EntityUid? HitUid)
{
    /// <summary>
    ///     Takes <paramref name="hitUid"/> at <paramref name="fraction"/> along the step if it is nearer than the nearest
    ///         so far.
    /// </summary>
    public void Offer(EntityUid hitUid, float fraction)
    {
        if (fraction >= Fraction)
            return;

        Fraction = fraction;
        HitUid = hitUid;
    }
}
