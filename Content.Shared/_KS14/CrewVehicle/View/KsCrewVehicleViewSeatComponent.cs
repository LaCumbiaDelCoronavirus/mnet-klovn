using System.Numerics;
using Robust.Shared.GameStates;

namespace Content.Shared._KS14.CrewVehicle.View;

/// <summary>
///     A seat inside a crew vehicle whose occupant looks out from the hull instead of from where they sit.
/// </summary>
/// <remarks>
///     Networked although it never changes, so that a seat placed in a map file with its components written out, and
///         no prototype, still has it on the client, which predicts sitting down in it.
/// </remarks>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, Access(typeof(KsCrewVehicleViewSystem))]
public sealed partial class KsCrewVehicleViewSeatComponent : Component
{
    /// <summary>
    ///     How far around the hull the occupant is sent while seated, as a PVS scale. Must cover <see cref="Zoom"/>
    ///         (checked when the interior loads) and the cursor lean.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float PvsScale = 1f;

    /// <summary>
    ///     The occupant's zoom while seated, which is also as far as they can zoom out.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Vector2 Zoom = Vector2.One;

    /// <summary>
    ///     How far, in tiles, the view leans toward the cursor while seated. Null for not at all.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float? CursorOffset;

    /// <summary>
    ///     How quickly the view follows the cursor, as <c>EyeCursorOffset</c>'s <c>offsetSpeed</c>.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float CursorOffsetSpeed = 0.5f;
}
