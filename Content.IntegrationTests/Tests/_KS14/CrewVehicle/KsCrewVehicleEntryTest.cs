#nullable enable
using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.CrewVehicle.Entry;
using Content.Server._KS14.CrewVehicle.Interior;
using Content.Shared._KS14.CrewVehicle.Interior;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.CrewVehicle;

/// <summary>
///     Getting into and out of a crew vehicle through its doorways.
/// </summary>
[TestFixture]
[TestOf(typeof(KsCrewVehicleEntrySystem))]
[TestOf(typeof(KsCrewVehicleExitSystem))]
public sealed class KsCrewVehicleEntryTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false, Dirty = true };

    private readonly EntProtoId Hull = "KsCrewVehicleEntryTestHull";
    private readonly EntProtoId HullWithNoRoom = "KsCrewVehicleEntryTestHullNoRoom";
    private readonly EntProtoId Mob = "KsCrewVehicleEntryTestMob";
    private readonly EntProtoId Crate = "KsCrewVehicleEntryTestCrate";
    private readonly EntProtoId Blocker = "KsCrewVehicleEntryTestBlocker";
    private static readonly EntProtoId Ghost = "MobObserver";

    // The interior (Maps/Test/_KS14/CrewVehicle/basic.yml) has its "left" door on its west wall and its "right" door on
    //      its east wall. The interior faces grid north and the hull faces its local -Y (KsCrewVehicleHullFrame), so the
    //      hull's left is its local +X.
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsCrewVehicleEntryTestHull
  components:
  - type: Physics
    bodyType: KinematicController
  # A solid hull: the doorways' landing points are less than a tile from its sides, so the tile someone lands on
  #   overlaps it.
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeAabb
          bounds: -0.65,-1.2,0.65,1.2
        layer:
        - MidImpassable
        - LowImpassable
  - type: KsCrewVehicleInterior
    interiorPath: /Maps/Test/_KS14/CrewVehicle/basic.yml
    interiorAnchor: 2.5,3
    enterDelay: 0.5
    exitDelay: 0.5
    maxOccupants: 2
    doorways:
      left:
        offset: 1,0
      right:
        offset: -1,0

- type: entity
  id: KsCrewVehicleEntryTestHullNoRoom
  components:
  - type: KsCrewVehicleInterior
    interiorPath: /Maps/Test/_KS14/CrewVehicle/basic.yml
    maxOccupants: 0
    doorways:
      left:
        offset: 1,0
      right:
        offset: -1,0

- type: entity
  id: KsCrewVehicleEntryTestMob
  components:
  - type: DoAfter
  - type: Puller
    needsHands: false
  - type: Physics
    bodyType: KinematicController
  - type: MobState

- type: entity
  id: KsCrewVehicleEntryTestCrate
  components:
  - type: Pullable
  - type: Physics
    bodyType: Dynamic

- type: entity
  id: KsCrewVehicleEntryTestBlocker
  components:
  - type: Transform
    anchored: true
  - type: Physics
    bodyType: Static
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeAabb
          bounds: -0.5,-0.5,0.5,0.5
        layer:
        - WallLayer
";

    private KsCrewVehicleInteriorSystem Interiors => SEntMan.System<KsCrewVehicleInteriorSystem>();

    private MapCoordinates MapPosition(EntityUid uid)
        => SEntMan.System<SharedTransformSystem>().GetMapCoordinates(uid);

    private void Use(EntityUid user, EntityUid target)
    {
        SEntMan.System<SharedInteractionSystem>().InteractionActivate(user, target,
            checkCanInteract: false, checkUseDelay: false, checkAccess: false, complexInteractions: true);
    }

    private int TicksFor(TimeSpan delay)
    {
        return (int)Math.Ceiling(delay.TotalSeconds * Server.Timing.TickRate) + 5;
    }

    private Entity<KsCrewVehicleInteriorComponent> Vehicle(EntityUid hull) => SEntity<KsCrewVehicleInteriorComponent>(hull);

    /// <summary>
    ///     A test map with a hull at (0.5, -0.5), on floor that reaches two tiles round it, so that the hull is on the
    ///         grid it lands people on; and where, at rotation 0, its doorways are.
    /// </summary>
    private async Task<(MapId MapId, EntityUid Hull, Vector2 Left, Vector2 Right)> SpawnHullAsync(EntProtoId hullPrototype)
    {
        var testMap = await Pair.CreateTestMap();
        EntityUid hull = default;
        var hullPosition = new Vector2(0.5f, -0.5f);
        await Server.WaitPost(() =>
        {
            var mapSystem = SEntMan.System<SharedMapSystem>();
            for (var x = -2; x <= 2; x++)
            {
                for (var y = -3; y <= 1; y++)
                {
                    mapSystem.SetTile(testMap.Grid, new Vector2i(x, y), testMap.Tile.Tile);
                }
            }

            hull = SEntMan.Spawn(hullPrototype, new MapCoordinates(hullPosition, testMap.MapId));
            Assert.That(SComp<TransformComponent>(hull).GridUid, Is.EqualTo(testMap.Grid.Owner), "precondition: the hull is on the grid");
        });
        return (testMap.MapId, hull, hullPosition + new Vector2(1f, 0f), hullPosition + new Vector2(-1f, 0f));
    }

    private async Task EnterAsync(EntityUid user, EntityUid hull)
    {
        await Server.WaitPost(() => Assert.That(SEntMan.System<KsCrewVehicleEntrySystem>().TryStartEnter(Vehicle(hull), user), Is.True));
        await Pair.RunTicksSync(TicksFor(SComp<KsCrewVehicleInteriorComponent>(hull).EnterDelay));
    }

    private EntityUid ExitDoor(EntityUid hull, string doorway)
    {
        var interiorMap = SComp<KsCrewVehicleInteriorComponent>(hull).InteriorMap;
        Assert.That(interiorMap, Is.Not.Null, "the interior is loaded");
        // Found here rather than through the system, which is under test.
        var doors = SEntMan.AllEntities<KsCrewVehicleExitComponent>()
            .Where(door => door.Comp.Doorway == doorway && SComp<TransformComponent>(door).MapUid == interiorMap)
            .ToList();
        Assert.That(doors, Has.Count.EqualTo(1));
        return doors[0].Owner;
    }

    [TestCase("left")]
    [TestCase("right")]
    public async Task UsingTheVehicleAtADoorwayTakesYouInByItsDoor(string doorway)
    {
        var (mapId, hull, left, right) = await SpawnHullAsync(Hull);
        EntityUid user = default;
        EntityUid farUser = default;
        await Server.WaitPost(() =>
        {
            user = SEntMan.Spawn(Mob, new MapCoordinates(doorway == "left" ? left : right, mapId));
            farUser = SEntMan.Spawn(Mob, new MapCoordinates(new Vector2(0.5f, 3f), mapId));
            Use(user, hull);
            Use(farUser, hull);

            Assert.That(MapPosition(user).MapId, Is.EqualTo(mapId), "nobody gets in before the do-after ends");
            Assert.That(SComp<DoAfterComponent>(user).DoAfters, Is.Not.Empty);
            Assert.That(SComp<DoAfterComponent>(farUser).DoAfters, Is.Empty, "the control: away from every doorway, nothing starts");
        });

        await Pair.RunTicksSync(TicksFor(SComp<KsCrewVehicleInteriorComponent>(hull).EnterDelay));

        await Server.WaitAssertion(() =>
        {
            var door = ExitDoor(hull, doorway);
            Assert.That(MapPosition(user), Is.EqualTo(MapPosition(door)), "they stand on the door of the doorway they used");
            Assert.That(SComp<KsCrewVehicleOccupantComponent>(user).Vehicle, Is.EqualTo(hull));
            Assert.That(MapPosition(farUser).MapId, Is.EqualTo(mapId), "the control is still outside");
            Assert.That(SEntMan.HasComponent<KsCrewVehicleOccupantComponent>(farUser), Is.False);
        });
    }

    [Test]
    public async Task WhatYouPullComesInStillPulled()
    {
        var (mapId, hull, left, _) = await SpawnHullAsync(Hull);
        EntityUid user = default;
        EntityUid crate = default;
        await Server.WaitPost(() =>
        {
            user = SEntMan.Spawn(Mob, new MapCoordinates(left, mapId));
            crate = SEntMan.Spawn(Crate, new MapCoordinates(left + new Vector2(0.5f, 0f), mapId));
            Assert.That(SEntMan.System<PullingSystem>().TryStartPull(user, crate), Is.True, "precondition: the user pulls the crate");
        });

        await EnterAsync(user, hull);

        await Server.WaitAssertion(() =>
        {
            var interiorMap = SComp<KsCrewVehicleInteriorComponent>(hull).InteriorMap;
            Assert.That(SComp<TransformComponent>(user).MapUid, Is.EqualTo(interiorMap));
            Assert.That(SComp<TransformComponent>(crate).MapUid, Is.EqualTo(interiorMap), "the crate came in too");
            Assert.That(SComp<PullerComponent>(user).Pulling, Is.EqualTo(crate), "and is still pulled");
            Assert.That(SEntMan.HasComponent<KsCrewVehicleOccupantComponent>(crate), Is.False, "a crate is not an occupant");
        });
    }

    [Test]
    public async Task ThereIsRoomOnlyForTheLivingCapacity()
    {
        var (mapId, hull, left, _) = await SpawnHullAsync(Hull);
        var capacity = SComp<KsCrewVehicleInteriorComponent>(hull).MaxOccupants;
        var users = new EntityUid[capacity + 1];
        await Server.WaitPost(() =>
        {
            // All start at once: each passes the check at the start, so the one too many is refused at the end.
            for (var i = 0; i < users.Length; i++)
            {
                users[i] = SEntMan.Spawn(Mob, new MapCoordinates(left, mapId));
                Assert.That(SEntMan.System<KsCrewVehicleEntrySystem>().TryStartEnter(Vehicle(hull), users[i]), Is.True);
            }
        });
        await Pair.RunTicksSync(TicksFor(SComp<KsCrewVehicleInteriorComponent>(hull).EnterDelay));

        var lastUser = users[capacity];
        await Server.WaitAssertion(() =>
        {
            Assert.That(Interiors.CountLivingOccupants(hull), Is.EqualTo(capacity), "it filled up");
            Assert.That(MapPosition(lastUser).MapId, Is.EqualTo(mapId), "and the one too many stayed outside");
            Assert.That(SEntMan.System<KsCrewVehicleEntrySystem>().TryStartEnter(Vehicle(hull), lastUser), Is.False, "full");

            SEntMan.System<MobStateSystem>().ChangeMobState(users[0], MobState.Dead);
        });

        await EnterAsync(lastUser, hull);
        await Server.WaitAssertion(() =>
            Assert.That(SComp<TransformComponent>(lastUser).MapUid, Is.EqualTo(SComp<KsCrewVehicleInteriorComponent>(hull).InteriorMap),
                "the dead do not count, so there was room again"));
    }

    [Test]
    public async Task AGhostGetsInAtOnceEvenWithNoRoom()
    {
        var (mapId, hull, left, _) = await SpawnHullAsync(HullWithNoRoom);
        await Server.WaitAssertion(() =>
        {
            var living = SEntMan.Spawn(Mob, new MapCoordinates(left, mapId));
            Assert.That(SEntMan.System<KsCrewVehicleEntrySystem>().TryStartEnter(Vehicle(hull), living), Is.False,
                "the control: there is no room for the living");

            var ghost = SEntMan.Spawn(Ghost, new MapCoordinates(left, mapId));
            Assert.That(SEntMan.System<KsCrewVehicleEntrySystem>().TryStartEnter(Vehicle(hull), ghost), Is.True);
            Assert.That(SComp<TransformComponent>(ghost).MapUid, Is.EqualTo(SComp<KsCrewVehicleInteriorComponent>(hull).InteriorMap),
                "the ghost is inside at once, with no do-after");
        });
    }

    /// <param name="blocked">Something stands where they would land.</param>
    /// <param name="moving">The hull is moving.</param>
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    public async Task GettingOutLandsAtTheDoorwayAsTheHullNowStands(bool blocked, bool moving)
    {
        var (mapId, hull, left, _) = await SpawnHullAsync(Hull);
        EntityUid user = default;
        await Server.WaitPost(() => user = SEntMan.Spawn(Mob, new MapCoordinates(left, mapId)));
        await EnterAsync(user, hull);

        // Turned a quarter, the left doorway (local +X) is north of the hull: on the test grid's one tile at (0, 0).
        var expectedLanding = new Vector2(0.5f, 0.5f);
        await Server.WaitPost(() =>
        {
            SEntMan.System<SharedTransformSystem>().SetWorldRotation(hull, Angle.FromDegrees(90));
            if (blocked)
                SEntMan.Spawn(Blocker, new MapCoordinates(expectedLanding, mapId));

            if (moving)
            {
                var speed = SComp<KsCrewVehicleInteriorComponent>(hull).MaxExitSpeed * 2f + 1f;
                SEntMan.System<SharedPhysicsSystem>().SetLinearVelocity(hull, new Vector2(speed, 0f));
            }

            var door = SEntity<KsCrewVehicleExitComponent>(ExitDoor(hull, "left"));
            Assert.That(SEntMan.System<KsCrewVehicleExitSystem>().TryStartExit(door, user), Is.EqualTo(!blocked && !moving));
        });
        await Pair.RunTicksSync(TicksFor(SComp<KsCrewVehicleInteriorComponent>(hull).ExitDelay));

        await Server.WaitAssertion(() =>
        {
            if (blocked || moving)
            {
                Assert.That(SComp<TransformComponent>(user).MapUid, Is.EqualTo(SComp<KsCrewVehicleInteriorComponent>(hull).InteriorMap),
                    "refused: they are still inside");
                Assert.That(SEntMan.HasComponent<KsCrewVehicleOccupantComponent>(user), Is.True);
                return;
            }

            var landing = MapPosition(user);
            Assert.That(landing.MapId, Is.EqualTo(mapId), "they are outside");
            Assert.That(Vector2.Distance(landing.Position, expectedLanding), Is.LessThan(0.01f),
                $"at the doorway as the hull now stands, not as it stood: {landing.Position}");
            Assert.That(SEntMan.HasComponent<KsCrewVehicleOccupantComponent>(user), Is.False, "and no longer tracked as inside");
        });
    }

    [Test]
    public async Task LeavingTheInteriorAnyOtherWayStopsTracking()
    {
        var (mapId, hull, left, _) = await SpawnHullAsync(Hull);
        EntityUid user = default;
        await Server.WaitPost(() => user = SEntMan.Spawn(Mob, new MapCoordinates(left, mapId)));
        await EnterAsync(user, hull);

        await Server.WaitPost(() =>
        {
            Assert.That(SEntMan.HasComponent<KsCrewVehicleOccupantComponent>(user), Is.True, "precondition: inside");
            SEntMan.System<SharedTransformSystem>().SetMapCoordinates(user, new MapCoordinates(new Vector2(5f, 5f), mapId));
        });
        await Pair.RunTicksSync(1);

        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.HasComponent<KsCrewVehicleOccupantComponent>(user), Is.False, "teleported out, they are no longer inside"));
    }

    /// <param name="spawned">Spawned on the interior grid, rather than teleported onto it.</param>
    [TestCase(true)]
    [TestCase(false)]
    public async Task WhoeverIsOnTheInteriorGridIsAnOccupant(bool spawned)
    {
        var (mapId, hull, _, _) = await SpawnHullAsync(Hull);
        EntityUid mob = default;
        EntityUid crate = default;
        await Server.WaitPost(() =>
        {
            Assert.That(Interiors.EnsureInterior(Vehicle(hull), out var interiorMap), Is.True);
            var inside = new MapCoordinates(new Vector2(2.5f, 1.5f), SComp<MapComponent>(interiorMap!.Value).MapId);
            if (spawned)
            {
                mob = SEntMan.Spawn(Mob, inside);
                crate = SEntMan.Spawn(Crate, inside);
                return;
            }

            mob = SEntMan.Spawn(Mob, new MapCoordinates(new Vector2(5f, 5f), mapId));
            crate = SEntMan.Spawn(Crate, new MapCoordinates(new Vector2(5f, 5f), mapId));
            var transformSystem = SEntMan.System<SharedTransformSystem>();
            transformSystem.SetMapCoordinates(mob, inside);
            transformSystem.SetMapCoordinates(crate, inside);
        });
        await Pair.RunTicksSync(2);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SComp<TransformComponent>(mob).GridUid, Is.Not.Null, "precondition: on the interior grid");
            Assert.That(SComp<KsCrewVehicleOccupantComponent>(mob).Vehicle, Is.EqualTo(hull), "a mob there is inside, door or not");
            Assert.That(SEntMan.HasComponent<KsCrewVehicleOccupantComponent>(crate), Is.False, "the control: an item there is not");
        });
    }
}
