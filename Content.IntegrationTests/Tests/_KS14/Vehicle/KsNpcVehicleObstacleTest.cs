#nullable enable
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server.NPC.Pathfinding;
using Content.Server.NPC.Systems;
using Content.Shared.Vehicle.Components;
using Content.Shared.Vehicle.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;
using static Content.IntegrationTests.Tests._KS14.Vehicle.KsNpcVehicleTestPrototypes;

namespace Content.IntegrationTests.Tests._KS14.Vehicle;

/// <summary>
///     Vehicles in NPCs' way: a loose vehicle blocks every tile of the navmesh it covers, not just the one its centre is on;
///         a vehicle is never priced as something to smash through; and steering avoids a driven vehicle, as it does a
///         parked one, rather than leave it to step aside like a mob.
/// </summary>
[TestOf(typeof(PathfindingSystem))]
public sealed class KsNpcVehicleObstacleTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    // Points of the navmesh, a quarter of a tile apart, in the tiles below, at and above the body's centre tile (0, 0).
    //   The body is 1.2 by 2.4, centred on (0.5, 0.5): it covers x -0.1 to 1.1, y -0.7 to 1.7.
    private static readonly Vector2 BelowPoint = new(0.625f, -0.375f);
    private static readonly Vector2 CentrePoint = new(0.625f, 0.625f);
    private static readonly Vector2 AbovePoint = new(0.625f, 1.375f);
    private static readonly Vector2 BesidePoint = new(1.625f, 0.625f);
    private static readonly Vector2 OtherBesidePoint = new(-0.375f, 0.625f);

    private async Task<(EntityUid Grid, EntityUid Body)> SpawnSettled(string prototype)
    {
        var map = await Pair.CreateTestMap();
        EntityUid gridUid = default;
        EntityUid bodyUid = default;

        await Pair.Server.WaitPost(() =>
        {
            gridUid = MakeGrid(SEntMan, Pair.Server.ResolveDependency<ITileDefinitionManager>(), map.MapId, map.Grid,
                new Vector2i(-4, -4), new Vector2i(4, 4)).Owner;
            bodyUid = SpawnAt(SEntMan, prototype, gridUid, 0, 0);
        });

        await WaitForNavmeshSettled(Pair, gridUid);
        return (gridUid, bodyUid);
    }

    private PathPoly Poly(EntityUid gridUid, Vector2 point)
    {
        var poly = SEntMan.System<PathfindingSystem>().GetPoly(new EntityCoordinates(gridUid, point));
        Assert.That(poly, Is.Not.Null, $"precondition: the navmesh has a poly at {point}");
        return poly!;
    }

    private bool IsBlocked(EntityUid gridUid, Vector2 point)
    {
        var data = Poly(gridUid, point).Data;
        return data.CollisionLayer != 0 || data.CollisionMask != 0;
    }

    [Test]
    public async Task TestParkedVehicleBlocksEveryTileItCovers()
    {
        var (gridUid, _) = await SpawnSettled(FactionVehicle);

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(IsBlocked(gridUid, CentrePoint), Is.True, "the tile its centre is on");
            Assert.That(IsBlocked(gridUid, BelowPoint), Is.True, "the tile its back end is on");
            Assert.That(IsBlocked(gridUid, AbovePoint), Is.True, "the tile its front end is on");
            Assert.That(IsBlocked(gridUid, BesidePoint), Is.False, "but not the tile beside it");
            Assert.That(IsBlocked(gridUid, OtherBesidePoint), Is.False, "on either side");
        });
    }

    /// <summary>
    ///     Controls: anything not a loose vehicle, anchored or loose, keeps to its own tile, as it always has. A tank, its
    ///         turret welded on, would otherwise wall itself in.
    /// </summary>
    [TestCase(AnchoredBlock)]
    [TestCase(Crate)]
    public async Task TestNonVehicleKeepsToItsOwnTile(string prototype)
    {
        var (gridUid, _) = await SpawnSettled(prototype);

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(IsBlocked(gridUid, CentrePoint), Is.True, "precondition: its own tile is blocked");
            Assert.That(IsBlocked(gridUid, AbovePoint), Is.False, "and only its own tile");
        });
    }

    /// <summary>
    ///     Nothing under a vehicle is priced for smashing through, wreckable as it is, so smashing NPCs go round; the same
    ///         body without the vehicle is.
    /// </summary>
    [TestCase(FactionVehicle, false)]
    [TestCase(Crate, true)]
    public async Task TestOnlyNonVehiclesArePricedForSmashing(string prototype, bool priced)
    {
        var (gridUid, _) = await SpawnSettled(prototype);

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(IsBlocked(gridUid, CentrePoint), Is.True, "precondition: in the way");
            Assert.That(Poly(gridUid, CentrePoint).Data.Damage > 0f, Is.EqualTo(priced),
                priced ? "a wreckable crate is priced for smashing through" : "a vehicle never is");
        });
    }

    /// <summary>
    ///     A driven vehicle is a kinematic controller, as mobs are, which steering leaves out of collision avoidance for
    ///         stepping aside on their own. A vehicle does not: it stays an obstacle.
    /// </summary>
    [Test]
    public async Task TestDrivenVehicleIsAvoided()
    {
        var (gridUid, vehicleUid) = await SpawnSettled(FactionVehicle);
        EntityUid mobUid = default;

        await Pair.Server.WaitPost(() =>
        {
            var driverUid = SEntMan.SpawnEntity(DriverNanoTrasen, MapCoordinates.Nullspace);
            Assert.That(SEntMan.System<VehicleSystem>().TrySetOperator((vehicleUid, SEntMan.GetComponent<VehicleComponent>(vehicleUid)), driverUid),
                Is.True, "precondition: driven");
            mobUid = SpawnAt(SEntMan, LonerMob, gridUid, 3, 3);
        });
        await Pair.RunTicksSync(1);

        await Pair.Server.WaitAssertion(() =>
        {
            var steeringSystem = SEntMan.System<NPCSteeringSystem>();
            var vehicleBody = SEntMan.GetComponent<PhysicsComponent>(vehicleUid);
            var mobBody = SEntMan.GetComponent<PhysicsComponent>(mobUid);

            Assert.That(vehicleBody.BodyType, Is.EqualTo(BodyType.KinematicController), "precondition: driven, a kinematic controller");
            Assert.That(mobBody.BodyType, Is.EqualTo(BodyType.KinematicController), "precondition: so is the mob");
            Assert.That(steeringSystem.KsIsIgnoredController(vehicleUid, vehicleBody), Is.False, "a driven vehicle is avoided");
            Assert.That(steeringSystem.KsIsIgnoredController(mobUid, mobBody), Is.True, "control: a mob is left to step aside");
        });
    }
}
