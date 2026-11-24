namespace Content.Shared._KS14.CrewVehicle.View;

/// <summary>
///     Raised on an occupant of a crew vehicle to ask how far around the hull they should be sent, as a PVS scale.
///         Pure: handlers only widen <see cref="Scale"/>, with <see cref="System.MathF.Max(float, float)"/>.
/// </summary>
[ByRefEvent]
public record struct KsGetCrewVehicleOutsidePvsScaleEvent(float Scale);

/// <summary>
///     Raised on an occupant of a crew vehicle whenever something that answers
///         <see cref="KsGetCrewVehicleOutsidePvsScaleEvent"/> for them changes its answer, so that the scale is asked
///         for again.
/// </summary>
[ByRefEvent]
public readonly record struct KsCrewVehicleOutsideViewChangedEvent;
