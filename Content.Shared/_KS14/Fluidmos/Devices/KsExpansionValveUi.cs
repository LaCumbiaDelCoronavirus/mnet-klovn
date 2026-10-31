using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Fluidmos.Devices;

[Serializable, NetSerializable]
public enum KsExpansionValveUiKey : byte
{
    Key,
}

/// <summary>
///     What the expansion valve's window shows. <see cref="Superheat"/> and <see cref="VaporFraction"/> are NaN while
///         there is no refrigerant to measure.
/// </summary>
[Serializable, NetSerializable]
public sealed class KsExpansionValveBoundUserInterfaceState(
    float opening,
    bool thermostatic,
    float superheatSetpoint,
    float vaporFraction,
    float superheat,
    float flowRate) : BoundUserInterfaceState
{
    /// <summary>
    ///     From 0 (shut) to 1 (wide open).
    /// </summary>
    public readonly float Opening = opening;

    public readonly bool Thermostatic = thermostatic;

    /// <summary>
    ///     Kelvin above its boiling point the thermostatic mode holds the outlet at.
    /// </summary>
    public readonly float SuperheatSetpoint = superheatSetpoint;

    /// <summary>
    ///     Share of the liquid crossing the valve that flashes to vapor, from 0 to 1.
    /// </summary>
    public readonly float VaporFraction = vaporFraction;

    /// <summary>
    ///     Kelvin the outlet sits above the refrigerant's boiling point at the outlet pressure.
    /// </summary>
    public readonly float Superheat = superheat;

    /// <summary>
    ///     Units of liquid per second.
    /// </summary>
    public readonly float FlowRate = flowRate;
}

[Serializable, NetSerializable]
public sealed class KsExpansionValveSetOpeningMessage(float opening) : BoundUserInterfaceMessage
{
    public readonly float Opening = opening;
}

[Serializable, NetSerializable]
public sealed class KsExpansionValveSetThermostaticMessage(bool thermostatic) : BoundUserInterfaceMessage
{
    public readonly bool Thermostatic = thermostatic;
}

[Serializable, NetSerializable]
public sealed class KsExpansionValveSetSuperheatMessage(float superheat) : BoundUserInterfaceMessage
{
    public readonly float Superheat = superheat;
}
