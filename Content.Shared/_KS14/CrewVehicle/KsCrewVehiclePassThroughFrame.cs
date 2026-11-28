using System.Numerics;

namespace Content.Shared._KS14.CrewVehicle;

/// <summary>
///     How a round going through a crew vehicle's hull lines up with its cabin: the hull's body box, in hull-local
///         coordinates, laid over the cabin's box, in interior-grid-local ones, outline on outline, whatever their sizes.
/// </summary>
/// <remarks>
///     A hull-local point h is the interior point c_I + S(R⁻¹(h − c_H)): c the boxes' centres, R
///         <see cref="KsCrewVehicleHullFrame.GetInteriorToHull"/> of the cabin's rotation on its grid (a half turn for a
///         cabin facing grid north, three quarters for one facing east: a whole number of quarter turns, so axes stay
///         axes), and S each interior axis scaled by the cabin's half-extent along it over the hull's along the hull axis
///         it lies on. Directions turn and scale the same way, then are made unit again.
/// </remarks>
public static class KsCrewVehiclePassThroughFrame
{
    /// <summary>
    ///     A hull-local point as the interior point over it.
    /// </summary>
    /// <param name="cabinRotation">How far the cabin is turned on its grid (<see cref="KsCrewVehicleHullFrame.GetCabinRotation"/>).</param>
    public static Vector2 HullToInterior(Box2 hullBox, Box2 interiorBox, Vector2 hullPoint, Angle cabinRotation = default)
    {
        var turned = (-KsCrewVehicleHullFrame.GetInteriorToHull(cabinRotation)).RotateVec(hullPoint - hullBox.Center);
        return interiorBox.Center + turned * GetScale(hullBox, interiorBox, cabinRotation);
    }

    /// <summary>
    ///     An interior point as the hull-local point it stands for.
    /// </summary>
    /// <param name="cabinRotation">How far the cabin is turned on its grid (<see cref="KsCrewVehicleHullFrame.GetCabinRotation"/>).</param>
    public static Vector2 InteriorToHull(Box2 hullBox, Box2 interiorBox, Vector2 interiorPoint, Angle cabinRotation = default)
    {
        var unscaled = (interiorPoint - interiorBox.Center) / GetScale(hullBox, interiorBox, cabinRotation);
        return hullBox.Center + KsCrewVehicleHullFrame.GetInteriorToHull(cabinRotation).RotateVec(unscaled);
    }

    /// <summary>
    ///     A hull-local direction as the unit interior direction it maps to.
    /// </summary>
    /// <param name="cabinRotation">How far the cabin is turned on its grid (<see cref="KsCrewVehicleHullFrame.GetCabinRotation"/>).</param>
    public static Vector2 HullDirectionToInterior(Box2 hullBox, Box2 interiorBox, Vector2 hullDirection, Angle cabinRotation = default)
    {
        var turned = (-KsCrewVehicleHullFrame.GetInteriorToHull(cabinRotation)).RotateVec(hullDirection);
        return Normalise(turned * GetScale(hullBox, interiorBox, cabinRotation));
    }

    /// <summary>
    ///     An interior direction as the unit hull-local direction it maps back to.
    /// </summary>
    /// <param name="cabinRotation">How far the cabin is turned on its grid (<see cref="KsCrewVehicleHullFrame.GetCabinRotation"/>).</param>
    public static Vector2 InteriorDirectionToHull(Box2 hullBox, Box2 interiorBox, Vector2 interiorDirection, Angle cabinRotation = default)
    {
        return Normalise(KsCrewVehicleHullFrame.GetInteriorToHull(cabinRotation)
            .RotateVec(interiorDirection / GetScale(hullBox, interiorBox, cabinRotation)));
    }

    /// <summary>
    ///     Where the line through <paramref name="point"/> along <paramref name="direction"/> enters <paramref name="box"/>:
    ///         the point on its outline a round going that way came in by. The point itself, held to the box, if the line
    ///         misses it.
    /// </summary>
    public static Vector2 GetEntry(Box2 box, Vector2 point, Vector2 direction)
    {
        var length = GetReach(box, point);
        var start = point - direction * length;
        return TryClip(start, direction * (length * 2f), box, out var enter, out _)
            ? start + direction * (length * 2f * enter)
            : Clamp(box, point);
    }

    /// <summary>
    ///     Where the line through <paramref name="point"/> along <paramref name="direction"/> leaves <paramref name="box"/>:
    ///         the far side of it. The point itself, held to the box, if the line misses it.
    /// </summary>
    public static Vector2 GetExit(Box2 box, Vector2 point, Vector2 direction)
    {
        var length = GetReach(box, point);
        var start = point - direction * length;
        return TryClip(start, direction * (length * 2f), box, out _, out var exit)
            ? start + direction * (length * 2f * exit)
            : Clamp(box, point);
    }

    /// <summary>
    ///     Whether the line through <paramref name="point"/> along <paramref name="direction"/> goes through
    ///         <paramref name="box"/> at all, rather than only touching it.
    /// </summary>
    public static bool Crosses(Box2 box, Vector2 point, Vector2 direction)
    {
        var length = GetReach(box, point);
        var start = point - direction * length;
        return TryClip(start, direction * (length * 2f), box, out var enter, out var exit) &&
               (exit - enter) * length * 2f > 1e-4f;
    }

    /// <summary>
    ///     The point <paramref name="box"/> is nearest to <paramref name="point"/>, at least <paramref name="inset"/> inside
    ///         its outline (or its centre, along an axis it is too thin for).
    /// </summary>
    public static Vector2 Clamp(Box2 box, Vector2 point, float inset = 0f)
    {
        var insetX = MathF.Min(inset, box.Width / 2f);
        var insetY = MathF.Min(inset, box.Height / 2f);
        return new Vector2(Math.Clamp(point.X, box.Left + insetX, box.Right - insetX),
            Math.Clamp(point.Y, box.Bottom + insetY, box.Top - insetY));
    }

    /// <summary>
    ///     Each interior axis's extent over the extent of the hull along the hull axis it lies on.
    /// </summary>
    private static Vector2 GetScale(Box2 hullBox, Box2 interiorBox, Angle cabinRotation)
    {
        // The hull's box as seen along the interior's axes: its sides swap for a cabin turned a quarter.
        var turn = KsCrewVehicleHullFrame.GetInteriorToHull(cabinRotation);
        var cos = Math.Abs(Math.Cos(turn.Theta));
        var sin = Math.Abs(Math.Sin(turn.Theta));
        var hullWidth = (float)(cos * hullBox.Width + sin * hullBox.Height);
        var hullHeight = (float)(sin * hullBox.Width + cos * hullBox.Height);
        return new Vector2(
            hullWidth > 0f ? interiorBox.Width / hullWidth : 1f,
            hullHeight > 0f ? interiorBox.Height / hullHeight : 1f);
    }

    private static Vector2 Normalise(Vector2 vector)
    {
        return vector.LengthSquared() > 0f ? Vector2.Normalize(vector) : vector;
    }

    /// <summary>
    ///     Longer than any line through the box from as far off as <paramref name="point"/> is.
    /// </summary>
    private static float GetReach(Box2 box, Vector2 point)
    {
        return (box.Size.Length() + (point - box.Center).Length()) * 2f + 1f;
    }

    /// <summary>
    ///     The fractions of <paramref name="step"/> from <paramref name="origin"/> at which the segment enters and leaves
    ///         <paramref name="box"/>.
    /// </summary>
    private static bool TryClip(Vector2 origin, Vector2 step, Box2 box, out float enter, out float exit)
    {
        enter = 0f;
        exit = 1f;
        return ClipAxis(origin.X, step.X, box.Left, box.Right, ref enter, ref exit) &&
               ClipAxis(origin.Y, step.Y, box.Bottom, box.Top, ref enter, ref exit);
    }

    private static bool ClipAxis(float origin, float step, float min, float max, ref float enter, ref float exit)
    {
        if (MathF.Abs(step) < 1e-6f)
            return origin >= min && origin <= max;

        var first = (min - origin) / step;
        var second = (max - origin) / step;
        if (first > second)
            (first, second) = (second, first);

        enter = MathF.Max(enter, first);
        exit = MathF.Min(exit, second);
        return enter <= exit;
    }
}
