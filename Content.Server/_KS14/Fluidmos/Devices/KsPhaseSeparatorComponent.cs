using Content.Shared.Atmos;

namespace Content.Server._KS14.Fluidmos.Devices;

/// <summary>
///     Splits a pipe's contents by phase: gas and every vapor are pumped out of the gas outlet, towards a target
///         pressure, and liquid flows out of the liquid outlet at a set rate. Ice stays behind. With a heater ahead of it,
///         this is a still.
/// </summary>
[RegisterComponent, Access(typeof(KsPhaseSeparatorSystem))]
public sealed partial class KsPhaseSeparatorComponent : Component
{
    [DataField]
    public string InletName = "inlet";

    [DataField]
    public string GasOutletName = "outlet";

    [DataField]
    public string LiquidOutletName = "filter";

    /// <summary>
    ///     The gas outlet is pumped up to this pressure, in kPa, and no further.
    /// </summary>
    [DataField]
    public float TargetPressure = Atmospherics.OneAtmosphere;

    /// <summary>
    ///     Units of liquid per second sent to the liquid outlet.
    /// </summary>
    [DataField]
    public float LiquidRate = 20f;

    /// <summary>
    ///     The liquid outlet is fed no further once it reaches this pressure, in kPa.
    /// </summary>
    [DataField]
    public float MaxLiquidOutletPressure = Atmospherics.MaxOutputPressure;
}
