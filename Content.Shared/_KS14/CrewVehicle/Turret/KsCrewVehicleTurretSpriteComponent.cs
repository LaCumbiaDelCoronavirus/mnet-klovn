using System.Numerics;

namespace Content.Shared._KS14.CrewVehicle.Turret;

/// <summary>
///     A mounted weapon whose sprite turns freely with it but is drawn pivoting on a spot of its hull's sprite, such as a
///         roof hatch, that the hull's art puts somewhere different in each of the four directions it is drawn in. Only
///         the sprite moves: the weapon, and where its shots leave, stay where its mount puts them.
/// </summary>
/// <remarks>
///     Read by the client's <c>KsCrewVehicleTurretSpriteSystem</c>. Prototype data only, so not networked.
/// </remarks>
[RegisterComponent]
public sealed partial class KsCrewVehicleTurretSpriteComponent : Component
{
    /// <summary>
    ///     For each direction the hull's sprite is drawn in, where the pivot is in that drawing, from the hull's centre, in
    ///         tiles, x right and y up as the drawing is seen when the hull faces that way.
    /// </summary>
    [DataField(required: true)]
    public Dictionary<Direction, Vector2> PivotOffsets = new();
}
