namespace Content.Shared._KS14.CrewVehicle.Interior;

/// <summary>
///     Raised on a crew vehicle when something starts being tracked as inside it.
/// </summary>
[ByRefEvent]
public readonly record struct KsCrewVehicleEnteredEvent(EntityUid Occupant);

/// <summary>
///     Raised on a crew vehicle when something stops being tracked as inside it, however it left: out of a door,
///         thrown clear, teleported or deleted.
/// </summary>
[ByRefEvent]
public readonly record struct KsCrewVehicleExitedEvent(EntityUid Occupant);
