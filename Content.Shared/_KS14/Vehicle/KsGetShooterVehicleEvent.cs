namespace Content.Shared._KS14.Vehicle;

/// <summary>
///     Raised on whatever fires a shot, to ask which vehicle it fires from, so that the shot passes through that vehicle
///         rather than hitting it from inside: a gun mounted on a hull answers with the hull.
/// </summary>
/// <remarks>
///     Get, pure: handlers only fill <see cref="Vehicle"/>, and leave it alone once it is set. Ask through
///         <see cref="KsRiderProjectileSystem.GetShooterVehicle"/>, which also covers a vehicle's operator.
/// </remarks>
[ByRefEvent]
public record struct KsGetShooterVehicleEvent(EntityUid? Vehicle = null);
