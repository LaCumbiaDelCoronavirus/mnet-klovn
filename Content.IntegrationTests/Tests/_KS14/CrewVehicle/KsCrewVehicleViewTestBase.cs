#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._KS14.CrewVehicle.Entry;
using Content.Server._KS14.CrewVehicle.Interior;
using Content.Shared._KS14.CrewVehicle.Interior;
using Content.Shared._KS14.ZLevel;
using NUnit.Framework;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.CrewVehicle;

/// <summary>
///     What the view tests share: a parked test vehicle with a view seat inside, a mob for the player, PVS on, and
///         markers spawned outside to see what the client is sent.
/// </summary>
/// <remarks>
///     Pooled pairs run with net.pvs off, which sends everything to everyone, so <see cref="SetUpVehicleAsync"/> turns
///         it on. Leaving PVS only detaches an entity on the client, so the tests look for markers spawned after the
///         state under test began.
/// </remarks>
public abstract class KsCrewVehicleViewTestBase : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    protected static readonly EntProtoId ViewerPrototype = "KsCrewVehicleOutsideViewer";

    protected readonly EntProtoId HullPrototype = "KsCrewVehicleViewTestHull";
    protected readonly EntProtoId NarrowSeatHullPrototype = "KsCrewVehicleViewTestHullNarrowSeat";
    protected readonly EntProtoId MobPrototype = "KsCrewVehicleViewTestMob";
    private readonly EntProtoId _markerPrototype = "KsCrewVehicleViewTestMarker";

    // The interiors (Maps/Test/_KS14/CrewVehicle/view_seat.yml and view_seat_bad.yml) are basic.yml with seats added:
    //      the view seat at (2.5, 1.5) and the passenger seat at (1.5, 1.5).
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsCrewVehicleViewTestHull
  components:
  - type: Physics
    bodyType: KinematicController
  - type: Vehicle
    requiresHands: false
    transferDamage: false
  - type: KsCrewVehicleInterior
    interiorPath: /Maps/Test/_KS14/CrewVehicle/view_seat.yml
    interiorAnchor: 2.5,3
    enterDelay: 0.5
    exitDelay: 0.5
    maxOccupants: 4
    doorways:
      left:
        offset: 1,0
      right:
        offset: -1,0

- type: entity
  id: KsCrewVehicleViewTestHullNarrowSeat
  components:
  - type: KsCrewVehicleInterior
    interiorPath: /Maps/Test/_KS14/CrewVehicle/view_seat_bad.yml
    interiorAnchor: 2.5,3
    doorways:
      left:
        offset: 1,0
      right:
        offset: -1,0

- type: entity
  id: KsCrewVehicleViewTestMob
  components:
  - type: DoAfter
  - type: Physics
    bodyType: KinematicController
  - type: MobState
  - type: Eye
  - type: ContentEye
  - type: Buckle

- type: entity
  id: KsCrewVehicleViewTestMarker
";

    /// <summary>Long enough for the PVS sweep and the state carrying its result to reach the client.</summary>
    protected const int SettleTicks = 30;

    /// <summary>Ten times the PVS range: out of reach at any scale the tests use.</summary>
    protected const float FarDistance = 10f;

    /// <param name="LowerMapId">The z-level under the outside map, if one was asked for.</param>
    protected sealed record VehicleScene(MapId OutsideMapId, EntityUid Hull, EntityUid InteriorGrid, Vector2 Left, float PvsRange,
        MapId LowerMapId = default);

    /// <summary>
    ///     Turns PVS on, parks a test hull on floor that reaches round it, and loads its interior.
    /// </summary>
    /// <param name="levelBelow">Makes the outside map a z-level with another under it.</param>
    protected async Task<VehicleScene> SetUpVehicleAsync(EntProtoId? hullPrototype = null, bool levelBelow = false)
    {
        await OverrideCVar(Side.Server, CVars.NetPVS, true);
        Assert.That(ServerSession, Is.Not.Null, "these tests need a connected session for anything to replicate to");

        var testMap = await Pair.CreateTestMap();
        EntityUid hull = default, interiorGrid = default;
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

            hull = SEntMan.Spawn(hullPrototype ?? HullPrototype, new MapCoordinates(hullPosition, testMap.MapId));
            Assert.That(Interiors.EnsureInterior(Vehicle(hull), out var interiorMap), Is.True);
            interiorGrid = SEntMan.AllEntities<MapGridComponent>()
                .Single(grid => SComp<TransformComponent>(grid).MapUid == interiorMap)
                .Owner;
        });

        var lowerMapId = MapId.Nullspace;
        if (levelBelow)
        {
            await Server.WaitPost(() =>
            {
                var lowerMapUid = SEntMan.System<SharedMapSystem>().CreateMap(out lowerMapId);
                SEntMan.EnsureComponent<KsZLevelComponent>(testMap.MapUid);
                SEntMan.EnsureComponent<KsZLevelComponent>(lowerMapUid);
                SEntMan.System<KsZLevelSystem>().AddZLevelDirectlyUnder(testMap.MapUid, lowerMapUid);
            });
        }

        var pvsRange = Server.ResolveDependency<IConfigurationManager>().GetCVar(CVars.NetMaxUpdateRange);
        return new VehicleScene(testMap.MapId, hull, interiorGrid, hullPosition + new Vector2(1f, 0f), pvsRange, lowerMapId);
    }

    protected KsCrewVehicleInteriorSystem Interiors => SEntMan.System<KsCrewVehicleInteriorSystem>();

    protected Entity<KsCrewVehicleInteriorComponent> Vehicle(EntityUid hull) => SEntity<KsCrewVehicleInteriorComponent>(hull);

    protected int TicksFor(TimeSpan delay)
    {
        return (int)Math.Ceiling(delay.TotalSeconds * Server.Timing.TickRate) + 5;
    }

    /// <summary>
    ///     Spawns the player's mob outside at the left doorway and gets in through it, as a player would.
    /// </summary>
    protected async Task<EntityUid> EnterAsPlayerAsync(VehicleScene scene)
    {
        EntityUid mob = default;
        await Server.WaitPost(() =>
        {
            mob = SEntMan.Spawn(MobPrototype, new MapCoordinates(scene.Left, scene.OutsideMapId));
            Server.PlayerMan.SetAttachedEntity(ServerSession!, mob);
            Assert.That(SEntMan.System<KsCrewVehicleEntrySystem>().TryStartEnter(Vehicle(scene.Hull), mob), Is.True);
        });
        await Pair.RunTicksSync(TicksFor(SComp<KsCrewVehicleInteriorComponent>(scene.Hull).EnterDelay));
        await Pair.RunTicksSync(SettleTicks);

        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.HasComponent<KsCrewVehicleOccupantComponent>(mob), Is.True, "precondition: they got in"));
        return mob;
    }

    /// <summary>
    ///     Spawns a marker on the vehicle's map, <paramref name="distance"/> east of the hull.
    /// </summary>
    protected async Task<NetEntity> SpawnMarkerAsync(VehicleScene scene, float distance)
    {
        var netEntity = NetEntity.Invalid;
        await Server.WaitPost(() =>
        {
            var hullPosition = SEntMan.System<SharedTransformSystem>().GetWorldPosition(scene.Hull);
            var markerUid = SEntMan.Spawn(_markerPrototype,
                new MapCoordinates(hullPosition + new Vector2(distance, 0f), scene.OutsideMapId));
            netEntity = SEntMan.GetNetEntity(markerUid);
        });
        return netEntity;
    }

    /// <summary>Whether the client holds the entity and it is in view, not left detached by an earlier send.</summary>
    protected async Task<bool> ClientHasAsync(NetEntity netEntity)
    {
        var has = false;
        await Client.WaitPost(() =>
            has = CEntMan.TryGetEntity(netEntity, out var clientUid)
                  && (CEntMan.GetComponent<MetaDataComponent>(clientUid.Value).Flags & MetaDataFlags.Detached) == 0);
        return has;
    }

    /// <summary>The outside viewers the player's session is subscribed to. Call on the server thread.</summary>
    protected List<EntityUid> SubscribedViewers()
    {
        return ServerSession!.ViewSubscriptions
            .Where(uid => SEntMan.GetComponentOrNull<MetaDataComponent>(uid)?.EntityPrototype?.ID == ViewerPrototype.Id)
            .ToList();
    }
}
