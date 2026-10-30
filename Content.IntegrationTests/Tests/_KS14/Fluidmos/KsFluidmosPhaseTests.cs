#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Fluidmos;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._KS14.Fluidmos;
using Content.Shared.Atmos;
using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._KS14.Fluidmos;

/// <summary>
///     The phase step inside fluid-capable mixtures: Hertz–Knudsen evaporation towards Raoult's-law equilibrium, flashing
///         above the boiling point at the mixture's pressure, condensation of linked gases, freezing plateaus, sublimation,
///         the critical-temperature cap, and energy conservation throughout.
/// </summary>
[TestOf(typeof(KsFluidmosPhaseSystem))]
public sealed class KsFluidmosPhaseTests : GameTest
{
    private static readonly ProtoId<ReagentPrototype> WaterReagent = "Water";
    private static readonly ProtoId<ReagentPrototype> NitrogenReagent = "Nitrogen";

    private static readonly KsFluidReagentKey WaterKey = new(WaterReagent, null);
    private static readonly KsFluidReagentKey NitrogenKey = new(NitrogenReagent, null);

    /// <summary>
    ///     A pipe node's volume, which the Hertz–Knudsen tuning targets.
    /// </summary>
    private const float NodeVolume = 200f;

    private const float StepSeconds = 0.5f;

    private static GasMixture MakePipe(float temperature)
    {
        return new GasMixture(NodeVolume) { Temperature = temperature, FluidCapable = true };
    }

    /// <summary>
    ///     Runs one phase step as if <paramref name="seconds"/> had passed since the last.
    /// </summary>
    private static void Step(KsFluidmosPhaseSystem phaseSystem, IGameTiming gameTiming, GasMixture mixture, float seconds = StepSeconds)
    {
        if (mixture.Fluid != null)
            mixture.Fluid.LastPhaseTime = gameTiming.CurTime - TimeSpan.FromSeconds(seconds);

        phaseSystem.UpdatePhases(mixture, holder: null);
    }

    private static float EquilibriumVaporMoles(KsFluidReagentProperties properties, GasMixture mixture)
    {
        var saturationPressure = KsPhaseMath.SaturationPressure(properties.TroutonConstant, properties.BoilingPoint, mixture.Temperature);
        return saturationPressure * mixture.FreeVolume / (Atmospherics.R * mixture.Temperature);
    }

    [Test]
    public async Task WaterEvaporatesToSaturationWithinTheTunedTimeAndCools()
    {
        var server = Pair.Server;
        var phaseSystem = server.System<KsFluidmosPhaseSystem>();
        var atmosphereSystem = server.System<AtmosphereSystem>();
        var propertiesSystem = server.System<KsFluidmosPropertiesSystem>();
        var gameTiming = server.ResolveDependency<IGameTiming>();

        await server.WaitAssertion(() =>
        {
            Assert.That(propertiesSystem.TryGetProperties(WaterReagent, out var water));
            const float startTemperature = 300f;
            var mixture = MakePipe(startTemperature);
            mixture.Fluid = new KsFluidMixture();
            mixture.Fluid.AddLiquid(WaterKey, 100f);

            var energyBefore = atmosphereSystem.GetHeatCapacity(mixture, applyScaling: false) * mixture.Temperature;

            // The tuning: most of the way to saturation in about five seconds.
            for (var elapsed = 0f; elapsed < 5f; elapsed += StepSeconds)
            {
                Step(phaseSystem, gameTiming, mixture);
            }

            var vapor = mixture.GetMoles(Gas.WaterVapor);
            var equilibrium = EquilibriumVaporMoles(water!, mixture);
            var evaporatedUnits = vapor / water!.MolesPerUnit;
            var energyAfter = atmosphereSystem.GetHeatCapacity(mixture, applyScaling: false) * mixture.Temperature
                              + evaporatedUnits * water.LatentHeat;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(vapor, Is.GreaterThanOrEqualTo(0.9f * equilibrium), "Water came nowhere near saturation in five seconds.");
                Assert.That(vapor, Is.LessThanOrEqualTo(1.01f * equilibrium), "Evaporation overshot equilibrium.");
                Assert.That(mixture.Temperature, Is.LessThan(startTemperature), "Evaporation did not cool the water.");
                Assert.That(energyAfter, Is.EqualTo(energyBefore).Within(energyBefore * 0.0005f), "Evaporation created or destroyed energy.");
            }
        });
    }

    [Test]
    public async Task WaterVaporCondensesInAColdDryPipe()
    {
        var server = Pair.Server;
        var phaseSystem = server.System<KsFluidmosPhaseSystem>();
        var propertiesSystem = server.System<KsFluidmosPropertiesSystem>();
        var gameTiming = server.ResolveDependency<IGameTiming>();

        await server.WaitAssertion(() =>
        {
            Assert.That(propertiesSystem.TryGetProperties(WaterReagent, out var water));
            const float startTemperature = 285f;
            var mixture = MakePipe(startTemperature);
            mixture.AdjustMoles(Gas.Nitrogen, 8f);
            mixture.AdjustMoles(Gas.WaterVapor, 2f);

            for (var i = 0; i < 40; i++)
            {
                Step(phaseSystem, gameTiming, mixture);
            }

            Assert.That(mixture.Fluid, Is.Not.Null, "Supersaturated steam in a dry pipe never condensed.");
            Assert.That(mixture.Fluid!.TryGetAmount(WaterKey, out var amount));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(amount.Liquid, Is.GreaterThan(0f));
                Assert.That(mixture.GetMoles(Gas.WaterVapor), Is.EqualTo(EquilibriumVaporMoles(water!, mixture)).Within(5f).Percent,
                    "The steam did not settle at saturation.");
                Assert.That(mixture.Temperature, Is.GreaterThan(startTemperature), "Condensing released no latent heat.");
            }
        });
    }

    [Test]
    public async Task PressureSuppressesFlashing()
    {
        var server = Pair.Server;
        var phaseSystem = server.System<KsFluidmosPhaseSystem>();
        var propertiesSystem = server.System<KsFluidmosPropertiesSystem>();
        var gameTiming = server.ResolveDependency<IGameTiming>();

        await server.WaitAssertion(() =>
        {
            Assert.That(propertiesSystem.TryGetProperties(WaterReagent, out var water));
            // Above water's boiling point at one atmosphere.
            var hotTemperature = water!.BoilingPoint + 20f;

            var open = MakePipe(hotTemperature);
            open.Fluid = new KsFluidMixture();
            open.Fluid.AddLiquid(WaterKey, 100f);

            var pressurised = MakePipe(hotTemperature);
            pressurised.Fluid = new KsFluidMixture();
            pressurised.Fluid.AddLiquid(WaterKey, 100f);
            pressurised.AdjustMoles(Gas.Nitrogen, 1000f * NodeVolume / (Atmospherics.R * hotTemperature));

            var flashPoint = KsPhaseMath.BoilingPointAt(water.TroutonConstant, water.BoilingPoint, pressurised.Pressure);
            Assert.That(flashPoint, Is.GreaterThan(hotTemperature), "The test's pressure doesn't raise the boiling point enough.");

            Step(phaseSystem, gameTiming, open);
            Step(phaseSystem, gameTiming, pressurised);

            Assert.That(pressurised.GetMoles(Gas.WaterVapor), Is.LessThan(open.GetMoles(Gas.WaterVapor) * 0.5f),
                "Pressurised water boiled as fast as water free to flash.");
        });
    }

    [Test]
    public async Task WaterFreezesAtAPlateauAndMeltsBack()
    {
        var server = Pair.Server;
        var phaseSystem = server.System<KsFluidmosPhaseSystem>();
        var atmosphereSystem = server.System<AtmosphereSystem>();
        var propertiesSystem = server.System<KsFluidmosPropertiesSystem>();
        var gameTiming = server.ResolveDependency<IGameTiming>();

        await server.WaitAssertion(() =>
        {
            Assert.That(propertiesSystem.TryGetProperties(WaterReagent, out var water));
            var coldTemperature = water!.MeltingPoint - 10f;
            var mixture = MakePipe(coldTemperature);
            mixture.Fluid = new KsFluidMixture();
            mixture.Fluid.AddLiquid(WaterKey, 100f);

            var energyBefore = atmosphereSystem.GetHeatCapacity(mixture, applyScaling: false) * mixture.Temperature;
            Step(phaseSystem, gameTiming, mixture);

            Assert.That(mixture.Fluid!.TryGetAmount(WaterKey, out var frozen));
            // A little evaporates in the same step, taking its own latent heat.
            var evaporatedUnits = mixture.GetMoles(Gas.WaterVapor) / water.MolesPerUnit;
            var energyAfter = atmosphereSystem.GetHeatCapacity(mixture, applyScaling: false) * mixture.Temperature
                              + evaporatedUnits * water.LatentHeat
                              - frozen.Solid * water.FusionHeat;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(mixture.Temperature, Is.EqualTo(water.MeltingPoint).Within(0.01f), "Freezing water did not plateau at its melting point.");
                Assert.That(frozen.Solid, Is.GreaterThan(0f), "Water below its melting point did not freeze.");
                Assert.That(frozen.Liquid, Is.GreaterThan(0f), "Water froze past what its sensible heat paid for.");
                Assert.That(energyAfter, Is.EqualTo(energyBefore).Within(energyBefore * 0.0005f), "The heat of fusion was not accounted for.");
            }

            // Warm it back up: the ice melts, taking the same heat.
            mixture.Temperature = water.MeltingPoint + 5f;
            Step(phaseSystem, gameTiming, mixture);
            Assert.That(mixture.Fluid!.TryGetAmount(WaterKey, out var melted));
            Assert.That(melted.Solid, Is.LessThan(frozen.Solid), "Warm ice did not melt.");
        });
    }

    [Test]
    public async Task IceInVacuumSublimes()
    {
        var server = Pair.Server;
        var phaseSystem = server.System<KsFluidmosPhaseSystem>();
        var propertiesSystem = server.System<KsFluidmosPropertiesSystem>();
        var gameTiming = server.ResolveDependency<IGameTiming>();

        await server.WaitAssertion(() =>
        {
            Assert.That(propertiesSystem.TryGetProperties(WaterReagent, out var water));
            var mixture = MakePipe(water!.MeltingPoint - 10f);
            mixture.Fluid = new KsFluidMixture();
            mixture.Fluid.SetAmount(WaterKey, new KsFluidAmount(10f, 0f, 0f));

            for (var i = 0; i < 10; i++)
            {
                Step(phaseSystem, gameTiming, mixture);
            }

            Assert.That(mixture.GetMoles(Gas.WaterVapor), Is.GreaterThan(0f), "Ice in vacuum did not sublime.");
            Assert.That(mixture.Fluid!.TryGetAmount(WaterKey, out var amount));
            Assert.That(amount.Liquid, Is.Zero, "Subliming ice melted instead.");
        });
    }

    [Test]
    public async Task LinkedGasesNeverCondenseAboveTheirCriticalTemperature()
    {
        var server = Pair.Server;
        var phaseSystem = server.System<KsFluidmosPhaseSystem>();
        var propertiesSystem = server.System<KsFluidmosPropertiesSystem>();
        var gameTiming = server.ResolveDependency<IGameTiming>();

        await server.WaitAssertion(() =>
        {
            Assert.That(propertiesSystem.TryGetProperties(NitrogenReagent, out var nitrogen));
            Assert.That(nitrogen!.CriticalTemperature, Is.LessThan(float.PositiveInfinity), "Nitrogen has no critical temperature.");

            // Far past what its vapor-pressure curve would hold, but above its critical temperature.
            var supercritical = MakePipe(nitrogen.CriticalTemperature + 5f);
            supercritical.AdjustMoles(Gas.Nitrogen, 50000f * NodeVolume / (Atmospherics.R * supercritical.Temperature));

            // The same just below its boiling point.
            var cold = MakePipe(nitrogen.BoilingPoint - 2f);
            cold.AdjustMoles(Gas.Nitrogen, 500f * NodeVolume / (Atmospherics.R * cold.Temperature));

            for (var i = 0; i < 10; i++)
            {
                Step(phaseSystem, gameTiming, supercritical);
                Step(phaseSystem, gameTiming, cold);
            }

            using (Assert.EnterMultipleScope())
            {
                Assert.That(supercritical.Fluid, Is.Null, "Nitrogen condensed above its critical temperature.");
                Assert.That(cold.Fluid?.TryGetAmount(NitrogenKey, out var liquid) == true && liquid.Liquid > 0f,
                    "Nitrogen below its boiling point never condensed.");
            }
        });
    }

    [Test]
    public async Task OverfilledNetsStopPumps()
    {
        var server = Pair.Server;
        var atmosphereSystem = server.System<AtmosphereSystem>();

        await server.WaitAssertion(() =>
        {
            var inlet = MakePipe(Atmospherics.T20C);
            inlet.Fluid = new KsFluidMixture();
            inlet.Fluid.AddLiquid(WaterKey, 100f);
            inlet.AdjustMoles(Gas.Nitrogen, 5f);

            var outlet = MakePipe(Atmospherics.T20C);
            outlet.Fluid = new KsFluidMixture();
            outlet.Fluid.AddLiquid(WaterKey, NodeVolume / KsFluidmos.LitersPerUnit);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(outlet.Pressure, Is.GreaterThan(Atmospherics.MaxOutputPressure), "A liquid-filled net has no hydraulic pressure.");
                Assert.That(atmosphereSystem.PumpGasTo(inlet, outlet, Atmospherics.MaxOutputPressure), Is.False,
                    "A pump kept feeding a liquid-filled net.");
            }
        });
    }
}
