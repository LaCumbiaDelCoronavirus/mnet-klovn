namespace Content.Shared._KS14.CrewVehicle.Interior;

/// <summary>
///     A piece of a crew vehicle's interior: a seat, the ammo loader, the floor plan. It goes with the interior map when
///         the vehicle is lost, rather than being thrown out beside it like something left lying about, even though some
///         pieces are unanchored so they can sit off the tile grid where the art has them.
/// </summary>
[RegisterComponent]
public sealed partial class KsCrewVehicleInteriorPartComponent : Component;
