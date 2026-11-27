namespace Content.Shared._KS14.CrewVehicle.PassThrough;

/// <summary>
///     On a round that went through a crew vehicle's hull and is flying through its cabin: it passes the interior's own
///         walls and fittings, and hits the hull again from inside where it leaves <see cref="Bounds"/>.
/// </summary>
/// <remarks>
///     Server only: what flies through a cabin is the server's, predicted by nobody.
/// </remarks>
[RegisterComponent, UnsavedComponent, Access(typeof(SharedKsCrewVehiclePassThroughSystem))]
public sealed partial class KsCrewVehiclePassThroughRoundComponent : Component
{
    /// <summary>
    ///     The hull it went into.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public EntityUid Vehicle;

    /// <summary>
    ///     The interior grid it is flying over.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public EntityUid Grid;

    /// <summary>
    ///     The cabin's outline, in <see cref="Grid"/>'s local coordinates: where it reaches the far side.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public Box2 Bounds;
}
