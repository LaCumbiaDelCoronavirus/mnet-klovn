using Content.Server.Atmos.Portable;
using Content.Shared.Atmos.Components;
using Content.Shared.Atmos.Piping.Unary.Components;

namespace Content.Server._KS14.Fluidmos;

/// <summary>
///     Marks canister, portable scrubber and gas tank air as able to hold fluid. The flag is not saved, so it is set again on every init.
/// </summary>
public sealed partial class KsFluidmosCapabilitySystem : EntitySystem
{
    [SubscribeLocalEvent]
    private void OnCanisterInit(Entity<GasCanisterComponent> entity, ref ComponentInit args)
    {
        entity.Comp.Air.FluidCapable = true;
    }

    [SubscribeLocalEvent]
    private void OnPortableScrubberInit(Entity<PortableScrubberComponent> entity, ref ComponentInit args)
    {
        entity.Comp.Air.FluidCapable = true;
    }

    [SubscribeLocalEvent]
    private void OnTankInit(Entity<GasTankComponent> entity, ref ComponentInit args)
    {
        entity.Comp.Air.FluidCapable = true;
    }
}
