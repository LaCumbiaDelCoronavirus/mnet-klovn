using Robust.Shared.Serialization;

namespace Content.Shared._KS14.CrewVehicle;

/// <summary>
///     Sprite layers of a crew vehicle's hull. An enum because <c>DamageVisuals</c> only targets enum-keyed layers.
/// </summary>
[Serializable, NetSerializable]
public enum KsCrewVehicleVisualLayers : byte
{
    Hull,
    Wheels,
}
