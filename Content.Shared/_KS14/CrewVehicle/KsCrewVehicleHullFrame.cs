using System.Numerics;

namespace Content.Shared._KS14.CrewVehicle;

/// <summary>
///     The one convention every crew vehicle system uses for where things are on a hull, and how the interior lines up
///         with it.
/// </summary>
/// <remarks>
///     <para>
///         Hull-local positions (doorways, mounts, exit landing points) are in the hull entity's own frame, as for any
///             entity: world = hull position + hull world rotation applied to the local offset. The hull faces the
///             SS14 way, <see cref="Forward"/>: south at rotation 0, local -Y.
///     </para>
///     <para>
///         A cabin whose front is grid north, its +Y, has an interior point P at the hull-local point
///             <see cref="InteriorToHull"/> (P - anchor): turned half a circle. At rotation 0 the interior's west wall
///             (the hull's left) is on the world's east.
///     </para>
///     <para>
///         A cabin laid out facing another way on its grid is turned back by its <see cref="GetCabinRotation"/> first:
///             the turn from grid axes to hull axes is <see cref="GetInteriorToHull"/>.
///     </para>
/// </remarks>
public static class KsCrewVehicleHullFrame
{
    /// <summary>
    ///     The turn from interior-grid axes to hull-local axes: the interior faces +Y, the hull -Y.
    /// </summary>
    public static readonly Angle InteriorToHull = new(Math.PI);

    /// <summary>
    ///     The turn that takes grid north to the cabin's front: zero for a cabin facing north, a quarter turn clockwise
    ///         (-90°) for one facing east.
    /// </summary>
    public static Angle GetCabinRotation(Direction cabinFront)
    {
        return cabinFront.ToAngle() - Direction.North.ToAngle();
    }

    /// <summary>
    ///     The turn from interior-grid axes to hull-local axes for a cabin turned <paramref name="cabinRotation"/> on its
    ///         grid: the cabin is turned back to face north, then half a circle.
    /// </summary>
    public static Angle GetInteriorToHull(Angle cabinRotation)
    {
        return InteriorToHull - cabinRotation;
    }

    /// <summary>
    ///     The world direction a hull at <paramref name="hullWorldRotation"/> faces.
    /// </summary>
    public static Vector2 Forward(Angle hullWorldRotation)
    {
        return hullWorldRotation.ToWorldVec();
    }

    /// <summary>
    ///     A hull-local offset in world coordinates.
    /// </summary>
    public static Vector2 HullToWorld(Vector2 hullWorldPosition, Angle hullWorldRotation, Vector2 hullLocal)
    {
        return hullWorldPosition + hullWorldRotation.RotateVec(hullLocal);
    }

    /// <summary>
    ///     The unit direction from <paramref name="hullUid"/> to <paramref name="otherUid"/> across the gap between their
    ///         bodies: the way something blocked by the hull's side or end is pushed back, not toward its centre, which
    ///         would let it slide round the corner and in. Through the centres if their bodies already overlap.
    /// </summary>
    /// <param name="hullPosition">The hull's world position.</param>
    public static Vector2? TryGetSeparation(Robust.Shared.Physics.Systems.SharedPhysicsSystem physicsSystem,
        SharedTransformSystem transformSystem,
        EntityUid hullUid,
        Vector2 hullPosition,
        EntityUid otherUid)
    {
        if (physicsSystem.TryGetNearestPoints(hullUid, otherUid, out var hullPoint, out var otherPoint) &&
            (otherPoint - hullPoint).LengthSquared() > 0.000001f)
        {
            return Vector2.Normalize(otherPoint - hullPoint);
        }

        var away = transformSystem.GetWorldPosition(otherUid) - hullPosition;
        return away.LengthSquared() > 0.000001f ? Vector2.Normalize(away) : null;
    }

    /// <summary>
    ///     A world point in the hull's local frame.
    /// </summary>
    public static Vector2 WorldToHull(Vector2 hullWorldPosition, Angle hullWorldRotation, Vector2 world)
    {
        return (-hullWorldRotation).RotateVec(world - hullWorldPosition);
    }
}
