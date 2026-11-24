namespace Content.Shared._KS14.CrewVehicle.Interior;

/// <summary>
///     Someone, or something, tracked as being inside a crew vehicle.
/// </summary>
/// <remarks>
///     Server-side bookkeeping, so not networked: a client finds the vehicle it is in through the interior map's
///         <see cref="KsCrewVehicleInteriorMapComponent"/>.
/// </remarks>
[RegisterComponent, UnsavedComponent, Access(typeof(SharedKsCrewVehicleInteriorSystem))]
public sealed partial class KsCrewVehicleOccupantComponent : Component
{
    /// <summary>
    ///     The vehicle whose interior this is in.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public EntityUid Vehicle;
}
