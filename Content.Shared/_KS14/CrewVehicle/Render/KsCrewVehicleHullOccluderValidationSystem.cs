using System.Numerics;
using Content.Shared._KS14.CrewVehicle.Interior;
using Robust.Shared.Physics;

namespace Content.Shared._KS14.CrewVehicle.Render;

/// <summary>
///     Refuses a crew vehicle whose <see cref="KsCrewVehicleHullOccluderComponent"/> can't work: a polygon the engine
///         can't use, a networked <c>Occluder</c> beside it, or a shadow-casting light of the hull's inside it.
/// </summary>
/// <remarks>
///     The engine drops a shadow-casting light that sits inside an enabled occluder, so a headlight at the hull's centre
///         would go dark for everyone outside. It is checked whether or not the light is on now, as it can be switched on.
/// </remarks>
public sealed partial class KsCrewVehicleHullOccluderValidationSystem : EntitySystem
{
    [Dependency] private SharedPointLightSystem _pointLightSystem = default!;

    [SubscribeLocalEvent]
    private void OnGetInteriorErrors(Entity<KsCrewVehicleHullOccluderComponent> hull, ref KsGetCrewVehicleInteriorErrorsEvent args)
    {
        // Raised on every entity of the interior too: only the vehicle's own component is this rule's.
        if (hull.Owner != args.Vehicle.Owner)
            return;

        var validation = args.Validation;
        var name = ToPrettyString(hull);
        if (GetPolygonError(hull.Comp.Polygon) is { } polygonError)
            validation.AddError($"hull occluder of {name} {polygonError}");

        // The server never builds one from this component, so any Occluder here is a networked one.
        if (HasComp<OccluderComponent>(hull))
            validation.AddError($"{name} has a networked Occluder as well as a hull occluder");

        if (_pointLightSystem.TryGetLight(hull, out var light) &&
            light.CastShadows &&
            IsInside(hull.Comp.Polygon, light.Offset))
        {
            validation.AddError($"{name}'s shadow-casting PointLight, offset {light.Offset}, is inside its hull occluder, which would put it out for everyone outside");
        }
    }

    /// <summary>
    ///     What is wrong with the polygon, or null if the engine can use it: 3 to 8 finite vertices, strictly convex, in
    ///         one winding.
    /// </summary>
    public static string? GetPolygonError(Vector2[] polygon)
    {
        if (polygon.Length < 3 || polygon.Length > PhysicsConstants.MaxPolygonVertices)
            return $"has {polygon.Length} vertices, not 3 to {PhysicsConstants.MaxPolygonVertices}";

        foreach (var vertex in polygon)
        {
            if (!float.IsFinite(vertex.X) || !float.IsFinite(vertex.Y))
                return $"has a vertex that is not finite, {vertex}";
        }

        var sign = 0;
        for (var i = 0; i < polygon.Length; i++)
        {
            var cross = Cross(polygon[i], polygon[(i + 1) % polygon.Length], polygon[(i + 2) % polygon.Length]);
            if (MathF.Abs(cross) < 1e-6f)
                return $"has three vertices in a line at vertex {(i + 1) % polygon.Length}";

            var turn = MathF.Sign(cross);
            if (sign != 0 && turn != sign)
                return $"is not convex at vertex {(i + 1) % polygon.Length}";

            sign = turn;
        }

        return null;
    }

    /// <summary>
    ///     Whether a point is strictly inside a convex polygon of either winding. A point on an edge is outside.
    /// </summary>
    public static bool IsInside(Vector2[] polygon, Vector2 point)
    {
        if (polygon.Length < 3)
            return false;

        var sign = 0;
        for (var i = 0; i < polygon.Length; i++)
        {
            var turn = MathF.Sign(Cross(polygon[i], polygon[(i + 1) % polygon.Length], point));
            if (turn == 0 || sign != 0 && turn != sign)
                return false;

            sign = turn;
        }

        return true;
    }

    private static float Cross(Vector2 a, Vector2 b, Vector2 c)
    {
        var ab = b - a;
        var bc = c - b;
        return ab.X * bc.Y - ab.Y * bc.X;
    }
}
