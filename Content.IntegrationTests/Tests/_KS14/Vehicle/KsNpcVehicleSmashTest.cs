#nullable enable
using System;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using System.Collections.Generic;
using System.Reflection;
using Content.Server.NPC.Components;
using Content.Server.NPC.Pathfinding;
using Content.Server.NPC.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Systems;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;
using static Content.IntegrationTests.Tests._KS14.Vehicle.KsNpcVehicleTestPrototypes;

namespace Content.IntegrationTests.Tests._KS14.Vehicle;

/// <summary>
///     A smashing NPC breaks through what is in its way that is destructible, and goes round a vehicle: a hull is not
///         something to wear down on the way through.
/// </summary>
[TestOf(typeof(NPCSteeringSystem))]
public sealed class KsNpcVehicleSmashTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private const string Smasher = "KsNpcVehicleSmashTestSmasher";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: KsSquadTestMobLoner
  id: KsNpcVehicleSmashTestSmasher
  components:
  - type: InputMover
  - type: MobMover
  - type: MovementSpeedModifier
  - type: CombatMode
  - type: MeleeWeapon
    attackRate: 4
    damage:
      types:
        Blunt: 5
";

    /// <summary>
    ///     Steering leaves what a smasher would smash out of the obstacles it steers round, and a smasher walks into them;
    ///         a vehicle, the same body otherwise, stays one. Asked of the filter itself, which is private: what is left
    ///         of the obstacles near the NPC, with the path ahead of it through them.
    /// </summary>
    [TestCase(Crate, false)]
    [TestCase(FactionVehicle, true)]
    public async Task TestSmasherSteersRoundOnlyVehicles(string obstaclePrototype, bool stillObstacle)
    {
        var map = await Pair.CreateTestMap();
        EntityUid obstacleUid = default;
        EntityUid npcUid = default;
        EntityUid gridUid = default;

        await Pair.Server.WaitPost(() =>
        {
            gridUid = MakeGrid(SEntMan, Pair.Server.ResolveDependency<ITileDefinitionManager>(), map.MapId, map.Grid,
                new Vector2i(-6, -6), new Vector2i(10, 6)).Owner;
            npcUid = SpawnAt(SEntMan, Smasher, gridUid, 1, 0);
            obstacleUid = SpawnAt(SEntMan, obstaclePrototype, gridUid, 2, 0);
        });
        await WaitForNavmeshSettled(Pair, gridUid);

        await Pair.Server.WaitAssertion(() =>
        {
            var steering = SEntMan.System<NPCSteeringSystem>().Register(npcUid, new EntityCoordinates(gridUid, new Vector2(8.5f, 0.5f)));
            steering.Flags = PathFlags.Smashing;
            var poly = SEntMan.System<PathfindingSystem>().GetPoly(new EntityCoordinates(gridUid, new Vector2(8.5f, 0.5f)));
            Assert.That(poly, Is.Not.Null, "precondition: the navmesh has a poly at the goal");
            steering.CurrentPath.Clear();
            steering.CurrentPath.Enqueue(poly!);

            var filter = typeof(NPCSteeringSystem).GetMethod("FilterObstacleEntities", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(filter, Is.Not.Null, "precondition: the filter is where this test looks for it");

            var obstacles = new HashSet<EntityUid> { obstacleUid };
            // What the NPC collides with, and what collides with it: the layers and masks of the bodies themselves.
            var (layer, mask) = SEntMan.System<SharedPhysicsSystem>().GetHardCollision(npcUid);
            filter!.Invoke(SEntMan.System<NPCSteeringSystem>(), [new Entity<NPCSteeringComponent>(npcUid, steering), mask, layer, obstacles, false]);

            Assert.That(obstacles.Contains(obstacleUid), Is.EqualTo(stillObstacle),
                stillObstacle ? "a vehicle is something to steer round" : "a smasher walks into what it would smash");
        });
    }
}
