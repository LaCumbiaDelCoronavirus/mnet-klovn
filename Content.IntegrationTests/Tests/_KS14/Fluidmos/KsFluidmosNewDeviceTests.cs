#nullable enable
using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Fluidmos;
using Content.Server._KS14.Fluidmos.Devices;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Shared._KS14.Fluidmos;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Fluidmos;

/// <summary>
///     The Fluidmos devices: the fluid port moves reagents between its tank and its pipe with their data, the phase
///         separator splits a pipe by phase, the expansion valve reads out its flash and steers itself towards its
///         superheat setpoint, and the UI entries sum what a mixture carries.
/// </summary>
[TestOf(typeof(KsFluidPortSystem))]
public sealed class KsFluidmosNewDeviceTests : GameTest
{
    private static readonly ProtoId<ReagentPrototype> WaterReagent = "Water";
    private static readonly ProtoId<ReagentPrototype> EthanolReagent = "Ethanol";
    private static readonly ProtoId<ReagentPrototype> BloodReagent = "Blood";

    private static readonly EntProtoId FluidPortPrototype = "KsFluidPort";
    private static readonly EntProtoId PhaseSeparatorPrototype = "KsPhaseSeparator";
    private static readonly EntProtoId ExpansionValvePrototype = "KsExpansionValve";

    private static readonly KsFluidReagentKey WaterKey = new(WaterReagent, null);
    private static readonly KsFluidReagentKey EthanolKey = new(EthanolReagent, null);

    private static ReagentId Blood(string dna)
    {
        return new ReagentId(BloodReagent, new List<ReagentData> { new DnaData { DNA = dna } });
    }

    private static void Update(IEntityManager entityManager, EntityUid deviceUid, float deltaTime)
    {
        var ev = new AtmosDeviceUpdateEvent(deltaTime, null, null);
        entityManager.EventBus.RaiseLocalEvent(deviceUid, ref ev);
    }

    private static void SetGas(GasMixture mixture, float pressure, float temperature)
    {
        mixture.Clear();
        mixture.Temperature = temperature;
        mixture.AdjustMoles(Gas.Nitrogen, pressure * mixture.Volume / (Atmospherics.R * temperature));
    }

    private async Task<EntityUid> SpawnDevice(EntProtoId prototype)
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        EntityUid deviceUid = default;
        await server.WaitPost(() => deviceUid = server.EntMan.SpawnEntity(prototype, testMap.GridCoords));
        await server.WaitRunTicks(5);
        return deviceUid;
    }

    [Test]
    public async Task FluidPortRoundTripsReagentData()
    {
        var server = Pair.Server;
        var portUid = await SpawnDevice(FluidPortPrototype);
        var nodeContainerSystem = server.System<NodeContainerSystem>();
        var solutionContainerSystem = server.System<SharedSolutionContainerSystem>();
        var fluidPortSystem = server.System<KsFluidPortSystem>();

        await server.WaitAssertion(() =>
        {
            Assert.That(nodeContainerSystem.TryGetNode(portUid, "pipe", out PipeNode? pipe));
            Assert.That(solutionContainerSystem.TryGetSolution(portUid, "fluidport", out var solutionEntity, out var solution));
            var port = server.EntMan.GetComponent<KsFluidPortComponent>(portUid);
            SetGas(pipe!.Air, Atmospherics.OneAtmosphere, Atmospherics.T20C);

            var units = port.Rate;
            solutionContainerSystem.TryAddReagent(solutionEntity!.Value, new ReagentQuantity(Blood("GATTACA"), FixedPoint2.New(units)), out _);

            // One second injects the port's rate.
            Update(server.EntMan, portUid, deltaTime: 1f);
            Assert.That(pipe.Air.Fluid?.TryGetAmount(KsFluidReagentKey.From(Blood("GATTACA")), out var injected) == true &&
                        injected.Liquid >= units - 0.01f,
                "The port didn't inject its tank, data and all, into the pipe.");
            Assert.That(solution!.Volume, Is.EqualTo(FixedPoint2.Zero), "The injected reagents stayed in the tank.");

            // Ice in the pipe is left behind; only liquid comes out.
            pipe.Air.Fluid!.SetAmount(WaterKey, new KsFluidAmount(5f, 0f, 0f));
            fluidPortSystem.SetMode((portUid, port), KsFluidPortMode.Extract);
            Update(server.EntMan, portUid, deltaTime: 1f);

            using (Assert.EnterMultipleScope())
            {
                Assert.That((float)solution.GetReagentQuantity(Blood("GATTACA")), Is.EqualTo(units).Within(0.02f),
                    "The extracted blood lost its DNA, or didn't come back.");
                Assert.That((float)solution.GetTotalPrototypeQuantity(WaterReagent), Is.Zero, "The port extracted ice.");
                Assert.That(pipe.Air.Fluid!.SolidUnits, Is.EqualTo(5f).Within(0.01f), "The pipe's ice left with the liquid.");
            }
        });
    }

    [Test]
    public async Task PhaseSeparatorSplitsByPhase()
    {
        var server = Pair.Server;
        var separatorUid = await SpawnDevice(PhaseSeparatorPrototype);
        var nodeContainerSystem = server.System<NodeContainerSystem>();
        var propertiesSystem = server.System<KsFluidmosPropertiesSystem>();

        await server.WaitAssertion(() =>
        {
            Assert.That(nodeContainerSystem.TryGetNodes(separatorUid, "inlet", "outlet", "filter",
                out PipeNode? inlet, out PipeNode? gasOutlet, out PipeNode? liquidOutlet));
            Assert.That(propertiesSystem.TryGetProperties(EthanolReagent, out var ethanol));
            var separator = server.EntMan.GetComponent<KsPhaseSeparatorComponent>(separatorUid);

            SetGas(inlet!.Air, 3f * Atmospherics.OneAtmosphere, Atmospherics.T20C);
            inlet.Air.Fluid = new KsFluidMixture();
            inlet.Air.Fluid.AddLiquid(WaterKey, 500f);
            inlet.Air.Fluid.SetAmount(EthanolKey, new KsFluidAmount(0f, 0f, 2f * ethanol!.MolesPerUnit));
            gasOutlet!.Air.Clear();
            liquidOutlet!.Air.Clear();

            const float deltaTime = 0.5f;
            Update(server.EntMan, separatorUid, deltaTime);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(gasOutlet.Air.TotalMoles, Is.GreaterThan(0f), "No gas left by the gas outlet.");
                Assert.That(gasOutlet.Air.Fluid?.VaporMoles ?? 0f, Is.GreaterThan(0f), "Vapor didn't leave with the gas.");
                Assert.That(gasOutlet.Air.Fluid?.LiquidUnits ?? 0f, Is.Zero, "Liquid left by the gas outlet.");
                Assert.That(liquidOutlet.Air.Fluid?.LiquidUnits ?? 0f, Is.EqualTo(separator.LiquidRate * deltaTime).Within(0.01f),
                    "The liquid outlet didn't get its rate of liquid.");
                Assert.That(liquidOutlet.Air.TotalMoles, Is.Zero, "Gas left by the liquid outlet.");
            }
        });
    }

    [Test]
    public async Task ExpansionValveReadsItsFlashAndSteersTowardsItsSetpoint()
    {
        var server = Pair.Server;
        var valveUid = await SpawnDevice(ExpansionValvePrototype);
        var nodeContainerSystem = server.System<NodeContainerSystem>();
        var propertiesSystem = server.System<KsFluidmosPropertiesSystem>();

        await server.WaitAssertion(() =>
        {
            Assert.That(nodeContainerSystem.TryGetNodes(valveUid, "inlet", "outlet", out PipeNode? inlet, out PipeNode? outlet));
            Assert.That(propertiesSystem.TryGetProperties(WaterReagent, out var water));
            var valve = server.EntMan.GetComponent<KsExpansionValveComponent>(valveUid);

            // Hot pressurised water into a low-pressure net: part of it flashes.
            const float inletTemperature = 360f;
            const float outletPressure = 10f;
            SetGas(inlet!.Air, 3f * Atmospherics.OneAtmosphere, inletTemperature);
            inlet.Air.Fluid = new KsFluidMixture();
            inlet.Air.Fluid.AddLiquid(WaterKey, 1000f);
            var outletBoilingPoint = KsPhaseMath.BoilingPointAt(water!.VaporizationEntropy, water.BoilingPoint, outletPressure);

            // A cold outlet is flooded (no superheat): thermostatic mode closes the valve.
            SetGas(outlet!.Air, outletPressure, outletBoilingPoint - 20f);
            var openingBefore = valve.Opening;
            SetThermostatic(server.EntMan, valveUid, valve);
            Update(server.EntMan, valveUid, deltaTime: 1f);
            var flooded = valve.Opening;

            var expectedFraction = Math.Clamp(water.SpecificHeat * (inletTemperature - outletBoilingPoint) / water.LatentHeat, 0f, 1f);
            var vaporFraction = valve.VaporFraction;

            // A hot outlet is starved: it opens.
            SetGas(outlet.Air, outletPressure, outletBoilingPoint + 40f);
            var outletMoles = outlet.Air.TotalMoles;
            Update(server.EntMan, valveUid, deltaTime: 1f);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(vaporFraction, Is.EqualTo(expectedFraction).Within(1e-3f), "The valve's flash readout isn't the energy balance.");
                Assert.That(vaporFraction, Is.GreaterThan(0f).And.LessThan(1f), "The test's temperatures don't make a partial flash.");
                Assert.That(flooded, Is.LessThan(openingBefore), "A flooded evaporator didn't close the thermostatic valve.");
                Assert.That(valve.Opening, Is.GreaterThan(flooded), "A starved evaporator didn't open the thermostatic valve.");
                Assert.That(outlet.Air.Fluid?.LiquidUnits ?? 0f, Is.GreaterThan(0f), "No liquid crossed the valve.");
                Assert.That(outlet.Air.TotalMoles, Is.EqualTo(outletMoles).Within(1e-4f),
                    "Gas crossed a liquid-only valve.");
            }
        });
    }

    [Test]
    public async Task ThermostaticValveHoldsItsSuperheat()
    {
        var server = Pair.Server;
        var valveUid = await SpawnDevice(ExpansionValvePrototype);
        var nodeContainerSystem = server.System<NodeContainerSystem>();
        var propertiesSystem = server.System<KsFluidmosPropertiesSystem>();
        var phaseSystem = server.System<KsFluidmosPhaseSystem>();
        var gameTiming = server.ResolveDependency<Robust.Shared.Timing.IGameTiming>();

        await server.WaitAssertion(() =>
        {
            Assert.That(nodeContainerSystem.TryGetNodes(valveUid, "inlet", "outlet", out PipeNode? inlet, out PipeNode? outlet));
            Assert.That(propertiesSystem.TryGetProperties(WaterReagent, out var water));
            var valve = server.EntMan.GetComponent<KsExpansionValveComponent>(valveUid);
            SetThermostatic(server.EntMan, valveUid, valve);
            server.EntMan.EventBus.RaiseLocalEvent(valveUid, new Content.Shared._KS14.Fluidmos.Devices.KsExpansionValveSetOpeningMessage(0f));

            // An evaporator: a heat load the valve is sized for, and a compressor's suction holding it at a low pressure.
            //      Started shut, as a flooded start has to boil its pool off first, which tells nothing about the control.
            const float lowPressure = 5f;
            const float heatLoad = 20000f;
            const float deltaTime = 0.5f;
            var boilingPoint = KsPhaseMath.BoilingPointAt(water!.VaporizationEntropy, water.BoilingPoint, lowPressure);
            outlet!.Air.Clear();
            outlet.Air.Temperature = boilingPoint + 20f;
            outlet.Air.AdjustMoles(Gas.WaterVapor, lowPressure * outlet.Air.Volume / (Atmospherics.R * outlet.Air.Temperature));

            var superheats = new List<float>();
            for (var step = 0; step < 1200; step++)
            {
                // The high side: warm liquid under pressure, as a condenser's receiver would hold it.
                SetGas(inlet!.Air, 3f * Atmospherics.OneAtmosphere, 310f);
                inlet.Air.Fluid = new KsFluidMixture();
                inlet.Air.Fluid.AddLiquid(WaterKey, 1000f);

                Update(server.EntMan, valveUid, deltaTime);

                var heatCapacity = server.System<Content.Server.Atmos.EntitySystems.AtmosphereSystem>().GetHeatCapacity(outlet.Air, applyScaling: false);
                if (heatCapacity > 0f)
                    outlet.Air.Temperature += heatLoad * deltaTime / heatCapacity;

                if (outlet.Air.Fluid != null)
                    outlet.Air.Fluid.LastPhaseTime = gameTiming.CurTime - TimeSpan.FromSeconds(deltaTime);
                phaseSystem.UpdatePhases(outlet.Air, holder: null);

                var targetMoles = lowPressure * outlet.Air.FreeVolume / (Atmospherics.R * outlet.Air.Temperature);
                var excess = outlet.Air.GetMoles(Gas.WaterVapor) - targetMoles;
                if (excess > 0f)
                    outlet.Air.AdjustMoles(Gas.WaterVapor, -excess);

                superheats.Add(outlet.Air.Temperature - KsPhaseMath.BoilingPointAt(water.VaporizationEntropy, water.BoilingPoint, outlet.Air.Pressure));
            }

            // Settled over the second half: on the setpoint on average, and never far off it.
            var settled = superheats.GetRange(superheats.Count / 2, superheats.Count - superheats.Count / 2);
            var mean = 0f;
            var worst = 0f;
            foreach (var superheat in settled)
            {
                mean += superheat / settled.Count;
                worst = MathF.Max(worst, MathF.Abs(superheat - valve.SuperheatSetpoint));
            }

            using (Assert.EnterMultipleScope())
            {
                Assert.That(mean, Is.EqualTo(valve.SuperheatSetpoint).Within(1f), "The thermostatic valve didn't hold its superheat on average.");
                Assert.That(worst, Is.LessThan(4f), "The thermostatic valve swung far off its setpoint.");
            }
        });
    }

    private static void SetThermostatic(IEntityManager entityManager, EntityUid valveUid, KsExpansionValveComponent valve)
    {
        // Through the window's own message, as a player would.
        entityManager.EventBus.RaiseLocalEvent(valveUid, new Content.Shared._KS14.Fluidmos.Devices.KsExpansionValveSetThermostaticMessage(true));
        Assert.That(valve.Thermostatic, "The thermostatic message was ignored.");
    }

    [Test]
    public async Task FluidEntriesSumDataVariantsAndShowVaporInUnits()
    {
        var server = Pair.Server;
        var propertiesSystem = server.System<KsFluidmosPropertiesSystem>();

        await server.WaitAssertion(() =>
        {
            Assert.That(propertiesSystem.TryGetProperties(EthanolReagent, out var ethanol));
            var fluid = new KsFluidMixture();
            var blood = new Solution();
            blood.AddReagent(Blood("AAAA"), FixedPoint2.New(3));
            blood.AddReagent(Blood("CCCC"), FixedPoint2.New(4));
            fluid.AddSolution(blood);
            fluid.SetAmount(EthanolKey, new KsFluidAmount(0f, 0f, 2f * ethanol!.MolesPerUnit));

            var entries = KsFluidEntry.FromMixture(fluid, propertiesSystem);
            Assert.That(entries, Is.Not.Null);
            var bloodEntry = Array.Find(entries!, entry => entry.Reagent == BloodReagent);
            var ethanolEntry = Array.Find(entries!, entry => entry.Reagent == EthanolReagent);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(entries!, Has.Length.EqualTo(2), "Data variants weren't summed into one entry per reagent.");
                Assert.That(bloodEntry.Liquid, Is.EqualTo(7f).Within(0.01f), "The blood variants didn't sum.");
                Assert.That(ethanolEntry.Vapor, Is.EqualTo(2f).Within(0.01f), "Vapor wasn't shown in units.");
                Assert.That(KsFluidEntry.FromMixture(new KsFluidMixture(), propertiesSystem), Is.Null, "An empty mixture listed entries.");
            }
        });
    }
}
