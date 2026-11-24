using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.CrewVehicle.AmmoPort;

/// <summary>
///     Loading a mounted weapon through its ammo port, with the ammunition used on it.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class KsCrewVehicleAmmoPortDoAfterEvent : SimpleDoAfterEvent;
