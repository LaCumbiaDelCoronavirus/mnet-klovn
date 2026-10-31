using Content.Shared._KS14.Fluidmos;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._KS14.Fluidmos.Devices;

/// <summary>
///     A refrigeration expansion valve: draws liquid only, as if fed from the bottom of a condenser's receiver, and lets
///         it through by the orifice law as far as it is open. Crossing to the low side, the liquid flashes and chills it.
///         In thermostatic mode it opens and closes itself to hold the outlet a set superheat above the refrigerant's
///         boiling point, so the evaporator is fed but never flooded.
/// </summary>
[RegisterComponent, Access(typeof(KsExpansionValveSystem))]
public sealed partial class KsExpansionValveComponent : Component
{
    [DataField]
    public string InletName = "inlet";

    [DataField]
    public string OutletName = "outlet";

    /// <summary>
    ///     From 0 (shut) to 1 (wide open). Scales <see cref="Conductance"/>.
    /// </summary>
    [DataField]
    public float Opening = 0.5f;

    /// <summary>
    ///     Units per second per √kPa when wide open.
    /// </summary>
    [DataField]
    public float Conductance = KsFluidmos.DefaultLiquidConductance;

    [DataField]
    public bool Thermostatic;

    /// <summary>
    ///     Kelvin above the refrigerant's boiling point that thermostatic mode holds the outlet at.
    /// </summary>
    [DataField]
    public float SuperheatSetpoint = 5f;

    [DataField]
    public float MaxSuperheatSetpoint = 50f;

    /// <summary>
    ///     How fast thermostatic mode moves the opening: per kelvin of error, per second.
    /// </summary>
    [DataField]
    public float ThermostaticGain = 0.02f;

    [DataField]
    public TimeSpan UiUpdateInterval = TimeSpan.FromSeconds(1);

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan NextUiUpdate;

    [ViewVariables]
    public float VaporFraction = float.NaN;

    [ViewVariables]
    public float Superheat = float.NaN;

    [ViewVariables]
    public float FlowRate;
}
