#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.CrewVehicle.Entry;
using Content.Server._KS14.CrewVehicle.Interior;
using Content.Shared._KS14.CrewVehicle.Floor;
using Content.Shared._KS14.CrewVehicle.Interior;
using Content.Shared.Damage.Systems;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.CrewVehicle;

/// <summary>
///     Where people and things leaving a crew vehicle are put down: on floor when the vehicle stands on it, never
///         lost, at the vehicle's own distances, at rest, and a shot flying inside is spent rather than carried out.
/// </summary>
[TestFixture]
[TestOf(typeof(KsCrewVehicleEjectionSystem))]
[TestOf(typeof(KsCrewVehicleExitSystem))]
public sealed class KsCrewVehicleLandingTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false, Dirty = true };

    private readonly EntProtoId _hull = "KsCrewVehicleLandingTestHull";
    private readonly EntProtoId _wideHull = "KsCrewVehicleLandingTestHullWide";
    private readonly EntProtoId _mob = "KsCrewVehicleLandingTestMob";
    private readonly EntProtoId _crate = "KsCrewVehicleLandingTestCrate";
    private readonly EntProtoId _bullet = "KsCrewVehicleLandingTestBullet";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsCrewVehicleLandingTestHull
  components:
  - type: KsCrewVehicleInterior
    interiorPath: /Maps/Test/_KS14/CrewVehicle/basic.yml
    exitDelay: 0.5
    maxOccupants: 8
    ejectDamage:
      types:
        Blunt: 5
    doorways:
      left:
        offset: 1,0
      right:
        offset: -1,0

- type: entity
  parent: KsCrewVehicleLandingTestHull
  id: KsCrewVehicleLandingTestHullWide
  components:
  - type: KsCrewVehicleInterior
    interiorPath: /Maps/Test/_KS14/CrewVehicle/basic.yml
    ejectRadii: [ 6 ]
    doorways:
      left:
        offset: 1,0
      right:
        offset: -1,0

- type: entity
  id: KsCrewVehicleLandingTestMob
  components:
  - type: DoAfter
  - type: Physics
    bodyType: KinematicController
  - type: MobState
  - type: Damageable
  - type: Injurable
    damageContainer: Biological
  - type: KsCrewVehicleLandingTestDamageRecorder

- type: entity
  id: KsCrewVehicleLandingTestCrate
  components:
  - type: Physics
    bodyType: Dynamic
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeCircle
          radius: 0.2
        density: 10
        hard: false

- type: entity
  parent: KsCrewVehicleLandingTestCrate
  id: KsCrewVehicleLandingTestBullet
  components:
  - type: Projectile
    damage:
      types:
        Blunt: 1
";

    private MapId _mapId;
    private EntityUid _gridUid;

    /// <summary>
    ///     A map whose one grid has floor only on <paramref name="floorTiles"/>, and a hull at <paramref name="hullAt"/>.
    /// </summary>
    private async Task<EntityUid> SpawnHullAsync(EntProtoId hull, Vector2 hullAt, IEnumerable<Vector2i> floorTiles)
    {
        var testMap = await Pair.CreateTestMap();
        _mapId = testMap.MapId;
        _gridUid = testMap.Grid.Owner;
        EntityUid hullUid = default;
        await Server.WaitPost(() =>
        {
            // The test map's own tile, at (0, 0), stays only if asked for (and when nothing is: a grid left with no
            //      tiles at all is deleted).
            var mapSystem = SEntMan.System<SharedMapSystem>();
            var tiles = floorTiles.ToList();
            foreach (var tile in tiles)
            {
                mapSystem.SetTile(testMap.Grid, tile, testMap.Tile.Tile);
            }

            if (tiles.Count > 0 && !tiles.Contains(Vector2i.Zero))
                mapSystem.SetTile(testMap.Grid, Vector2i.Zero, Tile.Empty);

            hullUid = SEntMan.Spawn(hull, new MapCoordinates(hullAt, _mapId));
            Assert.That(SEntMan.System<KsCrewVehicleInteriorSystem>().EnsureInterior(SEntity<KsCrewVehicleInteriorComponent>(hullUid), out _), Is.True);
        });
        return hullUid;
    }

    private MapId InteriorMapId(EntityUid hull) => SComp<MapComponent>(SComp<KsCrewVehicleInteriorComponent>(hull).InteriorMap!.Value).MapId;

    private EntityUid ExitDoor(EntityUid hull, string doorway)
    {
        var interiorMap = SComp<KsCrewVehicleInteriorComponent>(hull).InteriorMap!.Value;
        Assert.That(SEntMan.System<KsCrewVehicleInteriorSystem>().TryGetExitDoor(interiorMap, doorway, out var door), Is.True);
        return door!.Value;
    }

    private bool IsFloor(EntityUid uid)
        => SEntMan.System<KsCrewVehicleFloorTileSystem>().IsFloor(SEntMan.System<SharedTransformSystem>().GetMapCoordinates(uid));

    private static IEnumerable<Vector2i> Rectangle(int minX, int maxX, int minY, int maxY)
    {
        for (var x = minX; x <= maxX; x++)
        {
            for (var y = minY; y <= maxY; y++)
            {
                yield return new Vector2i(x, y);
            }
        }
    }

    /// <param name="doorway">Which door: "left" lands over space, "right" on floor.</param>
    [TestCase("left", false)]
    [TestCase("right", true)]
    public async Task AtTheFloorsEdgeYouGetOutOnlyOntoFloor(string doorway, bool allowed)
    {
        // Floor to the hull's west only: its left doorway, local +X, is a tile out over space.
        var hull = await SpawnHullAsync(_hull, new Vector2(0.5f, 0.5f), Rectangle(-3, 0, -2, 2));
        await Server.WaitAssertion(() =>
        {
            // Beside the door: the left one is at (0.5, 3.5), the right at (4.5, 3.5).
            var besideTheDoor = doorway == "left" ? new Vector2(1.5f, 3.5f) : new Vector2(3.5f, 3.5f);
            var mob = SEntMan.Spawn(_mob, new MapCoordinates(besideTheDoor, InteriorMapId(hull)));
            var exit = SEntity<KsCrewVehicleExitComponent>(ExitDoor(hull, doorway));
            Assert.That(SEntMan.System<KsCrewVehicleExitSystem>().TryStartExit(exit, mob), Is.EqualTo(allowed),
                allowed ? "the control: out onto the floor" : "refused: there is no floor to step out onto");
        });
    }

    /// <param name="freeFloor">Floor runs out either side of the hull, rather than only under it.</param>
    [TestCase(true)]
    [TestCase(false)]
    public async Task ThrownClearYouLandOnFloorAndAreNeverLost(bool freeFloor)
    {
        var hullAt = new Vector2(0.5f, 0.5f);
        var hull = await SpawnHullAsync(_hull, hullAt, freeFloor ? Rectangle(-4, 4, 0, 0) : Rectangle(0, 0, 0, 0));
        var mobs = new List<EntityUid>();
        await Server.WaitPost(() =>
        {
            for (var i = 0; i < 4; i++)
            {
                mobs.Add(SEntMan.Spawn(_mob, new MapCoordinates(new Vector2(1.5f + i * 0.5f, 2.5f), InteriorMapId(hull))));
            }
        });
        await Pair.RunTicksSync(2);
        await Server.WaitPost(() => SEntMan.DeleteEntity(hull));
        await Pair.RunTicksSync(3);

        await Server.WaitAssertion(() =>
        {
            var transformSystem = SEntMan.System<SharedTransformSystem>();
            foreach (var mob in mobs)
            {
                var position = transformSystem.GetMapCoordinates(mob);
                Assert.That(position.MapId, Is.EqualTo(_mapId), "thrown out");
                Assert.That(IsFloor(mob), Is.True, $"onto floor, at {position.Position}");
                if (!freeFloor)
                    Assert.That(Vector2.Distance(position.Position, hullAt), Is.LessThan(0.01f), "with no floor free around it, where the vehicle was");
            }
        });
    }

    [Test]
    public async Task ThrownClearIsHurtByNothingThatHasBeenDeleted()
    {
        var hull = await SpawnHullAsync(_hull, new Vector2(100.5f, 100.5f), []);
        EntityUid mob = default;
        await Server.WaitPost(() => mob = SEntMan.Spawn(_mob, new MapCoordinates(new Vector2(2.5f, 2.5f), InteriorMapId(hull))));
        await Pair.RunTicksSync(2);
        await Server.WaitPost(() => SEntMan.DeleteEntity(hull));
        await Pair.RunTicksSync(3);

        await Server.WaitAssertion(() =>
        {
            var recorder = SComp<KsCrewVehicleLandingTestDamageRecorderComponent>(mob);
            Assert.That(recorder.Origins, Is.Not.Empty, "precondition: thrown clear, it was hurt");
            Assert.That(recorder.Origins, Is.All.Null, "with no origin: the vehicle is gone");
        });
    }

    [Test]
    public async Task ThrownClearIsPutAtTheVehiclesOwnDistance()
    {
        var hullAt = new Vector2(100.5f, 100.5f);
        var hull = await SpawnHullAsync(_wideHull, hullAt, []);
        var radius = SComp<KsCrewVehicleInteriorComponent>(hull).EjectRadii.Single();
        EntityUid mob = default;
        await Server.WaitPost(() => mob = SEntMan.Spawn(_mob, new MapCoordinates(new Vector2(2.5f, 2.5f), InteriorMapId(hull))));
        await Pair.RunTicksSync(2);
        await Server.WaitPost(() => SEntMan.DeleteEntity(hull));
        await Pair.RunTicksSync(3);

        await Server.WaitAssertion(() =>
        {
            var position = SEntMan.System<SharedTransformSystem>().GetMapCoordinates(mob);
            Assert.That(Vector2.Distance(position.Position, hullAt), Is.EqualTo(radius).Within(0.01f), "on the vehicle's own ring");
        });
    }

    [Test]
    public async Task WhatFliesOffTheInteriorGridComesOutAtRestOrIsSpent()
    {
        var hull = await SpawnHullAsync(_hull, new Vector2(100.5f, 100.5f), []);
        EntityUid crate = default;
        EntityUid bullet = default;
        await Server.WaitPost(() =>
        {
            // Off the interior grid's edge, flying away from it.
            var offTheGrid = new MapCoordinates(new Vector2(8f, 3f), InteriorMapId(hull));
            var physicsSystem = SEntMan.System<SharedPhysicsSystem>();
            crate = SEntMan.Spawn(_crate, offTheGrid);
            bullet = SEntMan.Spawn(_bullet, offTheGrid);
            physicsSystem.SetLinearVelocity(crate, new Vector2(10f, 0f));
            physicsSystem.SetLinearVelocity(bullet, new Vector2(20f, 0f));
            Assert.That(SComp<PhysicsComponent>(crate).LinearVelocity.Length(), Is.GreaterThan(1f), "precondition: flying");
        });
        await Pair.RunTicksSync(3);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SComp<TransformComponent>(crate).MapID, Is.EqualTo(_mapId), "the crate is put outside");
            Assert.That(SComp<PhysicsComponent>(crate).LinearVelocity.Length(), Is.LessThan(0.01f), "at rest");
            Assert.That(SEntMan.Deleted(bullet) || SEntMan.IsQueuedForDeletion(bullet), Is.True, "the shot is spent, not carried out");
        });
    }
}

/// <summary>
///     Records the origin of every hit its entity takes.
/// </summary>
[RegisterComponent]
public sealed partial class KsCrewVehicleLandingTestDamageRecorderComponent : Component
{
    public readonly List<EntityUid?> Origins = new();
}

public sealed partial class KsCrewVehicleLandingTestDamageRecorderSystem : EntitySystem
{
    [SubscribeLocalEvent]
    private void OnDamageDealt(Entity<KsCrewVehicleLandingTestDamageRecorderComponent> entity, ref DamageDealtEvent args)
    {
        entity.Comp.Origins.Add(args.Origin);
    }
}
