using Content.Server._KS14.Atmos;
using Content.Server.Atmos.EntitySystems;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Shared._KS14.Fluidmos;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;

namespace Content.Server._KS14.Fluidmos.Devices;

/// <summary>
///     Runs <see cref="KsPhaseSeparatorComponent"/>s.
/// </summary>
public sealed partial class KsPhaseSeparatorSystem : EntitySystem
{
    [Dependency] private AtmosphereSystem _atmosphereSystem = default!;
    [Dependency] private NodeContainerSystem _nodeContainerSystem = default!;
    [Dependency] private KsTransferThermoSystem _transferThermoSystem = default!;

    [SubscribeLocalEvent]
    private void OnDeviceUpdate(Entity<KsPhaseSeparatorComponent> entity, ref AtmosDeviceUpdateEvent args)
    {
        if (!_nodeContainerSystem.TryGetNodes(entity.Owner,
                entity.Comp.InletName,
                entity.Comp.GasOutletName,
                entity.Comp.LiquidOutletName,
                out PipeNode? inlet,
                out PipeNode? gasOutlet,
                out PipeNode? liquidOutlet))
            return;

        SeparateGas(entity, inlet.Air, gasOutlet.Air);
        SeparateLiquid(entity, inlet.Air, liquidOutlet.Air, args.dt);
    }

    /// <summary>
    ///     Pumps gas and vapor, leaving the liquid behind, with a pressure pump's logic.
    /// </summary>
    private void SeparateGas(Entity<KsPhaseSeparatorComponent> entity, GasMixture inlet, GasMixture outlet)
    {
        var outletPressure = outlet.Pressure;
        var pressureDelta = entity.Comp.TargetPressure - outletPressure;
        var gasMoles = inlet.TotalMoles + (inlet.Fluid?.VaporMoles ?? 0f);
        if (pressureDelta < 0.01f || gasMoles <= 0f || inlet.Temperature <= 0f)
            return;

        var transferMoles = pressureDelta * outlet.Volume / (inlet.Temperature * Atmospherics.R);
        var inletPressure = inlet.Pressure;
        var removed = inlet.RemoveRatio(MathF.Min(transferMoles / gasMoles, 1f), includeLiquid: false);
        _transferThermoSystem.Apply(removed, inletPressure, outletPressure, doesWork: true, deviceUid: entity.Owner);
        _atmosphereSystem.Merge(outlet, removed);
    }

    private void SeparateLiquid(Entity<KsPhaseSeparatorComponent> entity, GasMixture inlet, GasMixture outlet, float deltaTime)
    {
        if (inlet.Fluid is not { } fluid ||
            fluid.LiquidUnits <= KsFluidmos.MinLiquidUnits ||
            !outlet.FluidCapable ||
            outlet.Pressure >= entity.Comp.MaxLiquidOutletPressure)
            return;

        var units = MathF.Min(entity.Comp.LiquidRate * deltaTime, fluid.LiquidUnits);
        if (units <= 0f)
            return;

        _atmosphereSystem.Merge(outlet, inlet.RemoveLiquidRatio(units / fluid.LiquidUnits));
    }
}
