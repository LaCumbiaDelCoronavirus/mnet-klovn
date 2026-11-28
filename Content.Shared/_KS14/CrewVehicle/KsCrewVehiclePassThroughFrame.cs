using System.Numerics;

namespace Content.Shared._KS14.CrewVehicle;

/// <summary>
///     How a round going through a crew vehicle's hull lines up with its cabin: the hull's body box, in hull-local
///         coordinates, laid over the cabin's box, in interior-grid-local ones, outline on outline, whatever their sizes.
/// </summary>
/// <remarks>
///     A hull-local point h is the interior point c_I + S(R⁻¹(h − c_H)): c the boxes' centres, R
///         <see cref="KsCrewVehicleHullFrame.InteriorToHull"/> (a half turn, so axes stay axes), and S each axis scaled by
///         the cabin's half-extent over the hull's. Directions turn and scale the same way, then are made unit again.
/// </remarks>
public static class KsCrewVehiclePassThroughFrame
{
    /// <summary>
    ///     A hull-local point as the interior point over it.
    /// </summary>
    public static Vector2 HullToInterior(Box2 hullBox, Box2 interiorBox, Vector2 hullPoint)
    {
        var turned = (-KsCrewVehicleHullFrame.InteriorToHull).RotateVec(hullPoint - hullBox.Center);
        return interiorBox.Center + turned * GetScale(hullBox, interiorBox);
    }

    /// <summary>
    ///     An interior point as the hull-local point it stands for.
    /// </summary>
    public static Vector2 InteriorToHull(Box2 hullBox, Box2 interiorBox, Vector2 interiorPoint)
    {
        var unscaled = (interiorPoint - interiorBox.Center) / GetScale(hullBox, interiorBox);
        return hullBox.Center + KsCrewVehicleHullFrame.InteriorToHull.RotateVec(unscaled);
    }

    /// <summary>
    ///     A hull-local direction as the unit interior direction it maps to.
    /// </summary>
    public static Vector2 HullDirectionToInterior(Box2 hullBox, Box2 interiorBox, Vector2 hullDirection)
    {
        var turned = (-KsCrewVehicleHullFrame.InteriorToHull).RotateVec(hullDirection);
        return Normalise(turned * GetScale(hullBox, interiorBox));
    }

    /// <summary>
    ///     An interior direction as the unit hull-local direction it maps back to.
    /// </summary>
    public static Vector2 InteriorDirectionToHull(Box2 hullBox, Box2 interiorBox, Vector2 interiorDirection)
    {
        return Normalise(KsCrewVehicleHullFrame.InteriorToHull.RotateVec(interiorDirection / GetScale(hullBox, interiorBox)));
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

    private static Vector2 GetScale(Box2 hullBox, Box2 interiorBox)
    {
        return new Vector2(
            hullBox.Width > 0f ? interiorBox.Width / hullBox.Width : 1f,
            hullBox.Height > 0f ? interiorBox.Height / hullBox.Height : 1f);
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
