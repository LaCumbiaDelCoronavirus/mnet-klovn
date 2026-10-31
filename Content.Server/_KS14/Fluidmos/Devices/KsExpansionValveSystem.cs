using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Shared._KS14.Fluidmos;
using Content.Shared._KS14.Fluidmos.Devices;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Fluidmos.Devices;

/// <summary>
///     Runs <see cref="KsExpansionValveComponent"/>s and their windows.
/// </summary>
public sealed partial class KsExpansionValveSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private NodeContainerSystem _nodeContainerSystem = default!;
    [Dependency] private KsLiquidFlowSystem _liquidFlowSystem = default!;
    [Dependency] private KsFluidmosPropertiesSystem _propertiesSystem = default!;
    [Dependency] private UserInterfaceSystem _userInterfaceSystem = default!;

    [SubscribeLocalEvent]
    private void OnDeviceUpdate(Entity<KsExpansionValveComponent> entity, ref AtmosDeviceUpdateEvent args)
    {
        if (!_nodeContainerSystem.TryGetNodes(entity.Owner, entity.Comp.InletName, entity.Comp.OutletName, out PipeNode? inlet, out PipeNode? outlet))
            return;

        var refrigerant = MainLiquid(inlet.Air) ?? MainLiquid(outlet.Air);
        UpdateReadout(entity, inlet.Air, outlet.Air, refrigerant);

        if (entity.Comp.Thermostatic && float.IsFinite(entity.Comp.Superheat))
        {
            // Too much superheat means the evaporator is starved: open up. Too little and it floods: close down.
            var error = entity.Comp.Superheat - entity.Comp.SuperheatSetpoint;
            entity.Comp.Opening = Math.Clamp(entity.Comp.Opening + entity.Comp.ThermostaticGain * error * args.dt, 0f, 1f);
        }

        var moved = _liquidFlowSystem.Transfer(inlet.Air, outlet.Air, entity.Comp.Conductance * entity.Comp.Opening, args.dt);
        entity.Comp.FlowRate = args.dt > 0f ? moved / args.dt : 0f;

        if (_gameTiming.CurTime >= entity.Comp.NextUiUpdate)
        {
            entity.Comp.NextUiUpdate = _gameTiming.CurTime + entity.Comp.UiUpdateInterval;
            UpdateUi(entity);
        }
    }

    /// <summary>
    ///     The superheat at the outlet, and the share of the liquid that flashes crossing the valve: the energy-limited
    ///         flash the outlet's phase step will do, <c>c (T - Tb(P_out)) / L</c>, computed here only for display.
    /// </summary>
    private static void UpdateReadout(Entity<KsExpansionValveComponent> entity, GasMixture inlet, GasMixture outlet, KsFluidReagentProperties? refrigerant)
    {
        if (refrigerant is not { Volatile: true } || refrigerant.LatentHeat <= 0f)
        {
            entity.Comp.Superheat = float.NaN;
            entity.Comp.VaporFraction = float.NaN;
            return;
        }

        var outletBoilingPoint = KsPhaseMath.BoilingPointAt(refrigerant.VaporizationEntropy, refrigerant.BoilingPoint, outlet.Pressure);
        entity.Comp.Superheat = outlet.Temperature - outletBoilingPoint;
        entity.Comp.VaporFraction = Math.Clamp(refrigerant.SpecificHeat * (inlet.Temperature - outletBoilingPoint) / refrigerant.LatentHeat, 0f, 1f);
    }

    /// <summary>
    ///     The reagent a mixture holds the most liquid of.
    /// </summary>
    private KsFluidReagentProperties? MainLiquid(GasMixture mixture)
    {
        if (mixture.Fluid is not { } fluid)
            return null;

        KsFluidReagentProperties? main = null;
        var mainUnits = 0f;
        foreach (var (key, amount) in fluid.Reagents)
        {
            if (amount.Liquid <= mainUnits || !_propertiesSystem.TryGetProperties(key.Prototype, out var properties))
                continue;

            main = properties;
            mainUnits = amount.Liquid;
        }

        return main;
    }

    // By-value BUI events: the engine has no Entity<T> handler for them (RA0054).
    [SubscribeLocalEvent]
    private void OnUiOpened(EntityUid uid, KsExpansionValveComponent component, BoundUIOpenedEvent args)
    {
        UpdateUi((uid, component));
    }

    [SubscribeLocalEvent]
    private void OnSetOpening(EntityUid uid, KsExpansionValveComponent component, KsExpansionValveSetOpeningMessage args)
    {
        if (!float.IsFinite(args.Opening))
            return;

        component.Opening = Math.Clamp(args.Opening, 0f, 1f);
        UpdateUi((uid, component));
    }

    [SubscribeLocalEvent]
    private void OnSetThermostatic(EntityUid uid, KsExpansionValveComponent component, KsExpansionValveSetThermostaticMessage args)
    {
        component.Thermostatic = args.Thermostatic;
        UpdateUi((uid, component));
    }

    [SubscribeLocalEvent]
    private void OnSetSuperheat(EntityUid uid, KsExpansionValveComponent component, KsExpansionValveSetSuperheatMessage args)
    {
        if (!float.IsFinite(args.Superheat))
            return;

        component.SuperheatSetpoint = Math.Clamp(args.Superheat, 0f, component.MaxSuperheatSetpoint);
        UpdateUi((uid, component));
    }

    private void UpdateUi(Entity<KsExpansionValveComponent> entity)
    {
        if (!_userInterfaceSystem.IsUiOpen(entity.Owner, KsExpansionValveUiKey.Key))
            return;

        _userInterfaceSystem.SetUiState(entity.Owner,
            KsExpansionValveUiKey.Key,
            new KsExpansionValveBoundUserInterfaceState(entity.Comp.Opening,
                entity.Comp.Thermostatic,
                entity.Comp.SuperheatSetpoint,
                entity.Comp.VaporFraction,
                entity.Comp.Superheat,
                entity.Comp.FlowRate));
    }
}
