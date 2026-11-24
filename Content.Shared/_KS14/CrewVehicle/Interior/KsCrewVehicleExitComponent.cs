namespace Content.Shared._KS14.CrewVehicle.Interior;

/// <summary>
///     A door inside a crew vehicle's interior, leading out through one of the vehicle's doorways.
/// </summary>
[RegisterComponent, Access(typeof(SharedKsCrewVehicleInteriorSystem))]
public sealed partial class KsCrewVehicleExitComponent : Component
{
    /// <summary>
    ///     The id of the doorway, in <see cref="KsCrewVehicleInteriorComponent.Doorways"/>, that this door leads out of.
    ///         Usually set per door in the interior's map file; one the vehicle lacks refuses the interior when loaded.
    /// </summary>
    [DataField]
    public string Doorway = string.Empty;
}
