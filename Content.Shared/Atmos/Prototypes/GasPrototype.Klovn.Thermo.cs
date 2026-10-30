namespace Content.Shared.Atmos.Prototypes;

public sealed partial class GasPrototype
{
    /// <summary>
    ///     Van der Waals attraction constant, in kPa L²/mol². Used only as a thermal correction (Joule–Thomson at
    ///         throttles) and for the critical temperature; pressure stays ideal-gas everywhere. Null is an ideal gas.
    /// </summary>
    [DataField]
    public float? KsVanDerWaalsA;

    /// <summary>
    ///     Van der Waals excluded volume, in L/mol. Null is an ideal gas.
    /// </summary>
    [DataField]
    public float? KsVanDerWaalsB;
}
