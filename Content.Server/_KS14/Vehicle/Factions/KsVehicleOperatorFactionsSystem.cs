using Content.Shared._KS14.NPC.Factions;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Prototypes;
using Content.Shared.NPC.Systems;
using Content.Shared.Vehicle;
using Content.Shared.Vehicle.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Vehicle.Factions;

/// <summary>
///     Keeps a <see cref="KsVehicleOperatorFactionsComponent"/> vehicle's NPC factions those of whoever drives it, while
///         they are fit to: on getting in and out, on the driver's own factions changing in the seat, and on the driver
///         going critical, dying or recovering. Only the driver's <see cref="NpcFactionMemberComponent.Factions"/> are
///         copied: their extra friends and foes are how the driver sees others, and the vehicle sees nothing.
/// </summary>
public sealed partial class KsVehicleOperatorFactionsSystem : EntitySystem
{
    [Dependency] private MobStateSystem _mobStateSystem = default!;
    [Dependency] private NpcFactionSystem _npcFactionSystem = default!;

    [Dependency] private EntityQuery<KsVehicleOperatorFactionsComponent> _operatorFactionsQuery = default!;
    [Dependency] private EntityQuery<NpcFactionMemberComponent> _factionMemberQuery = default!;
    [Dependency] private EntityQuery<VehicleComponent> _vehicleQuery = default!;

    private static readonly HashSet<ProtoId<NpcFactionPrototype>> NoFactions = new();

    /// <summary>
    ///     A vehicle loaded with factions it should not have: saved while someone drove it, with the driver not saved too.
    ///         Never adds a component, so nothing changes on a fresh spawn.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnStartup(Entity<KsVehicleOperatorFactionsComponent> vehicle, ref ComponentStartup args)
    {
        Refresh(vehicle, ensureMember: false);
    }

    [SubscribeLocalEvent]
    private void OnShutdown(Entity<KsVehicleOperatorFactionsComponent> vehicle, ref ComponentShutdown args)
    {
        if (Terminating(vehicle) || !_factionMemberQuery.TryComp(vehicle, out var memberComponent))
            return;

        SetFactions((vehicle, memberComponent), vehicle.Comp.OwnFactions);
    }

    [SubscribeLocalEvent]
    private void OnOperatorSet(Entity<KsVehicleOperatorFactionsComponent> vehicle, ref VehicleOperatorSetEvent args)
    {
        if (TerminatingOrDeleted(vehicle))
            return;

        Refresh(vehicle, ensureMember: true);
    }

    /// <summary>
    ///     The driver's factions changed in the seat: converted, zombified, given a side. Raised from inside a walk over
    ///         every faction member on a prototype reload, so this adds no component: the vehicle has had its member
    ///         component since the driver got in.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnDriverFactionsRefreshed(Entity<VehicleOperatorComponent> driver, ref KsNpcFactionsRefreshedEvent args)
    {
        RefreshDriven(driver);
    }

    [SubscribeLocalEvent]
    private void OnDriverMobStateChanged(Entity<VehicleOperatorComponent> driver, ref MobStateChangedEvent args)
    {
        RefreshDriven(driver);
    }

    private void RefreshDriven(Entity<VehicleOperatorComponent> driver)
    {
        // One leaving this tick is shut down but still stored until the end of it.
        if (driver.Comp.LifeStage > ComponentLifeStage.Running ||
            driver.Comp.Vehicle is not { } vehicleUid ||
            !_operatorFactionsQuery.TryComp(vehicleUid, out var operatorFactionsComponent))
            return;

        Refresh((vehicleUid, operatorFactionsComponent), ensureMember: false);
    }

    /// <summary>
    ///     Sets the vehicle's factions to its driver's, if it has one fit to drive, or else to its own.
    /// </summary>
    private void Refresh(Entity<KsVehicleOperatorFactionsComponent> vehicle, bool ensureMember)
    {
        var factions = GetFactions(vehicle);

        NpcFactionMemberComponent? memberComponent;
        if (ensureMember)
            memberComponent = EnsureComp<NpcFactionMemberComponent>(vehicle);
        else if (!_factionMemberQuery.TryComp(vehicle, out memberComponent))
            return;

        SetFactions((vehicle, memberComponent), factions);
    }

    private HashSet<ProtoId<NpcFactionPrototype>> GetFactions(Entity<KsVehicleOperatorFactionsComponent> vehicle)
    {
        if (!_vehicleQuery.TryComp(vehicle, out var vehicleComponent) ||
            vehicleComponent.Operator is not { } operatorUid ||
            TerminatingOrDeleted(operatorUid))
            return vehicle.Comp.OwnFactions;

        // Down in the seat: nobody is driving it, as far as anyone outside can tell.
        if (_mobStateSystem.IsIncapacitated(operatorUid))
            return vehicle.Comp.OwnFactions;

        return _factionMemberQuery.TryComp(operatorUid, out var operatorMemberComponent)
            ? operatorMemberComponent.Factions
            : NoFactions;
    }

    private void SetFactions(Entity<NpcFactionMemberComponent> member, HashSet<ProtoId<NpcFactionPrototype>> factions)
    {
        if (HasExactly(member, factions))
            return;

        _npcFactionSystem.ClearFactions(member.AsNullable(), dirty: false);
        // A copy: AddFactions reads the set as it goes, and it may be the driver's own.
        _npcFactionSystem.AddFactions(member.AsNullable(), new HashSet<ProtoId<NpcFactionPrototype>>(factions));
    }

    /// <summary>
    ///     Whether <paramref name="member"/>'s factions are <paramref name="factions"/>, no more and no fewer.
    /// </summary>
    private bool HasExactly(Entity<NpcFactionMemberComponent> member, HashSet<ProtoId<NpcFactionPrototype>> factions)
    {
        if (member.Comp.Factions.Count != factions.Count)
            return false;

        foreach (var faction in factions)
        {
            if (!_npcFactionSystem.IsMember(member.AsNullable(), faction))
                return false;
        }

        return true;
    }
}
