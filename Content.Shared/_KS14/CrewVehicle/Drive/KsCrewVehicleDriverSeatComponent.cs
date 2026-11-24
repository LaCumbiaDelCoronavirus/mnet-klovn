using Robust.Shared.GameStates;

namespace Content.Shared._KS14.CrewVehicle.Drive;

/// <summary>
///     A seat inside a crew vehicle whose occupant drives it. It must also be a view seat, and an interior may have only
///         one; both are checked when the interior loads.
/// </summary>
/// <remarks>
///     Networked although it holds nothing, so that a seat placed in a map file with its components written out, and no
///         prototype, still has it on the client, which predicts sitting down in it.
/// </remarks>
[RegisterComponent, NetworkedComponent, Access(typeof(KsCrewVehicleDriveSystem))]
public sealed partial class KsCrewVehicleDriverSeatComponent : Component;
