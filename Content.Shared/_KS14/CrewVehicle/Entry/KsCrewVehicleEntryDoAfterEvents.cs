using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.CrewVehicle.Entry;

/// <summary>
///     Getting into a crew vehicle through a doorway, raised on the vehicle.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class KsCrewVehicleEnterDoAfterEvent : SimpleDoAfterEvent;

/// <summary>
///     Getting out of a crew vehicle, raised on the exit door.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class KsCrewVehicleExitDoAfterEvent : SimpleDoAfterEvent;
