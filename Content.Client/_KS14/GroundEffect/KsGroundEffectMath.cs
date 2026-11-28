using System.Numerics;
using Content.Client._KS14.Particles;
using Robust.Shared.Map;

namespace Content.Client._KS14.GroundEffect;

/// <summary>
///     The arithmetic of <see cref="KsGroundEffectSystem"/>, apart from any entity, so it can be tested on its own.
/// </summary>
public static class KsGroundEffectMath
{
    /// <summary>
    ///     Particles per metre a contact point moving at <paramref name="speed"/> throws up: none below
    ///         <paramref name="minSpeed"/>, <paramref name="slowFactor"/> of <paramref name="perMetre"/> at it, rising
    ///         smoothly to all of it at <paramref name="fullSpeed"/> and above.
    /// </summary>
    public static float Density(float perMetre, float minSpeed, float fullSpeed, float slowFactor, float speed)
    {
        if (perMetre <= 0f || speed < minSpeed)
            return 0f;

        var fraction = SmoothStep(minSpeed, fullSpeed, speed);
        var slow = Math.Clamp(slowFactor, 0f, 1f);
        return perMetre * (slow + (1f - slow) * fraction);
    }

    /// <summary>
    ///     Hermite smoothstep of <paramref name="x"/> between two edges: 0 at or below the first, 1 at or above the
    ///         second (and everywhere if they meet).
    /// </summary>
    public static float SmoothStep(float edge0, float edge1, float x)
    {
        if (edge1 <= edge0)
            return x >= edge1 ? 1f : 0f;

        var t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    /// <summary>
    ///     How many whole particles <paramref name="distance"/> metres at <paramref name="density"/> per metre owe,
    ///         carrying the fraction in <paramref name="accumulator"/>, so many short frames add up to what one long one
    ///         would. At most <paramref name="cap"/>; what the cap cuts is dropped, not owed later.
    /// </summary>
    public static int TakeEmitCount(ref float accumulator, float density, float distance, int cap)
    {
        accumulator += density * distance;
        var owed = (int)accumulator;
        accumulator -= owed;
        return Math.Clamp(owed, 0, Math.Max(0, cap));
    }

    /// <summary>
    ///     Moves a smoothed speed toward a new sample, by as much as <paramref name="dt"/> seconds of an exponential
    ///         with time constant <paramref name="tau"/> would: the same after a second at any frame rate.
    /// </summary>
    public static float SmoothSpeed(float previous, float sample, float dt, float tau)
    {
        if (tau <= 0f)
            return sample;

        return previous + (sample - previous) * (1f - MathF.Exp(-dt / tau));
    }

    /// <summary>
    ///     How fast the particles of a contact point at <paramref name="speed"/> are thrown, against the prototype's.
    /// </summary>
    public static float ThrowSpeedScale(float speed, float fullSpeed)
    {
        return fullSpeed <= 0f ? 1f : Math.Clamp(speed / fullSpeed, 0.3f, 1.5f);
    }

    /// <summary>
    ///     Whether <paramref name="position"/> on <paramref name="mapId"/> is within <paramref name="margin"/> of any of
    ///         the first <paramref name="count"/> <paramref name="views"/>. No views is false; an overflowed list,
    ///         <paramref name="failOpen"/>, is true.
    /// </summary>
    public static bool IsInAnyView(KsParticleView[] views, int count, bool failOpen, MapId mapId, Vector2 position, float margin)
    {
        if (failOpen)
            return true;

        count = Math.Min(count, views.Length);
        for (var i = 0; i < count; i++)
        {
            ref readonly var view = ref views[i];
            if (view.MapId != mapId)
                continue;

            var box = view.WorldAABB;
            if (position.X >= box.Left - margin && position.X <= box.Right + margin &&
                position.Y >= box.Bottom - margin && position.Y <= box.Top + margin)
                return true;
        }

        return false;
    }
}
