#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Tests.Movement;
using Content.Server._KS14.CrewVehicle.Interior;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.CrewVehicle;
using Content.Shared._KS14.CrewVehicle.Drive;
using Content.Shared._KS14.CrewVehicle.Interior;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Maps;
using Content.Shared.Movement.Components;
using NUnit.Framework;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.CrewVehicle;

/// <summary>
///     What the driving tests share: a wide floor, a test hull parked on it with its interior loaded, and the player
///         inside, seated at the wheel or not. The player drives through their real client: keys, relay, prediction.
/// </summary>
public abstract class KsCrewVehicleDriveTestBase : MovementTest
{
    protected readonly EntProtoId HullPrototype = "KsCrewVehicleDriveTestHull";
    protected readonly EntProtoId TwoDriversHullPrototype = "KsCrewVehicleDriveTestHullTwoDrivers";
    protected readonly EntProtoId BlindHullPrototype = "KsCrewVehicleDriveTestHullBlind";
    protected readonly EntProtoId CratePrototype = "KsCrewVehicleDriveTestCrate";
    protected static readonly EntProtoId Human = "MobHuman";

    protected override bool AddWalls => false;
    protected override int Tiles => 1;

    /// <summary>Half the floor's side, in tiles, round the origin.</summary>
    protected const int FloorRadius = 12;

    // Interiors: Maps/Test/_KS14/CrewVehicle/drive.yml is view_seat.yml with its view seat made a driver seat as well,
    //      at (2.5, 1.5), and a passenger seat at (1.5, 1.5).
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsCrewVehicleDriveTestHull
  components:
  - type: Transform
    anchored: false
  - type: Physics
    bodyType: Dynamic
    fixedRotation: true
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeAabb
          bounds: ""-0.5,-0.8,0.5,0.8""
        density: 400
        mask:
        - MobMask
        layer:
        - Impassable
        - MidImpassable
        - HighImpassable
        - BulletImpassable
      contact:
        shape:
          !type:PhysShapeAabb
          bounds: ""-0.75,-1.05,0.75,1.05""
        hard: false
        mask:
        - MobMask
  - type: InputMover
  - type: MovementSpeedModifier
    baseWalkSpeed: 3
    baseSprintSpeed: 3
  - type: NoRotateOnMove
  - type: Vehicle
    requiresHands: false
    transferDamage: false
  - type: KsCrewVehicleDrive
    turnRate: 180
    reverseSpeedFraction: 0.5
    turningSpeedFraction: 0.25
  - type: KsCrewVehicleInterior
    interiorPath: /Maps/Test/_KS14/CrewVehicle/drive.yml
    interiorAnchor: 2.5,3
    doorways:
      left:
        offset: 1,0
      right:
        offset: -1,0
  - type: AmbientSound
    enabled: false
    sound:
      path: /Audio/Effects/Vehicle/vehicleengineidle.ogg
  - type: ActionsContainer
  - type: KsCrewVehicleHorn
    sound:
      path: /Audio/Effects/Vehicle/carhorn.ogg
  - type: UnpoweredFlashlight
  - type: PointLight
    enabled: false
  - type: KsCrewVehicleHeadlight

- type: entity
  parent: KsCrewVehicleDriveTestHull
  id: KsCrewVehicleDriveTestHullTwoDrivers
  components:
  - type: KsCrewVehicleInterior
    interiorPath: /Maps/Test/_KS14/CrewVehicle/drive_two_drivers.yml
    interiorAnchor: 2.5,3
    doorways:
      left:
        offset: 1,0
      right:
        offset: -1,0

- type: entity
  parent: KsCrewVehicleDriveTestHull
  id: KsCrewVehicleDriveTestHullBlind
  components:
  - type: KsCrewVehicleInterior
    interiorPath: /Maps/Test/_KS14/CrewVehicle/drive_blind.yml
    interiorAnchor: 2.5,3
    doorways:
      left:
        offset: 1,0
      right:
        offset: -1,0

- type: entity
  id: KsCrewVehicleDriveTestCrate
  components:
  - type: Physics
    bodyType: Dynamic
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeCircle
          radius: 0.3
        density: 50
";

    protected sealed record HullScene(EntityUid Hull, EntityUid InteriorGrid, EntityUid DriverSeat, EntityUid PassengerSeat);

    protected int TickRate => (int)STiming.TickRate;

    [SetUp]
    public override async Task Setup()
    {
        await base.Setup();

        await Server.WaitPost(() =>
        {
            var tile = new Tile(ProtoMan.Index<ContentTileDefinition>(Plating).TileId);
            for (var x = -FloorRadius; x <= FloorRadius; x++)
            {
                for (var y = -FloorRadius; y <= FloorRadius; y++)
                {
                    MapSystem.SetTile(MapData.Grid, new Vector2i(x, y), tile);
                }
            }
        });
        await RunTicks(1);
    }

    /// <summary>
    ///     Parks a hull at <paramref name="offset"/> from the origin and loads its interior.
    /// </summary>
    protected async Task<HullScene> SpawnHullAsync(Vector2 offset, EntProtoId? prototype = null)
    {
        HullScene scene = default!;
        await Server.WaitPost(() =>
        {
            var hullUid = SEntMan.SpawnAtPosition(prototype ?? HullPrototype, OriginCoordinates.Offset(offset));
            var interiorSystem = SEntMan.System<KsCrewVehicleInteriorSystem>();
            Assert.That(interiorSystem.EnsureInterior((hullUid, SEntMan.GetComponent<KsCrewVehicleInteriorComponent>(hullUid)), out var interiorMap),
                Is.True, "precondition: the interior loads");

            var interiorGrid = SEntMan.AllEntities<MapGridComponent>()
                .Single(grid => SEntMan.GetComponent<TransformComponent>(grid).MapUid == interiorMap).Owner;
            var driverSeat = SEntMan.AllEntities<KsCrewVehicleDriverSeatComponent>()
                .First(seat => SEntMan.GetComponent<TransformComponent>(seat).MapUid == interiorMap).Owner;
            var passengerSeat = SEntMan.AllEntities<StrapComponent>()
                .First(seat => SEntMan.GetComponent<TransformComponent>(seat).MapUid == interiorMap &&
                               !SEntMan.HasComponent<KsCrewVehicleDriverSeatComponent>(seat)).Owner;
            scene = new HullScene(hullUid, interiorGrid, driverSeat, passengerSeat);
        });
        await RunTicks(5);
        return scene;
    }

    protected EntityCoordinates OriginCoordinates => SEntMan.GetCoordinates(PlayerCoords);

    /// <summary>
    ///     Puts <paramref name="mob"/> (the player by default) inside, beside the driver seat, standing.
    /// </summary>
    protected async Task PutInsideAsync(HullScene scene, EntityUid? mob = null)
    {
        var mobUid = mob ?? SPlayer;
        await Server.WaitPost(() =>
            Transform.SetCoordinates(mobUid, SEntMan.GetComponent<TransformComponent>(scene.DriverSeat).Coordinates));
        await RunTicks(5);
    }

    protected async Task SitAsync(EntityUid seat, EntityUid? mob = null)
    {
        var mobUid = mob ?? SPlayer;
        await Server.WaitAssertion(() =>
            Assert.That(Server.System<SharedBuckleSystem>().TryBuckle(mobUid, mobUid, seat, popup: false), Is.True,
                "precondition: sat down"));
        await RunTicks(5);
    }

    protected async Task StandAsync(EntityUid? mob = null)
    {
        var mobUid = mob ?? SPlayer;
        await Server.WaitPost(() => Server.System<SharedBuckleSystem>().Unbuckle((mobUid, null), mobUid));
        await RunTicks(5);
    }

    protected async Task SetTankControlsAsync(bool tankControls)
    {
        await Server.WaitPost(() =>
            Server.ResolveDependency<IConfigurationManager>().SetCVar(KsCCVars.CrewVehicleTankControls, tankControls));
        await RunTicks(5);
    }

    protected T SComp<T>(EntityUid uid) where T : IComponent => SEntMan.GetComponent<T>(uid);

    protected Entity<T> SEntity<T>(EntityUid uid) where T : IComponent => (uid, SEntMan.GetComponent<T>(uid));

    /// <summary>
    ///     The world bounds of what is solid about <paramref name="uid"/>: a hull's larger non-hard contact fixture, or a
    ///         mob's, isn't.
    /// </summary>
    protected Box2 HardBox(EntityUid uid) => SEntMan.System<Robust.Shared.Physics.Systems.SharedPhysicsSystem>().GetHardAABB(uid);

    /// <summary>
    ///     Where <paramref name="uid"/> is on the test grid. Not its world position: the open test grid drifts a little
    ///         (decompression thrust), carrying everything on it.
    /// </summary>
    protected Vector2 HullPosition(EntityUid uid) => SComp<TransformComponent>(uid).LocalPosition;

    protected Angle HullRotation(EntityUid hull) => Transform.GetWorldRotation(hull);

    protected Vector2 HullVelocity(EntityUid hull) => SEntMan.GetComponent<PhysicsComponent>(hull).LinearVelocity;

    protected KsCrewVehicleDriveComponent Drive(EntityUid hull) => SEntMan.GetComponent<KsCrewVehicleDriveComponent>(hull);

    protected float TopSpeed(EntityUid hull) => SEntMan.GetComponent<MovementSpeedModifierComponent>(hull).CurrentSprintSpeed;

    /// <summary>
    ///     How fast the hull moves across its own facing: zero for wheels that grip.
    /// </summary>
    protected float SidewaysSpeed(EntityUid hull)
    {
        var forward = KsCrewVehicleHullFrame.Forward(HullRotation(hull));
        var velocity = HullVelocity(hull);
        return MathF.Abs(forward.X * velocity.Y - forward.Y * velocity.X);
    }

    /// <summary>
    ///     Holds <paramref name="direction"/> for up to <paramref name="maxTicks"/> ticks or until
    ///         <paramref name="until"/> holds, checking each tick that the hull never moves sideways. Returns the ticks held.
    /// </summary>
    protected async Task<int> HoldAsync(EntityUid hull, DirectionFlag direction, int maxTicks, Func<bool>? until = null)
    {
        await SetMovementKey(direction, Robust.Shared.Input.BoundKeyState.Down);
        var ticks = 0;
        var done = false;
        for (; ticks < maxTicks && !done; ticks++)
        {
            await RunTicks(1);
            await Server.WaitAssertion(() =>
            {
                Assert.That(SidewaysSpeed(hull), Is.LessThan(TopSpeed(hull) * 0.01f),
                    $"holding {direction}, the hull should move only along its facing");
                done = until?.Invoke() ?? false;
            });
        }

        await SetMovementKey(direction, Robust.Shared.Input.BoundKeyState.Up);
        return ticks;
    }

    /// <summary>
    ///     Lets the hull roll to a stop, up to a few seconds.
    /// </summary>
    protected async Task SettleAsync(EntityUid hull)
    {
        for (var tick = 0; tick < TickRate * 5; tick++)
        {
            await RunTicks(1);
            var stopped = false;
            await Server.WaitPost(() => stopped = HullVelocity(hull).Length() < 0.01f);
            if (stopped)
                return;
        }
    }

    protected List<string> RefusedInterior(EntProtoId hullPrototype)
    {
        var errors = new List<string>();
        var interiorSystem = SEntMan.System<KsCrewVehicleInteriorSystem>();
        var hullUid = SEntMan.SpawnAtPosition(hullPrototype, OriginCoordinates.Offset(new Vector2(-6f, -6f)));

        // The refusal logs an error, which would fail the test: silence the interior system's log for the call only.
        var log = interiorSystem.Log;
        var level = log.Level;
        log.Level = Robust.Shared.Log.LogLevel.Fatal;
        try
        {
            Assert.That(interiorSystem.EnsureInterior((hullUid, SEntMan.GetComponent<KsCrewVehicleInteriorComponent>(hullUid)), out _, errors),
                Is.False, $"{hullPrototype}'s interior should be refused");
        }
        finally
        {
            log.Level = level;
        }

        return errors;
    }
}
