#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.CrewVehicle.Interior;
using Content.Shared._KS14.CrewVehicle.Interior;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Light.Components;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.CrewVehicle;

/// <summary>
///     Crew vehicle interiors: each vehicle loads its own, and one that does not fit its vehicle is refused.
/// </summary>
[TestFixture]
[TestOf(typeof(KsCrewVehicleInteriorSystem))]
public sealed class KsCrewVehicleInteriorTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false, Dirty = true };

    private readonly EntProtoId Hull = "KsCrewVehicleInteriorTestHull";
    private readonly EntProtoId HullWithAnExtraDoorway = "KsCrewVehicleInteriorTestHullExtraDoorway";
    private readonly EntProtoId HullMissingADoorway = "KsCrewVehicleInteriorTestHullMissingDoorway";
    private readonly EntProtoId HullWithAPassenger = "KsCrewVehicleInteriorTestHullPassenger";

    // The interior has exit doors for "left" and "right" (Maps/Test/_KS14/CrewVehicle/basic.yml).
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsCrewVehicleInteriorTestHull
  components:
  - type: KsCrewVehicleInterior
    interiorPath: /Maps/Test/_KS14/CrewVehicle/basic.yml
    interiorAnchor: 2.5,3
    cabinAmbient: '#303036'
    doorways:
      left:
        offset: -1,0
      right:
        offset: 1,0

- type: entity
  id: KsCrewVehicleInteriorTestHullPassenger
  components:
  - type: KsCrewVehicleInterior
    interiorPath: /Maps/Test/_KS14/CrewVehicle/passenger.yml
    doorways:
      left:
        offset: 1,0
      right:
        offset: -1,0

- type: entity
  id: KsCrewVehicleInteriorTestHullExtraDoorway
  components:
  - type: KsCrewVehicleInterior
    interiorPath: /Maps/Test/_KS14/CrewVehicle/basic.yml
    doorways:
      left:
        offset: -1,0
      right:
        offset: 1,0
      rear:
        offset: 0,-1

- type: entity
  id: KsCrewVehicleInteriorTestHullMissingDoorway
  components:
  - type: KsCrewVehicleInterior
    interiorPath: /Maps/Test/_KS14/CrewVehicle/basic.yml
    doorways:
      left:
        offset: -1,0
";

    private KsCrewVehicleInteriorSystem Interiors => SEntMan.System<KsCrewVehicleInteriorSystem>();

    private int CountMaps()
    {
        return SEntMan.AllEntities<MapComponent>().Count();
    }

    [Test]
    public async Task EachVehicleGetsItsOwnInterior()
    {
        var testMap = await Pair.CreateTestMap();

        EntityUid hullA = default;
        EntityUid hullB = default;
        EntityUid interiorA = default;
        EntityUid interiorB = default;
        await Server.WaitPost(() =>
        {
            hullA = SEntMan.SpawnAtPosition(Hull, new EntityCoordinates(testMap.Grid, new Vector2(0.5f, 0.5f)));
            hullB = SEntMan.SpawnAtPosition(Hull, new EntityCoordinates(testMap.Grid, new Vector2(4.5f, 0.5f)));

            Assert.That(Interiors.EnsureInterior(SEntity<KsCrewVehicleInteriorComponent>(hullA), out var mapA), Is.True);
            Assert.That(Interiors.EnsureInterior(SEntity<KsCrewVehicleInteriorComponent>(hullB), out var mapB), Is.True);
            interiorA = mapA!.Value;
            interiorB = mapB!.Value;
        });

        await Server.WaitAssertion(() =>
        {
            var mapSystem = SEntMan.System<SharedMapSystem>();
            Assert.That(interiorA, Is.Not.EqualTo(interiorB), "two vehicles, two interiors");

            foreach (var (hull, interior) in new[] { (hullA, interiorA), (hullB, interiorB) })
            {
                Assert.That(SComp<KsCrewVehicleInteriorMapComponent>(interior).Vehicle, Is.EqualTo(hull), "the interior links back to its own vehicle");
                Assert.That(SComp<KsCrewVehicleInteriorMapComponent>(interior).CabinAmbient,
                    Is.EqualTo(SComp<KsCrewVehicleInteriorComponent>(hull).CabinAmbient).And.Not.EqualTo(Color.Black),
                    "the interior map carries the vehicle's cabin ambient to the client's renderer");
                Assert.That(SComp<KsCrewVehicleInteriorComponent>(hull).InteriorMap, Is.EqualTo(interior));
                Assert.That(mapSystem.IsInitialized(interior), Is.True, "a linked interior has been initialised");
                Assert.That(mapSystem.IsPaused(interior), Is.False, "and runs");

                var grids = SEntMan.AllEntities<MapGridComponent>().Where(grid => SComp<TransformComponent>(grid).MapUid == interior).ToList();
                Assert.That(grids, Has.Count.EqualTo(1));
                Assert.That(SEntMan.HasComponent<ImplicitRoofComponent>(grids[0]), Is.False, "nothing roofs the interior, so its map's light reaches it");

                // The air is the interior map's fixed, breathable one, from the moment it loads.
                Assert.That(SEntMan.HasComponent<GridAtmosphereComponent>(grids[0]), Is.False, "the interior grid has no atmosphere of its own");
                var air = SEntMan.System<AtmosphereSystem>().GetTileMixture(grids[0].Owner, interior, new Vector2i(2, 2));
                Assert.That(air, Is.Not.Null);
                Assert.That(air!.Immutable, Is.True, "the air inside is fixed");
                Assert.That(air.GetMoles(Gas.Oxygen), Is.GreaterThan(0f), "and has oxygen");
                Assert.That(air.Pressure, Is.EqualTo(Atmospherics.OneAtmosphere).Within(0.1f * Atmospherics.OneAtmosphere), "at about one atmosphere");
            }

            var mapsBefore = CountMaps();
            Assert.That(Interiors.EnsureInterior(SEntity<KsCrewVehicleInteriorComponent>(hullA), out var again), Is.True);
            Assert.That(again, Is.EqualTo(interiorA), "asking again gives the same interior");
            Assert.That(CountMaps(), Is.EqualTo(mapsBefore), "and loads nothing");

            var insideA = SEntMan.Spawn(null, new MapCoordinates(new Vector2(1f, 1f), SComp<MapComponent>(interiorA).MapId));
            Assert.That(Interiors.TryGetVehicle(insideA, out var vehicle), Is.True);
            Assert.That(vehicle!.Value.Owner, Is.EqualTo(hullA), "something inside A is in A, not B");

            var outside = SEntMan.SpawnAtPosition(null, new EntityCoordinates(testMap.Grid, new Vector2(2.5f, 0.5f)));
            Assert.That(Interiors.TryGetVehicle(outside, out _), Is.False, "the control: something outside is in no vehicle");
        });
    }

    /// <param name="vehicleHasAnExtraDoorway">
    ///     True: the vehicle has a doorway, "rear", that no door inside names. False: it lacks "right", which a door
    ///         inside names.
    /// </param>
    [TestCase(true, "rear")]
    [TestCase(false, "right")]
    public async Task BadInteriorIsRefused(bool vehicleHasAnExtraDoorway, string offendingId)
    {
        var hullId = vehicleHasAnExtraDoorway ? HullWithAnExtraDoorway : HullMissingADoorway;
        var testMap = await Pair.CreateTestMap();

        EntityUid hull = default;
        var mapsBefore = 0;
        var errors = new List<string>();
        await Server.WaitPost(() =>
        {
            hull = SEntMan.SpawnAtPosition(hullId, new EntityCoordinates(testMap.Grid, new Vector2(0.5f, 0.5f)));
            mapsBefore = CountMaps();

            // The refusal logs an error, which would fail the test: silence this system's log for the call only.
            var log = Interiors.Log;
            var level = log.Level;
            log.Level = LogLevel.Fatal;
            try
            {
                Assert.That(Interiors.EnsureInterior(SEntity<KsCrewVehicleInteriorComponent>(hull), out var interior, errors), Is.False);
                Assert.That(interior, Is.Null);
            }
            finally
            {
                log.Level = level;
            }
        });

        await Pair.RunTicksSync(1);

        await Server.WaitAssertion(() =>
        {
            Assert.That(errors, Has.Some.Contains($"'{offendingId}'"), "the error names the doorway at fault");
            Assert.That(SComp<KsCrewVehicleInteriorComponent>(hull).InteriorMap, Is.Null, "the vehicle has no interior");
            Assert.That(CountMaps(), Is.EqualTo(mapsBefore), "and the refused map is gone");
            Assert.That(SEntMan.AllEntities<KsCrewVehicleInteriorMapComponent>().Any(interior => interior.Comp.Vehicle == hull),
                Is.False, "nothing was linked to it");
        });
    }

    [Test]
    public async Task SomeonePlacedInTheMapFileIsInsideOnceItLoads()
    {
        var testMap = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var hull = SEntMan.SpawnAtPosition(HullWithAPassenger, new EntityCoordinates(testMap.Grid, new Vector2(0.5f, 0.5f)));
            Assert.That(Interiors.EnsureInterior(SEntity<KsCrewVehicleInteriorComponent>(hull), out var interior), Is.True);

            var passengers = SEntMan.AllEntities<Content.Shared.Mobs.Components.MobStateComponent>()
                .Where(mob => SComp<TransformComponent>(mob).MapUid == interior)
                .ToList();
            Assert.That(passengers, Has.Count.EqualTo(1), "precondition: the map file has one mob");
            Assert.That(STryComp<KsCrewVehicleOccupantComponent>(passengers[0], out var occupant), Is.True, "it is an occupant at once");
            Assert.That(occupant!.Vehicle, Is.EqualTo(hull));
        });
    }
}
