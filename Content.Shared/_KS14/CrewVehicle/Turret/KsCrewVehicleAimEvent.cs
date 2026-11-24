using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.CrewVehicle.Turret;

/// <summary>
///     A gunner's client telling their selected turret where they aim. Predictive.
/// </summary>
[Serializable, NetSerializable]
public sealed class KsCrewVehicleAimEvent : EntityEventArgs
{
    public NetEntity Weapon;
    public NetCoordinates Target;
}
