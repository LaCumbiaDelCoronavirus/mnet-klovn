#nullable enable
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Vehicle.Factions;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Prototypes;
using Content.Shared.NPC.Systems;
using Content.Shared.Vehicle.Components;
using Content.Shared.Vehicle.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.Vehicle.KsNpcVehicleTestPrototypes;

namespace Content.IntegrationTests.Tests._KS14.Vehicle;

/// <summary>
///     A <c>KsVehicleOperatorFactions</c> vehicle is on its driver's side while they drive it and are fit to, and on its
///         own otherwise.
/// </summary>
[TestOf(typeof(KsVehicleOperatorFactionsSystem))]
public sealed class KsVehicleOperatorFactionsTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private static readonly ProtoId<NpcFactionPrototype> NanoTrasenFaction = "NanoTrasen";
    private static readonly ProtoId<NpcFactionPrototype> SyndicateFaction = "Syndicate";

    private async Task<(EntityUid Vehicle, EntityUid Driver)> SpawnAsync(string vehiclePrototype, string driverPrototype)
    {
        var map = await Pair.CreateTestMap();
        EntityUid vehicleUid = default;
        EntityUid driverUid = default;

        await Pair.Server.WaitPost(() =>
        {
            vehicleUid = SEntMan.SpawnEntity(vehiclePrototype, map.GridCoords);
            driverUid = SEntMan.SpawnEntity(driverPrototype, MapCoordinates.Nullspace);
        });

        await Pair.RunTicksSync(2);
        return (vehicleUid, driverUid);
    }

    private void Drive(EntityUid vehicleUid, EntityUid driverUid)
    {
        Assert.That(SEntMan.System<VehicleSystem>().TrySetOperator((vehicleUid, SEntMan.GetComponent<VehicleComponent>(vehicleUid)), driverUid),
            Is.True, "precondition: the driver gets in");
    }

    private void GetOut(EntityUid vehicleUid)
    {
        Assert.That(SEntMan.System<VehicleSystem>().TryRemoveOperator((vehicleUid, SEntMan.GetComponent<VehicleComponent>(vehicleUid))),
            Is.True, "precondition: the driver gets out");
    }

    private string[] Factions(EntityUid uid)
    {
        return SEntMan.TryGetComponent<NpcFactionMemberComponent>(uid, out var memberComponent)
            ? memberComponent.Factions.Select(faction => faction.Id).OrderBy(id => id).ToArray()
            : [];
    }

    /// <summary>
    ///     Driven, it is on the driver's side; left, on nobody's.
    /// </summary>
    [Test]
    public async Task TestTakesTheDriversFactionsWhileDriven()
    {
        var (vehicleUid, driverUid) = await SpawnAsync(FactionVehicle, DriverNanoTrasen);

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(Factions(vehicleUid), Is.Empty, "nobody's before anyone drives it");

            Drive(vehicleUid, driverUid);
            Assert.That(Factions(vehicleUid), Is.EqualTo(new[] { NanoTrasenFaction.Id }), "on the driver's side while they drive");

            GetOut(vehicleUid);
            Assert.That(Factions(vehicleUid), Is.Empty, "and nobody's once they are out");
        });
    }

    /// <summary>
    ///     A vehicle with a side of its own goes back to it.
    /// </summary>
    [Test]
    public async Task TestOwnFactionsWithNobodyDriving()
    {
        var (vehicleUid, driverUid) = await SpawnAsync(VehicleOwnSide, DriverSyndicate);

        await Pair.Server.WaitAssertion(() =>
        {
            Drive(vehicleUid, driverUid);
            Assert.That(Factions(vehicleUid), Is.EqualTo(new[] { SyndicateFaction.Id }), "the driver's side, not its own");

            GetOut(vehicleUid);
            Assert.That(Factions(vehicleUid), Is.EqualTo(new[] { NanoTrasenFaction.Id }), "its own once they are out");
        });
    }

    /// <summary>
    ///     The driver changing sides in the seat takes the vehicle with them.
    /// </summary>
    [Test]
    public async Task TestDriverChangingSidesInTheSeat()
    {
        var (vehicleUid, driverUid) = await SpawnAsync(FactionVehicle, DriverNanoTrasen);
        var npcFactionSystem = SEntMan.System<NpcFactionSystem>();

        await Pair.Server.WaitAssertion(() =>
        {
            Drive(vehicleUid, driverUid);

            npcFactionSystem.AddFaction(driverUid, SyndicateFaction);
            Assert.That(Factions(vehicleUid), Is.EqualTo(new[] { NanoTrasenFaction.Id, SyndicateFaction.Id }), "a faction joined in the seat");

            npcFactionSystem.RemoveFaction(driverUid, NanoTrasenFaction);
            Assert.That(Factions(vehicleUid), Is.EqualTo(new[] { SyndicateFaction.Id }), "and one left");
        });
    }

    /// <summary>
    ///     A driver down in the seat is nobody's driver: the vehicle drops their side, and takes it back if they get up.
    /// </summary>
    [Test]
    public async Task TestDriverDownDropsTheSide()
    {
        var (vehicleUid, driverUid) = await SpawnAsync(VehicleOwnSide, DriverSyndicate);
        var mobStateSystem = SEntMan.System<MobStateSystem>();

        await Pair.Server.WaitAssertion(() =>
        {
            Drive(vehicleUid, driverUid);

            mobStateSystem.ChangeMobState(driverUid, MobState.Critical);
            Assert.That(Factions(vehicleUid), Is.EqualTo(new[] { NanoTrasenFaction.Id }), "its own side while the driver is critical");

            mobStateSystem.ChangeMobState(driverUid, MobState.Alive);
            Assert.That(Factions(vehicleUid), Is.EqualTo(new[] { SyndicateFaction.Id }), "the driver's again once they recover");

            mobStateSystem.ChangeMobState(driverUid, MobState.Dead);
            Assert.That(Factions(vehicleUid), Is.EqualTo(new[] { NanoTrasenFaction.Id }), "its own once the driver is dead");
        });
    }

    /// <summary>
    ///     A driver deleted in the seat leaves it.
    /// </summary>
    [Test]
    public async Task TestDriverDeletedInTheSeat()
    {
        var (vehicleUid, driverUid) = await SpawnAsync(FactionVehicle, DriverNanoTrasen);

        await Pair.Server.WaitPost(() =>
        {
            Drive(vehicleUid, driverUid);
            SEntMan.DeleteEntity(driverUid);
        });
        await Pair.RunTicksSync(2);

        await Pair.Server.WaitAssertion(() =>
            Assert.That(Factions(vehicleUid), Is.Empty, "nobody's once the driver is gone"));
    }

    /// <summary>
    ///     The component taken off while driven leaves the vehicle on its own side, not the driver's for good.
    /// </summary>
    [Test]
    public async Task TestComponentRemovedWhileDriven()
    {
        var (vehicleUid, driverUid) = await SpawnAsync(VehicleOwnSide, DriverSyndicate);

        await Pair.Server.WaitPost(() =>
        {
            Drive(vehicleUid, driverUid);
            SEntMan.RemoveComponent<KsVehicleOperatorFactionsComponent>(vehicleUid);
        });
        await Pair.RunTicksSync(1);

        await Pair.Server.WaitAssertion(() =>
            Assert.That(Factions(vehicleUid), Is.EqualTo(new[] { NanoTrasenFaction.Id }), "its own side, the component gone"));
    }

    /// <summary>
    ///     Controls: a vehicle without the component never takes a side, and a driver of no side gives it none.
    /// </summary>
    [Test]
    public async Task TestControls()
    {
        var (plainUid, plainDriverUid) = await SpawnAsync(VehiclePlain, DriverNanoTrasen);
        var (vehicleUid, factionlessUid) = await SpawnAsync(FactionVehicle, DriverFactionless);

        await Pair.Server.WaitAssertion(() =>
        {
            Drive(plainUid, plainDriverUid);
            Assert.That(SEntMan.HasComponent<NpcFactionMemberComponent>(plainUid), Is.False, "a plain vehicle takes no side");

            Drive(vehicleUid, factionlessUid);
            Assert.That(Factions(vehicleUid), Is.Empty, "a driver of no side gives it none");
        });
    }
}
