#nullable enable
using System;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.CrewVehicle.Entry;
using Content.Server._KS14.CrewVehicle.Interior;
using Content.Shared._KS14.CrewVehicle.Interior;
using Content.Shared.Damage.Systems;
using Content.Shared.Destructible;
using Content.Shared.Movement.Pulling.Systems;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.CrewVehicle;

/// <summary>
///     What is inside a crew vehicle is put back in the world beside it: all of it when the vehicle is destroyed or
///         deleted, and anything that leaves the interior grid for the rest of the interior map.
/// </summary>
[TestFixture]
[TestOf(typeof(KsCrewVehicleEjectionSystem))]
public sealed class KsCrewVehicleEjectionTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false, Dirty = true };

    private readonly EntProtoId Hull = "KsCrewVehicleEjectionTestHull";
    private readonly EntProtoId Mob = "KsCrewVehicleEjectionTestMob";
    private readonly EntProtoId Crate = "KsCrewVehicleEjectionTestCrate";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsCrewVehicleEjectionTestHull
  components:
  - type: KsCrewVehicleInterior
    interiorPath: /Maps/Test/_KS14/CrewVehicle/basic.yml
    enterDelay: 0.5
    maxOccupants: 4
    ejectDamage:
      types:
        Blunt: 5
    doorways:
      left:
        offset: 1,0
      right:
        offset: -1,0

- type: entity
  id: KsCrewVehicleEjectionTestMob
  components:
  - type: DoAfter
  - type: Puller
    needsHands: false
  - type: Physics
    bodyType: KinematicController
  - type: MobState
  - type: Damageable
  - type: Injurable
    damageContainer: Biological

- type: entity
  id: KsCrewVehicleEjectionTestCrate
  components:
  - type: Pullable
  - type: Physics
    bodyType: Dynamic
";

    /// <summary>
    ///     How far from the hull's centre ejected things may land: the farthest ring of landing places, and a margin.
    /// </summary>
    private const float EjectReach = 4f;

    private SharedTransformSystem TransformSystem => SEntMan.System<SharedTransformSystem>();

    private MapCoordinates MapPosition(EntityUid uid) => TransformSystem.GetMapCoordinates(uid);

    private void AssertBeside(EntityUid uid, MapCoordinates centre, string what)
    {
        var position = MapPosition(uid);
        Assert.That(position.MapId, Is.EqualTo(centre.MapId), $"{what} is outside");
        Assert.That(Vector2.Distance(position.Position, centre.Position), Is.LessThan(EjectReach), $"{what} is beside the vehicle");
        Assert.That(SEntMan.HasComponent<KsCrewVehicleOccupantComponent>(uid), Is.False, $"{what} is no longer inside");
    }

    /// <param name="destroyed">Destroyed, rather than deleted outright.</param>
    [TestCase(true)]
    [TestCase(false)]
    public async Task LosingTheVehiclePutsEveryoneAndEverythingLooseOutside(bool destroyed)
    {
        var testMap = await Pair.CreateTestMap();
        var hullPosition = new MapCoordinates(new Vector2(0.5f, -3.5f), testMap.MapId);
        EntityUid hull = default;
        EntityUid driver = default;
        EntityUid pulledCrate = default;
        await Server.WaitPost(() =>
        {
            hull = SEntMan.Spawn(Hull, hullPosition);
            driver = SEntMan.Spawn(Mob, hullPosition.Offset(new Vector2(1f, 0f)));
            pulledCrate = SEntMan.Spawn(Crate, hullPosition.Offset(new Vector2(1.5f, 0f)));
            Assert.That(SEntMan.System<PullingSystem>().TryStartPull(driver, pulledCrate), Is.True, "precondition: pulling the crate");
            Assert.That(SEntMan.System<KsCrewVehicleEntrySystem>().TryStartEnter(SEntity<KsCrewVehicleInteriorComponent>(hull), driver), Is.True);
        });
        var enterDelay = SComp<KsCrewVehicleInteriorComponent>(hull).EnterDelay;
        await Pair.RunTicksSync((int)Math.Ceiling(enterDelay.TotalSeconds * Server.Timing.TickRate) + 5);

        EntityUid interiorMap = default;
        EntityUid spawnedIn = default;
        EntityUid teleportedIn = default;
        EntityUid droppedItem = default;
        await Server.WaitPost(() =>
        {
            interiorMap = SComp<KsCrewVehicleInteriorComponent>(hull).InteriorMap!.Value;
            Assert.That(SComp<TransformComponent>(pulledCrate).MapUid, Is.EqualTo(interiorMap), "precondition: the crate came in");
            var inside = new MapCoordinates(new Vector2(2.5f, 2.5f), SComp<MapComponent>(interiorMap).MapId);
            spawnedIn = SEntMan.Spawn(Mob, inside);
            teleportedIn = SEntMan.Spawn(Mob, new MapCoordinates(new Vector2(8f, 8f), testMap.MapId));
            TransformSystem.SetMapCoordinates(teleportedIn, inside);
            droppedItem = SEntMan.Spawn(Crate, inside);
        });
        await Pair.RunTicksSync(2);

        EntityUid untracked = default;
        await Server.WaitPost(() =>
        {
            Assert.That(SEntMan.HasComponent<KsCrewVehicleOccupantComponent>(spawnedIn), Is.True, "precondition: spawned in, an occupant");
            Assert.That(SEntMan.HasComponent<KsCrewVehicleOccupantComponent>(teleportedIn), Is.True, "precondition: teleported in, an occupant");

            // The control: lost in the same tick it arrives, before anything noticed it, so not tracked.
            var inside = new MapCoordinates(new Vector2(1.5f, 1.5f), SComp<MapComponent>(interiorMap).MapId);
            untracked = SEntMan.Spawn(Mob, new MapCoordinates(new Vector2(8f, 8f), testMap.MapId));
            TransformSystem.SetMapCoordinates(untracked, inside);
            Assert.That(SEntMan.HasComponent<KsCrewVehicleOccupantComponent>(untracked), Is.False, "precondition: not tracked");

            if (destroyed)
                SEntMan.System<SharedDestructibleSystem>().DestroyEntity(hull);
            else
                SEntMan.DeleteEntity(hull);
        });
        await Pair.RunTicksSync(3);

        await Server.WaitAssertion(() =>
        {
            AssertBeside(driver, hullPosition, "the one who came in by the door");
            AssertBeside(pulledCrate, hullPosition, "the crate they pulled in");
            AssertBeside(spawnedIn, hullPosition, "a mob spawned inside");
            AssertBeside(teleportedIn, hullPosition, "a mob teleported inside");
            AssertBeside(untracked, hullPosition, "a mob nothing tracked");
            AssertBeside(droppedItem, hullPosition, "an item on the floor");
            Assert.That(SEntMan.Deleted(interiorMap), Is.True, "the interior map is gone");
            Assert.That((float)SEntMan.System<DamageableSystem>().GetTotalDamage(driver), Is.GreaterThan(0f), "being thrown clear hurts");
        });
    }

    [Test]
    public async Task SomethingThatLeavesTheInteriorGridIsPutOutBesideTheVehicle()
    {
        var testMap = await Pair.CreateTestMap();
        var hullPosition = new MapCoordinates(new Vector2(0.5f, -3.5f), testMap.MapId);
        EntityUid mob = default;
        EntityUid item = default;
        await Server.WaitPost(() =>
        {
            var hull = SEntMan.Spawn(Hull, hullPosition);
            Assert.That(SEntMan.System<KsCrewVehicleInteriorSystem>().EnsureInterior(SEntity<KsCrewVehicleInteriorComponent>(hull), out var interiorMap), Is.True);

            // Far past the interior grid's edge, on the interior map.
            var offTheGrid = new MapCoordinates(new Vector2(30f, 30f), SComp<MapComponent>(interiorMap!.Value).MapId);
            mob = SEntMan.Spawn(Mob, new MapCoordinates(new Vector2(8f, 8f), testMap.MapId));
            item = SEntMan.Spawn(Crate, new MapCoordinates(new Vector2(8f, 8f), testMap.MapId));
            TransformSystem.SetMapCoordinates(mob, offTheGrid);
            TransformSystem.SetMapCoordinates(item, offTheGrid);
            Assert.That(SComp<TransformComponent>(mob).GridUid, Is.Null, "precondition: off the grid");
        });
        await Pair.RunTicksSync(2);

        await Server.WaitAssertion(() =>
        {
            AssertBeside(mob, hullPosition, "the mob");
            AssertBeside(item, hullPosition, "the item");
        });
    }
}
