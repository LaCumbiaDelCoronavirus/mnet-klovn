using System.Numerics;
using Content.Shared._KS14.Portal;

namespace Content.Shared._KS14.CrewVehicle;

/// <summary>
///     Where a point inside a crew vehicle's interior is in the world outside, and back.
/// </summary>
/// <remarks>
///     An interior point P lands on V + R(θ + π)(P − A): A the interior anchor (the interior point over the hull's
///         centre), V the hull's world position, θ its world rotation, and π <see cref="KsCrewVehicleHullFrame.InteriorToHull"/>,
///         because the interior faces grid north and a hull faces the SS14 way, south at rotation 0.
///     This is a two-sided doorway whose near side is the anchor, turned by the interior's own rotation (see
///         <see cref="GetInteriorRotation"/>: its grid's turn on its map plus the turn of the cabin on its grid), and
///         whose far side is the hull turned by θ + π, so the maths is <see cref="KsPortalTransform"/>'s.
/// </remarks>
public static class KsCrewVehicleInteriorTransform
{
    /// <summary>
    ///     The near side's rotation for the methods here: how far the interior's grid is turned on its map (zero as
    ///         loaded) plus how far the cabin is turned on its grid (<see cref="KsCrewVehicleHullFrame.GetCabinRotation"/>).
    /// </summary>
    public static Angle GetInteriorRotation(Angle gridRotation, Angle cabinRotation)
    {
        return gridRotation + cabinRotation;
    }

    /// <summary>
    ///     The world rotation the interior's axes have when drawn around a hull at <paramref name="hullWorldRotation"/>.
    /// </summary>
    public static Angle GetInteriorWorldRotation(Angle hullWorldRotation)
    {
        return hullWorldRotation + KsCrewVehicleHullFrame.InteriorToHull;
    }

    /// <summary>
    ///     The world point an interior point sits over.
    /// </summary>
    /// <param name="interiorRotation">The interior's rotation, from <see cref="GetInteriorRotation"/>.</param>
    public static Vector2 InteriorToWorld(
        Vector2 interiorPoint,
        Vector2 anchor,
        Vector2 hullWorldPosition,
        Angle hullWorldRotation,
        Angle interiorRotation = default)
    {
        return KsPortalTransform.MapPoint(
            interiorPoint,
            anchor,
            interiorRotation,
            hullWorldPosition,
            GetInteriorWorldRotation(hullWorldRotation));
    }

    /// <summary>
    ///     The interior point that sits over a world point.
    /// </summary>
    /// <param name="interiorRotation">The interior's rotation, from <see cref="GetInteriorRotation"/>.</param>
    public static Vector2 WorldToInterior(
        Vector2 worldPoint,
        Vector2 anchor,
        Vector2 hullWorldPosition,
        Angle hullWorldRotation,
        Angle interiorRotation = default)
    {
        return KsPortalTransform.MapPoint(
            worldPoint,
            hullWorldPosition,
            GetInteriorWorldRotation(hullWorldRotation),
            anchor,
            interiorRotation);
    }

    /// <summary>
    ///     The eye on the vehicle's map that draws every world point on the pixel the interior eye draws the interior
    ///         point over it on: what a backdrop behind the interior is rendered through.
    /// </summary>
    /// <param name="interiorEyeCentre">The interior eye's position plus its offset, on the interior map.</param>
    /// <param name="anchor">The anchor on the interior map.</param>
    /// <param name="interiorRotation">The interior's rotation, from <see cref="GetInteriorRotation"/>.</param>
    public static void GetBackdropEye(
        Vector2 interiorEyeCentre,
        Angle interiorEyeRotation,
        Vector2 anchor,
        Vector2 hullWorldPosition,
        Angle hullWorldRotation,
        out Vector2 eyeCentre,
        out Angle eyeRotation,
        Angle interiorRotation = default)
    {
        KsPortalTransform.GetPortalEye(
            interiorEyeCentre,
            interiorEyeRotation,
            anchor,
            interiorRotation,
            hullWorldPosition,
            GetInteriorWorldRotation(hullWorldRotation),
            out eyeCentre,
            out eyeRotation);
    }

    /// <summary>
    ///     Splits the backdrop eye's view centre into the position its FOV is cast from, the hull's, and the offset that
    ///         keeps the view where it was: the engine casts FOV from an eye's position and centres the view on its
    ///         position plus its offset, which is in world units and not turned with the eye.
    /// </summary>
    /// <param name="eyeCentre">The backdrop eye's centre, from <see cref="GetBackdropEye"/>.</param>
    public static void GetBackdropFovEye(Vector2 hullWorldPosition, Vector2 eyeCentre, out Vector2 position, out Vector2 offset)
    {
        position = hullWorldPosition;
        offset = eyeCentre - hullWorldPosition;
    }
}
