namespace Content.Shared._KS14.CrewVehicle.Floor;

/// <summary>
///     On a crew vehicle's hull while its body is awake, so may be moving: what keeps it on the floor visits only these.
///         Each side marks its own bodies, so not networked; runtime state, so not saved.
/// </summary>
[RegisterComponent, UnsavedComponent, Access(typeof(KsCrewVehicleFloorSystem))]
public sealed partial class KsCrewVehicleRollingComponent : Component;
