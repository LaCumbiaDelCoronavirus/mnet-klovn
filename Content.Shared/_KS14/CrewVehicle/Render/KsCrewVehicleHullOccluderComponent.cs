using System.Numerics;

namespace Content.Shared._KS14.CrewVehicle.Render;

/// <summary>
///     The shape a crew vehicle's hull blocks sight and light with, for everyone outside it. Each client builds an
///         <c>Occluder</c> of its own from it on the hull, and switches it off while its own eye is inside that hull.
/// </summary>
/// <remarks>
///     Not networked, and the server builds nothing from it: a server occluder would feed NPC line of sight, and the
///         hull's own polygon lies across every sight line that ends at its centre. Checked when the vehicle's interior
///         loads (<see cref="KsCrewVehicleHullOccluderValidationSystem"/>).
/// </remarks>
[RegisterComponent, Access(typeof(KsCrewVehicleHullOccluderValidationSystem))]
public sealed partial class KsCrewVehicleHullOccluderComponent : Component
{
    /// <summary>
    ///     A convex polygon of 3 to 8 vertices in the hull's local frame (<see cref="KsCrewVehicleHullFrame"/>: the front
    ///         is -Y). Kept just inside the sprite's outline, so the sprite's edge draws over the start of its shadow.
    /// </summary>
    [DataField(required: true)]
    public Vector2[] Polygon = [];
}
