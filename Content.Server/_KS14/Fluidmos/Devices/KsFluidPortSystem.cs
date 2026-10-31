using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Shared._KS14.PopupLocalization;
using Content.Shared.Atmos.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Popups;
using Content.Shared.Verbs;

namespace Content.Server._KS14.Fluidmos.Devices;

/// <summary>
///     Moves reagents between a <see cref="KsFluidPortComponent"/>'s tank and its pipe.
/// </summary>
public sealed partial class KsFluidPortSystem : EntitySystem
{
    [Dependency] private NodeContainerSystem _nodeContainerSystem = default!;
    [Dependency] private SharedSolutionContainerSystem _solutionContainerSystem = default!;
    [Dependency] private KsFluidmosTileSystem _fluidmosTileSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;

    [SubscribeLocalEvent]
    private void OnDeviceUpdate(Entity<KsFluidPortComponent> entity, ref AtmosDeviceUpdateEvent args)
    {
        if (!_nodeContainerSystem.TryGetNode(entity.Owner, entity.Comp.PipeName, out PipeNode? pipe) ||
            !pipe.Air.FluidCapable ||
            !_solutionContainerSystem.TryGetSolution(entity.Owner, entity.Comp.SolutionName, out var solutionEntity, out var solution))
            return;

        var units = entity.Comp.Rate * args.dt;
        if (units <= 0f)
            return;

        if (entity.Comp.Mode == KsFluidPortMode.Inject)
        {
            if (solution.Volume <= FixedPoint2.Zero || pipe.Air.Pressure >= entity.Comp.MaxPressure)
                return;

            var injected = _solutionContainerSystem.SplitSolution(solutionEntity.Value, FixedPoint2.New(units));
            _fluidmosTileSystem.InjectSolution(pipe.Air, injected);
            return;
        }

        if (pipe.Air.Fluid is not { } fluid)
            return;

        units = MathF.Min(MathF.Min(units, fluid.LiquidUnits), (float)solution.AvailableVolume);
        if (units < (float)FixedPoint2.Epsilon)
            return;

        // Liquid only: ice stays in the pipe, and vapor is gas.
        var extracted = fluid.TakeLiquidAsSolution(units);
        extracted.Temperature = pipe.Air.Temperature;
        _solutionContainerSystem.TryAddSolution(solutionEntity.Value, extracted);
    }

    [SubscribeLocalEvent]
    private void OnGetVerbs(Entity<KsFluidPortComponent> entity, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        var userUid = args.User;
        var nextMode = entity.Comp.Mode == KsFluidPortMode.Inject ? KsFluidPortMode.Extract : KsFluidPortMode.Inject;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString(ModeVerbKey(nextMode)),
            Act = () => SetMode(entity, nextMode, userUid),
        });
    }

    [SubscribeLocalEvent]
    private void OnExamined(Entity<KsFluidPortComponent> entity, ref ExaminedEvent args)
    {
        if (args.IsInDetailsRange)
            args.PushMarkup(Loc.GetString(ModeExamineKey(entity.Comp.Mode)));
    }

    public void SetMode(Entity<KsFluidPortComponent> entity, KsFluidPortMode mode, EntityUid? userUid = null)
    {
        entity.Comp.Mode = mode;
        if (userUid is { } user)
            _popupSystem.PopupEntity(KsPopupMessage.Create(ModeExamineKey(mode)), entity.Owner, user);
    }

    private static string ModeVerbKey(KsFluidPortMode mode)
    {
        return mode == KsFluidPortMode.Inject ? "ks-fluid-port-verb-inject" : "ks-fluid-port-verb-extract";
    }

    private static string ModeExamineKey(KsFluidPortMode mode)
    {
        return mode == KsFluidPortMode.Inject ? "ks-fluid-port-mode-inject" : "ks-fluid-port-mode-extract";
    }
}
