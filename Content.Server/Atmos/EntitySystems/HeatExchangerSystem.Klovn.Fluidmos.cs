using Content.Server._KS14.Fluidmos;
using Content.Server.Atmos.Components;
using Content.Shared.Atmos;

namespace Content.Server.Atmos.EntitySystems;

public sealed partial class HeatExchangerSystem
{
    [Dependency] private KsFluidmosPhaseSystem _ksFluidmosPhaseSystem = default!;

    /// <summary>
    ///     Lets the packet passing the radiator condense or boil, with its walls carrying the latent heat to or from its
    ///         surroundings. Without this, vapor a condenser cools only condenses in the pipe after it, whose latent heat
    ///         nothing removes, and a loop whose valve passes only liquid stalls with none.
    /// </summary>
    /// <param name="environment">The radiator's surroundings, or null with none to convect with.</param>
    /// <param name="radiationTemperature">What the radiator radiates to.</param>
    /// <param name="radiationScale">The radiation constant per effective tile, before the radiator's coefficient.</param>
    private void KsExchangePhases(HeatExchangerComponent component,
        GasMixture packet,
        float linePressure,
        GasMixture? environment,
        float radiationTemperature,
        float radiationScale,
        float deltaTime)
    {
        // The radiator's coefficients are on atmos's scaled heat; the phase change is in unscaled joules.
        var scale = _atmosphereSystem.HeatScale * component.KsLatentHeatTransferScale;
        var wall = new KsWallHeatTransfer(
            Conductance: environment != null ? component.K * scale : 0f,
            ConvectionTemperature: environment?.Temperature ?? radiationTemperature,
            RadiativeCoefficient: component.alpha * radiationScale * scale,
            RadiationTemperature: radiationTemperature);

        var heat = _ksFluidmosPhaseSystem.ExchangeAtWall(packet, linePressure, wall, deltaTime);
        if (heat == 0f || environment == null || environment.Immutable)
            return;

        var environmentHeatCapacity = _atmosphereSystem.GetHeatCapacity(environment, applyScaling: false);
        if (environmentHeatCapacity > 0f)
            environment.Temperature = MathF.Max(environment.Temperature + heat / environmentHeatCapacity, Atmospherics.TCMB);
    }
}
