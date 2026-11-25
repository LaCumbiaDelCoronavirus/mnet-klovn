using System.Numerics;
using Robust.Shared.GameStates;

namespace Content.Shared._KS14.CrewVehicle.View;

/// <summary>
///     On someone sitting in a <see cref="KsCrewVehicleViewSeatComponent"/>: their eye is on the vehicle outside.
///         Removed when they stand, which puts their eye back.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true), UnsavedComponent]
[Access(typeof(KsCrewVehicleViewSystem))]
public sealed partial class KsCrewVehicleViewerComponent : Component
{
    /// <summary>
    ///     The vehicle they look out from.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public EntityUid Vehicle;

    /// <summary>
    ///     The seat they sit in.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public EntityUid Seat;

    /// <summary>
    ///     Their zoom before they sat down, put back when they stand.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public Vector2 PreviousZoom = Vector2.One;

    /// <summary>
    ///     How far they could zoom out before they sat down, put back when they stand.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public Vector2 PreviousMaxZoom = Vector2.One;

    /// <summary>
    ///     How their view was turned in the interior before they sat down, put back when they stand.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public Angle PreviousRelativeRotation;

    /// <summary>
    ///     Whether the seat gave them their cursor lean, so that standing takes it away again. One they had already is
    ///         left alone.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public bool AddedCursorOffset;
}
