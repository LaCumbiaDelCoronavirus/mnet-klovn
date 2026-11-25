#nullable enable
using System;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.CrewVehicle.Entry;
using Content.Server._KS14.CrewVehicle.Interior;
using Content.Shared._KS14.CrewVehicle.Interior;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.CrewVehicle;

/// <summary>
///     Whatever leaves a crew vehicle, by a door or thrown clear, ends up on the grid under where it lands, turned or
///         not, and on the map only where there is no grid.
/// </summary>
[TestFixture]
[TestOf(typeof(KsCrewVehicleInteriorSystem))]
public sealed class KsCrewVehiclePlacementTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false, Dirty = true };

    private readonly EntProtoId _hull = "KsCrewVehiclePlacementTestHull";
    private readonly EntProtoId _mob = "KsCrewVehiclePlacementTestMob";
    private readonly EntProtoId _crate = "KsCrewVehiclePlacementTestCrate";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsCrewVehiclePlacementTestHull
  components:
  - type: KsCrewVehicleInterior
    interiorPath: /Maps/Test/_KS14/CrewVehicle/basic.yml
    enterDelay: 0.5
    exitDelay: 0.5
    doorways:
      left:
        offset: 1,0
      right:
        offset: -1,0

- type: entity
  id: KsCrewVehiclePlacementTestMob
  components:
  - type: DoAfter
  - type: Puller
    needsHands: false
  - type: Physics
    bodyType: KinematicController
  - type: MobState

- type: entity
  id: KsCrewVehiclePlacementTestCrate
  components:
  - type: Pullable
  - type: Physics
    bodyType: Dynamic
";

    public enum Ground
    {
        /// <summary>On a grid, as built.</summary>
        Grid,

        /// <summary>On a grid turned off the world's axes.</summary>
        RotatedGrid,

        /// <summary>Out in space, on no grid.</summary>
        Space,
    }

    /// <summary>
    ///     A hull on the given ground, and what anything leaving it should end up parented to.
    /// </summary>
    private async Task<(EntityUid Hull, EntityUid ExpectedParent, MapId MapId)> SpawnHullAsync(Ground ground)
    {
        var testMap = await Pair.CreateTestMap();
        EntityUid hull = default;
        var expectedParent = ground == Ground.Space ? testMap.MapUid : testMap.Grid.Owner;
        await Server.WaitPost(() =>
        {
            var mapSystem = SEntMan.System<SharedMapSystem>();
            var transformSystem = SEntMan.System<SharedTransformSystem>();
            if (ground == Ground.RotatedGrid)
                transformSystem.SetWorldRotation(testMap.Grid.Owner, Angle.FromDegrees(30));

            // Floor well past every landing place round the hull.
            for (var x = -6; x <= 6; x++)
            {
                for (var y = -6; y <= 6; y++)
                {
                    mapSystem.SetTile(testMap.Grid, new Vector2i(x, y), testMap.Tile.Tile);
                }
            }

            hull = ground == Ground.Space
                ? SEntMan.Spawn(_hull, new MapCoordinates(new Vector2(100f, 100f), testMap.MapId))
                : SEntMan.SpawnAtPosition(_hull, new EntityCoordinates(testMap.Grid, new Vector2(0.5f, 0.5f)));
        });
        return (hull, expectedParent, testMap.MapId);
    }

    private async Task<(EntityUid User, EntityUid Crate)> EnterPullingACrateAsync(EntityUid hull, MapId mapId)
    {
        EntityUid user = default;
        EntityUid crate = default;
        await Server.WaitPost(() =>
        {
            var (hullPosition, hullRotation) = SEntMan.System<SharedTransformSystem>().GetWorldPositionRotation(hull);
            var doorway = hullPosition + hullRotation.RotateVec(new Vector2(1f, 0f));
            user = SEntMan.Spawn(_mob, new MapCoordinates(doorway, mapId));
            crate = SEntMan.Spawn(_crate, new MapCoordinates(doorway, mapId));
            Assert.That(SEntMan.System<PullingSystem>().TryStartPull(user, crate), Is.True, "precondition: pulling the crate");
            Assert.That(SEntMan.System<KsCrewVehicleEntrySystem>().TryStartEnter(SEntity<KsCrewVehicleInteriorComponent>(hull), user), Is.True);
        });
        await Pair.RunTicksSync(TicksFor(SComp<KsCrewVehicleInteriorComponent>(hull).EnterDelay));

        await Server.WaitAssertion(() =>
        {
            var interiorMap = SComp<KsCrewVehicleInteriorComponent>(hull).InteriorMap;
            var userTransform = SComp<TransformComponent>(user);
            Assert.That(userTransform.MapUid, Is.EqualTo(interiorMap), "precondition: inside");
            Assert.That(userTransform.ParentUid, Is.EqualTo(userTransform.GridUid), "getting in puts them on the interior grid");
            Assert.That(SComp<TransformComponent>(crate).ParentUid, Is.EqualTo(userTransform.GridUid), "and what they pull");
        });
        return (user, crate);
    }

    private int TicksFor(TimeSpan delay)
    {
        return (int)Math.Ceiling(delay.TotalSeconds * Server.Timing.TickRate) + 5;
    }

    private Task WatchAsync(MapId outside, params EntityUid[] watched)
    {
        return Server.WaitPost(() =>
        {
            foreach (var uid in watched)
            {
                SEntMan.EnsureComponent<KsCrewVehiclePlacementTestWatchComponent>(uid).Outside = outside;
            }
        });
    }

    private void AssertPlacedOn(EntityUid uid, EntityUid expectedParent, string what)
    {
        var parent = SComp<KsCrewVehiclePlacementTestWatchComponent>(uid).FirstParentOutside;
        Assert.That(parent, Is.Not.Null, $"{what} was put outside");
        Assert.That(parent, Is.EqualTo(expectedParent), $"{what} was put on the ground under it: the grid where there is one, else the map");
    }

    [TestCase(Ground.Grid)]
    [TestCase(Ground.RotatedGrid)]
    [TestCase(Ground.Space)]
    public async Task GettingOutLeavesYouOnTheGroundBesideTheVehicle(Ground ground)
    {
        var (hull, expectedParent, mapId) = await SpawnHullAsync(ground);
        var (user, crate) = await EnterPullingACrateAsync(hull, mapId);
        await WatchAsync(mapId, user, crate);

        await Server.WaitPost(() =>
        {
            var interiorMap = SComp<KsCrewVehicleInteriorComponent>(hull).InteriorMap!.Value;
            Assert.That(SEntMan.System<KsCrewVehicleInteriorSystem>().TryGetExitDoor(interiorMap, "left", out var door), Is.True);
            Assert.That(SEntMan.System<KsCrewVehicleExitSystem>().TryStartExit(SEntity<KsCrewVehicleExitComponent>(door!.Value), user), Is.True);
        });
        await Pair.RunTicksSync(TicksFor(SComp<KsCrewVehicleInteriorComponent>(hull).ExitDelay));


        await Server.WaitAssertion(() =>
        {
            AssertPlacedOn(user, expectedParent, "the one getting out");
            AssertPlacedOn(crate, expectedParent, "what they pull");
            Assert.That(SComp<PullerComponent>(user).Pulling, Is.EqualTo(crate), "still pulled");
        });
    }

    [TestCase(Ground.Grid)]
    [TestCase(Ground.RotatedGrid)]
    [TestCase(Ground.Space)]
    public async Task ThrownClearByTheVehicleBeingLostLeavesYouOnTheGround(Ground ground)
    {
        var (hull, expectedParent, mapId) = await SpawnHullAsync(ground);
        var (user, crate) = await EnterPullingACrateAsync(hull, mapId);
        await WatchAsync(mapId, user, crate);

        await Server.WaitPost(() => SEntMan.DeleteEntity(hull));
        await Pair.RunTicksSync(3);

        await Server.WaitAssertion(() =>
        {
            AssertPlacedOn(user, expectedParent, "the mob");
            AssertPlacedOn(crate, expectedParent, "the crate");
        });
    }

    [TestCase(Ground.Grid)]
    [TestCase(Ground.RotatedGrid)]
    [TestCase(Ground.Space)]
    public async Task LeavingTheInteriorGridLeavesYouOnTheGroundBesideTheVehicle(Ground ground)
    {
        var (hull, expectedParent, mapId) = await SpawnHullAsync(ground);
        EntityUid mob = default;
        EntityUid item = default;
        await Server.WaitPost(() =>
        {
            Assert.That(SEntMan.System<KsCrewVehicleInteriorSystem>().EnsureInterior(SEntity<KsCrewVehicleInteriorComponent>(hull), out var interiorMap), Is.True);
            var offTheGrid = new MapCoordinates(new Vector2(30f, 30f), SComp<MapComponent>(interiorMap!.Value).MapId);
            mob = SEntMan.Spawn(_mob, offTheGrid);
            item = SEntMan.Spawn(_crate, offTheGrid);
        });
        await WatchAsync(mapId, mob, item);
        await Pair.RunTicksSync(2);

        await Server.WaitAssertion(() =>
        {
            AssertPlacedOn(mob, expectedParent, "the mob");
            AssertPlacedOn(item, expectedParent, "the item");
        });
    }
}

/// <summary>
///     The parent an entity is first given on the outside map: where it was put. A moving body is later moved onto
///         the grid under it by physics anyway, which would hide a wrong placement; one at rest, as a player standing
///         still, would stay wrong.
/// </summary>
[RegisterComponent]
public sealed partial class KsCrewVehiclePlacementTestWatchComponent : Component
{
    public MapId Outside;
    public EntityUid? FirstParentOutside;
}

public sealed partial class KsCrewVehiclePlacementTestWatchSystem : EntitySystem
{
    [SubscribeLocalEvent]
    private void OnParentChanged(Entity<KsCrewVehiclePlacementTestWatchComponent> entity, ref EntParentChangedMessage args)
    {
        if (entity.Comp.FirstParentOutside == null && args.Transform.MapID == entity.Comp.Outside)
            entity.Comp.FirstParentOutside = args.Transform.ParentUid;
    }
}
