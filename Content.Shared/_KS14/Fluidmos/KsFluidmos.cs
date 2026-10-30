using Content.Shared.Atmos;

namespace Content.Shared._KS14.Fluidmos;

/// <summary>
///     Constants for reagents carried inside atmos mixtures. Constants rather than cvars, so the hot path does no lookup.
/// </summary>
public static class KsFluidmos
{
    /// <summary>
    ///     Litres of atmos volume one unit of liquid or solid takes up, for displacement.
    /// </summary>
    public const float LitersPerUnit = 0.75f;

    /// <summary>
    ///     Units of liquid or solid below which an amount is dropped, as <see cref="Atmospherics.GasMinMoles"/> is for gas.
    /// </summary>
    public const float MinLiquidUnits = 0.001f;

    /// <summary>
    ///     Moles of unlinked vapor below which an amount is dropped.
    /// </summary>
    public const float MinVaporMoles = Atmospherics.GasMinMoles;

    /// <summary>
    ///     The least fraction of a mixture's volume always left to its gas, however much liquid it holds.
    /// </summary>
    public const float MinFreeVolumeFraction = 0.02f;

    /// <summary>
    ///     kPa of pressure added per percent of the volume that liquid and solid overfill past
    ///         <see cref="MinFreeVolumeFraction"/>. Makes pumps stop feeding a full net, and overpressure mechanics fire.
    /// </summary>
    public const float HydraulicStiffness = 5000f;

    /// <summary>
    ///     How many moles a unit of liquid counts as in a transfer: tunes how fast liquid flows with the gas.
    /// </summary>
    public const float LiquidPseudoMolesPerUnit = 0.1f;

    /// <summary>
    ///     Grams in a unit, for mole counts. The same convention <c>BoilTileEffect</c> uses.
    /// </summary>
    public const float GramsPerUnit = 1f;

    /// <summary>
    ///     Molar mass, in g/mol, of a reagent that sets none and has no linked gas.
    /// </summary>
    public const float DefaultMolarMass = 100f;

    /// <summary>
    ///     Clausius–Clapeyron's dimensionless <c>ΔH_vap / (R T_b)</c> for a reagent that sets none: Trouton's rule
    ///         (about 88 J/(mol K) over R).
    /// </summary>
    /// <remarks>
    ///     This shapes the vapor-pressure curve only. It is the real molar value, independent of the game-scale latent
    ///         heat per unit, which is what the energy accounting uses: on the game's 1 J/(K u) scale a per-unit heat
    ///         cannot also give the real curve.
    /// </remarks>
    public const float DefaultTroutonConstant = 10.6f;

    /// <summary>
    ///     Hertz–Knudsen evaporation/condensation coefficient inside pipes. A tuning knob, not a physical constant: set so a
    ///         200 L node of water at 300 K comes most of the way to saturation in a few seconds.
    /// </summary>
    public const float HertzKnudsenAlpha = 0.0013f;

    /// <summary>
    ///     Hertz–Knudsen coefficient for a puddle evaporating into its tile, calibrated so water on a dry room-temperature
    ///         tile dries about as fast as upstream evaporation dried it.
    /// </summary>
    public const float PuddleHertzKnudsenAlpha = 0.00002f;

    /// <summary>
    ///     Liquid–gas interface area, in m² per litre of mixture volume (0.5 m² per 200 L pipe node).
    /// </summary>
    public const float PipeInterfaceAreaPerLiter = 0.0025f;

    /// <summary>
    ///     The most, in kelvin, a single phase step's latent heat may change a mixture's temperature by.
    /// </summary>
    public const float MaxPhaseDeltaT = 5f;

    /// <summary>
    ///     Default latent heat of vaporisation per unit, as a multiple of <c>BoilingPoint * SpecificHeat</c>: Trouton's
    ///         rule on the game scale, calibrated to give water exactly <c>BoilTileEffect</c>'s 540 J/u.
    /// </summary>
    public const float LatentHeatRatio = 540f / 373.15f;

    /// <summary>
    ///     Default heat of fusion per unit, as a multiple of <c>MeltingPoint * SpecificHeat</c>: Richards' rule,
    ///         calibrated to water's 334 J/g over 4.18 J/(g K).
    /// </summary>
    public const float FusionHeatRatio = 80f / 273.15f;

    /// <summary>
    ///     Units of liquid a device buffers before spilling them as a puddle.
    /// </summary>
    public const float SpillThresholdUnits = 1f;

    /// <summary>
    ///     Units of unlinked vapor a device buffers before releasing them as smoke.
    /// </summary>
    public const float SmokeThresholdUnits = 0.5f;

    /// <summary>
    ///     Boiling points at or above this are "never boils" sentinels (UnstableMutagen), not temperatures.
    /// </summary>
    public const float NonVolatileBoilingPoint = 1e6f;

    /// <summary>
    ///     Saturation pressure, in kPa, below which a reagent is treated as not evaporating at all.
    /// </summary>
    public const float MinActivePressure = 0.01f;

    /// <summary>
    ///     Fraction past saturation linked vapor on a tile must reach before it condenses, so a puddle and its tile do
    ///         not oscillate.
    /// </summary>
    public const float SupersaturationMargin = 0.05f;
}
