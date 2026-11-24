using Robust.Shared.GameStates;

namespace Content.Shared._KS14.CrewVehicle.Turret;

/// <summary>
///     On a turret while it is turning toward its target: all that the turret system's update walks.
/// </summary>
[RegisterComponent, NetworkedComponent, UnsavedComponent, Access(typeof(KsCrewVehicleTurretSystem))]
public sealed partial class KsCrewVehicleTurretTraversingComponent : Component;
