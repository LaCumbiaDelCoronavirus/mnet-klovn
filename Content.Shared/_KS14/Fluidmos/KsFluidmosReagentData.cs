using Content.Shared.Atmos;

namespace Content.Shared._KS14.Fluidmos;

/// <summary>
///     A reagent's optional Fluidmos block. Every field has a default derived from the reagent itself, so only reagents
///         whose behaviour matters set one.
/// </summary>
[DataDefinition]
public sealed partial class KsFluidmosReagentData
{
    /// <summary>
    ///     Molar mass, in g/mol. A reagent with a linked <see cref="Gas"/> takes its gas's and must not set this; anything
    ///         else defaults to <see cref="KsFluidmos.DefaultMolarMass"/>.
    /// </summary>
    [DataField]
    public float? MolarMass;

    /// <summary>
    ///     Latent heat of vaporisation, in joules per unit, on the game's specific-heat scale. Defaults to
    ///         <see cref="KsPhaseMath.DefaultLatentHeat"/>.
    /// </summary>
    [DataField]
    public float? LatentHeat;

    /// <summary>
    ///     Heat of fusion, in joules per unit. Defaults to <see cref="KsPhaseMath.DefaultFusionHeat"/>.
    /// </summary>
    [DataField]
    public float? FusionHeat;

    /// <summary>
    ///     Moles of vapor a unit becomes. Defaults to one gram per unit over the molar mass.
    /// </summary>
    [DataField]
    public float? MolesPerUnit;

    /// <summary>
    ///     The vapor-pressure curve's steepness, <c>k = ΔS_vap / R</c> (dimensionless, real and per mole). Defaults to
    ///         Trouton's rule, <see cref="KsFluidmos.DefaultVaporizationEntropy"/>; set it from the real heat of
    ///         vaporization, <c>ΔH_vap / (R Tb)</c>, for reagents that break the rule. Never derived from
    ///         <see cref="LatentHeat"/>.
    /// </summary>
    [DataField]
    public float? VaporizationEntropy;

    /// <summary>
    ///     The atmos gas this reagent's vapor is. Its vapor then lives in the mixture's gas moles, spreads across tiles as
    ///         gas, and condenses back into this reagent. Opt-in, and each gas may be linked from one reagent only.
    /// </summary>
    [DataField]
    public Gas? Gas;
}
