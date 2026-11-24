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
///         The interior's front is grid north, its +Y, so an interior point P is the hull-local point
///             <see cref="InteriorToHull"/> (P - anchor): turned half a circle. At rotation 0 the interior's west wall
///             (the hull's left) is on the world's east.
///     </para>
/// </remarks>
public static class KsCrewVehicleHullFrame
{
    /// <summary>
    ///     The turn from interior-grid axes to hull-local axes: the interior faces +Y, the hull -Y.
    /// </summary>
    public static readonly Angle InteriorToHull = new(Math.PI);

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
    ///     A world point in the hull's local frame.
    /// </summary>
    public static Vector2 WorldToHull(Vector2 hullWorldPosition, Angle hullWorldRotation, Vector2 world)
    {
        return (-hullWorldRotation).RotateVec(world - hullWorldPosition);
    }
}
