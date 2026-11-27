#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server.Atmos.Components;
using Content.Server._KS14.CrewVehicle.Entry;
using Content.Shared._KS14.CrewVehicle;
using Content.Shared._KS14.CrewVehicle.Interior;
using Content.Shared._KS14.CrewVehicle.Ride;
using Content.Shared._KS14.CrewVehicle.RunOver;
using Content.Shared._KS14.Throwing;
using Content.Shared.Climbing.Components;
using Content.Shared.Climbing.Systems;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Physics;
using Content.Shared.Verbs;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using NUnit.Framework;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.CrewVehicle;

/// <summary>
///     Riding on a hull: climbing on, staying on through driving and turns, getting off by the edge, the verb or a crash,
///         and what riders are spared (being run over, the hull's cover against shots aimed at them) and refused (getting
///         in from the roof, more riders than the hull takes). Every number comes from the test hulls' prototypes.
/// </summary>
[TestOf(typeof(KsCrewVehicleRideSystem))]
public sealed class KsCrewVehicleRideTest : KsCrewVehicleDriveTestBase
{
    private readonly EntProtoId _rideHull = "KsCrewVehicleRideTestHull";
    private readonly EntProtoId _calmRideHull = "KsCrewVehicleRideTestHullCalm";
    private readonly EntProtoId _occludedRideHull = "KsCrewVehicleRideTestHullOccluded";
    private readonly EntProtoId _table = "KsCrewVehicleRideTestTable";
    private readonly EntProtoId _post = "KsCrewVehicleRideTestPost";
    private readonly EntProtoId _laser = "KsCrewVehicleRideTestLaser";
    private readonly EntProtoId _pistol = "KsCrewVehicleRideTestPistol";
    private static readonly EntProtoId Humvee = "KsCrewVehicleHumvee";
    private static readonly LocId ClimbDownVerbId = "ks-crew-vehicle-ride-verb-climb-down";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: KsCrewVehicleDriveTestHull
  id: KsCrewVehicleRideTestHull
  components:
  - type: Sprite
    drawdepth: Mobs
  - type: Climbable
    range: 2
    delay: 0.5
  - type: KsCrewVehicleRideable
    maxRiders: 2

# Riders stay on through any crash it can have.
- type: entity
  parent: KsCrewVehicleRideTestHull
  id: KsCrewVehicleRideTestHullCalm
  components:
  - type: KsCrewVehicleRideable
    maxRiders: 2
    throwOffSpeed: 100

- type: entity
  parent: KsCrewVehicleRideTestHull
  id: KsCrewVehicleRideTestHullOccluded
  components:
  - type: PointLight
    enabled: false
    offset: 0,-0.9
  - type: KsCrewVehicleHullOccluder
    polygon:
    - -0.45,-0.75
    - 0.45,-0.75
    - 0.45,0.75
    - -0.45,0.75

# Climbable, and nothing to ride.
- type: entity
  id: KsCrewVehicleRideTestTable
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
          bounds: ""-0.45,-0.45,0.45,0.45""
        mask:
        - TableMask
        layer:
        - TableLayer
  - type: Climbable

# Something small and solid in the way of climbing down.
- type: entity
  id: KsCrewVehicleRideTestPost
  components:
  - type: Physics
    bodyType: Static
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeAabb
          bounds: ""-0.2,-0.2,0.2,0.2""
        mask:
        - FullTileMask
        layer:
        - WallLayer

- type: entity
  id: KsCrewVehicleRideTestRay
  components:
  - type: HitscanAmmo
  - type: HitscanBasicRaycast
  - type: HitscanBasicDamage
    damage:
      types:
        Piercing: 10

- type: entity
  id: KsCrewVehicleRideTestLaser
  components:
  - type: Gun
    fireRate: 100
    minAngle: 0
    maxAngle: 0
    selectedMode: SemiAuto
    availableModes:
    - SemiAuto
  - type: BasicEntityAmmoProvider
    proto: KsCrewVehicleRideTestRay
    capacity: 10
    count: 10

- type: entity
  parent: BaseBullet
  id: KsCrewVehicleRideTestBullet
  components:
  - type: Projectile
    damage:
      types:
        Piercing: 10

- type: entity
  id: KsCrewVehicleRideTestPistol
  components:
  - type: Gun
    fireRate: 100
    minAngle: 0
    maxAngle: 0
    selectedMode: SemiAuto
    availableModes:
    - SemiAuto
  - type: BasicEntityAmmoProvider
    proto: KsCrewVehicleRideTestBullet
    capacity: 10
    count: 10
";

    /// <summary>
    ///     The player starts at the origin, where the hulls are parked: out of the way, until a test puts them somewhere.
    /// </summary>
    [SetUp]
    public async Task KsMovePlayerAside()
    {
        await Server.WaitPost(() => Transform.SetCoordinates(SPlayer, OriginCoordinates.Offset(new Vector2(-8f, -8f))));
        await RunTicks(2);
    }

    private KsCrewVehicleRideSystem RideSystem => SEntMan.System<KsCrewVehicleRideSystem>();

    private bool IsRider(EntityUid mob, EntityUid hull) => RideSystem.IsRiderOf(mob, hull);

    private bool IsClimbing(EntityUid mob) => SComp<ClimbingComponent>(mob).IsClimbing;

    private FixedPoint2 Damage(EntityUid uid) => SEntMan.System<DamageableSystem>().GetTotalDamage(uid);

    private Vector2 Facing(EntityUid hull) => KsCrewVehicleHullFrame.Forward(HullRotation(hull));

    /// <summary>
    ///     A human at <paramref name="offset"/> from the origin, hurt by nothing but what the test does.
    /// </summary>
    private async Task<EntityUid> SpawnMobAsync(Vector2 offset)
    {
        EntityUid mob = default;
        await Server.WaitPost(() =>
        {
            mob = SEntMan.SpawnAtPosition(Human, OriginCoordinates.Offset(offset));
            SEntMan.RemoveComponent<BarotraumaComponent>(mob);
        });
        await RunTicks(5);
        return mob;
    }

    /// <summary>
    ///     Climbs <paramref name="mob"/> onto <paramref name="climbable"/> the stock way, a do-after, and waits until it
    ///         has slid into place.
    /// </summary>
    private async Task ClimbAsync(EntityUid mob, EntityUid climbable)
    {
        var delay = 0f;
        await Server.WaitAssertion(() =>
        {
            delay = SComp<ClimbableComponent>(climbable).ClimbDelay;
            Assert.That(SEntMan.System<ClimbSystem>().TryClimb(mob, mob, climbable, out _), Is.True, "precondition: the climb starts");
        });

        var done = false;
        for (var tick = 0; tick < (int)((delay + 2f) * TickRate) && !done; tick++)
        {
            await RunTicks(1);
            await Server.WaitPost(() =>
            {
                var climbing = SComp<ClimbingComponent>(mob);
                done = climbing.IsClimbing && climbing.NextTransition == null;
            });
        }

        Assert.That(done, Is.True, "precondition: climbed on");
        await RunTicks(2);
    }

    private async Task<EntityUid> MountedRiderAsync(EntityUid hull, Vector2 offset)
    {
        var mob = await SpawnMobAsync(offset);
        await ClimbAsync(mob, hull);
        await Server.WaitAssertion(() => Assert.That(IsRider(mob, hull), Is.True, "precondition: riding"));
        return mob;
    }

    /// <summary>
    ///     Whether any hard fixture of <paramref name="mob"/> touches the hull's body.
    /// </summary>
    private bool HardTouching(EntityUid mob, EntityUid hull)
    {
        var contacts = SEntMan.System<SharedPhysicsSystem>().GetContacts(mob);
        while (contacts.MoveNext(out var contact))
        {
            if (contact.IsTouching && contact.Hard && contact.OtherEnt(mob) == hull)
                return true;
        }

        return false;
    }

    private void SetKey(EntityUid mob, Direction direction, bool down)
    {
        var mover = new Entity<InputMoverComponent>(mob, SComp<InputMoverComponent>(mob));
        SEntMan.System<SharedMoverController>().SetVelocityDirection(mover, direction, subTick: 0, enabled: down);
    }

    /// <summary>
    ///     Climbs onto a parked hull: parented to it, climbing, marked, inside the deck by its inset, and not colliding
    ///         with it. Control: a table is climbed as ever, and nobody rides it.
    /// </summary>
    [Test]
    public async Task ClimbsOntoAParkedHull()
    {
        var scene = await SpawnHullAsync(Vector2.Zero, _rideHull);
        var hull = scene.Hull;
        var mob = await SpawnMobAsync(new Vector2(1.3f, 0.3f));
        await ClimbAsync(mob, hull);
        await RunTicks(TickRate / 2);

        await Server.WaitAssertion(() =>
        {
            var rideable = SComp<KsCrewVehicleRideableComponent>(hull);
            Assert.That(RideSystem.TryGetDeck((hull, rideable), out var deck), Is.True);
            var local = SComp<TransformComponent>(mob).LocalPosition;
            Assert.That(SComp<TransformComponent>(mob).ParentUid, Is.EqualTo(hull), "the rider is parented to the hull");
            Assert.That(IsRider(mob, hull), Is.True, "and marked as its rider");
            Assert.That(IsClimbing(mob), Is.True, "climbing on it");
            Assert.That(MathF.Abs(local.X), Is.LessThanOrEqualTo(deck.Right - rideable.DeckInset + 0.01f), "inside the deck by its inset");
            Assert.That(MathF.Abs(local.Y), Is.LessThanOrEqualTo(deck.Top - rideable.DeckInset + 0.01f), "inside the deck by its inset");
            Assert.That(local.X, Is.GreaterThan(0f), "on the side it climbed from, not the middle");
            Assert.That(HardTouching(mob, hull), Is.False, "never colliding with the hull it stands on");
            Assert.That(SComp<PhysicsComponent>(mob).SleepingAllowed, Is.False, "kept awake while it rides");
        });

        // Control: a table.
        EntityUid table = default;
        await Server.WaitPost(() => table = SEntMan.SpawnAtPosition(_table, OriginCoordinates.Offset(new Vector2(-4f, 0f))));
        var climber = await SpawnMobAsync(new Vector2(-5.2f, 0f));
        await ClimbAsync(climber, table);
        await Server.WaitAssertion(() =>
        {
            Assert.That(IsClimbing(climber), Is.True, "control: the table is climbed");
            Assert.That(SComp<TransformComponent>(climber).ParentUid, Is.Not.EqualTo(table), "and not ridden");
            Assert.That(SEntMan.HasComponent<KsCrewVehicleRiderComponent>(climber), Is.False);
        });
    }

    /// <summary>
    ///     No more riders than the hull takes, by the climb's attempt and by a climb that skips it (a shove onto it).
    /// </summary>
    [Test]
    public async Task NoMoreRidersThanItTakes()
    {
        var scene = await SpawnHullAsync(Vector2.Zero, _rideHull);
        var hull = scene.Hull;
        var max = 0;
        await Server.WaitPost(() => max = SComp<KsCrewVehicleRideableComponent>(hull).MaxRiders);
        for (var i = 0; i < max; i++)
        {
            await MountedRiderAsync(hull, new Vector2(1.3f, -0.6f + i * 0.6f));
        }

        var extra = await SpawnMobAsync(new Vector2(-1.3f, 0f));
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<ClimbSystem>().TryClimb(extra, extra, hull, out _), Is.False, "one too many is refused");
            SEntMan.System<ClimbSystem>().Climb(extra, extra, hull);
        });
        await RunTicks(TickRate);
        await Server.WaitAssertion(() =>
        {
            Assert.That(IsRider(extra, hull), Is.False, "a climb that skips the attempt is refused too");
            Assert.That(IsClimbing(extra), Is.False);
        });
    }

    /// <summary>
    ///     Driven straight and turned on the spot, a rider standing still stays where it is on the deck: on the server and
    ///         on the driver's lagging client, which predicts the hull. One stands at the front, where a driven hull would
    ///         carry or run over a mob on the ground.
    /// </summary>
    [Test]
    public async Task RidesThroughDrivingAndTurns()
    {
        var scene = await SpawnHullAsync(Vector2.Zero, _rideHull);
        var hull = scene.Hull;
        var facing = Vector2.Zero;
        await Server.WaitPost(() => facing = Facing(hull));
        var front = await MountedRiderAsync(hull, facing * 1.6f);
        var side = await MountedRiderAsync(hull, new Vector2(1.3f, 0f));

        await PutInsideAsync(scene);
        await SitAsync(scene.DriverSeat);
        await SimulateLatencyAsync(3);
        await RunTicks(5);

        var riders = new[] { front, side };
        var startLocal = new Dictionary<EntityUid, Vector2>();
        var startDamage = new Dictionary<EntityUid, FixedPoint2>();
        var startHull = Vector2.Zero;
        await Server.WaitPost(() =>
        {
            startHull = HullPosition(hull);
            foreach (var rider in riders)
            {
                startLocal[rider] = SComp<TransformComponent>(rider).LocalPosition;
                startDamage[rider] = Damage(rider);
            }
        });

        var worstServer = 0f;
        var worstClient = 0f;
        var lost = new List<string>();

        async Task Check()
        {
            await Server.WaitPost(() =>
            {
                foreach (var rider in riders)
                {
                    if (!IsRider(rider, hull) || SComp<TransformComponent>(rider).ParentUid != hull)
                        lost.Add($"server: {rider} got off");
                    worstServer = MathF.Max(worstServer, (SComp<TransformComponent>(rider).LocalPosition - startLocal[rider]).Length());
                }
            });
            await Client.WaitPost(() =>
            {
                var clientHull = CEntMan.GetEntity(SEntMan.GetNetEntity(hull));
                foreach (var rider in riders)
                {
                    var clientRider = CEntMan.GetEntity(SEntMan.GetNetEntity(rider));
                    var transform = CEntMan.GetComponent<TransformComponent>(clientRider);
                    if (transform.ParentUid != clientHull)
                        lost.Add($"driver's client: {rider} not on the hull");
                    worstClient = MathF.Max(worstClient, (transform.LocalPosition - startLocal[rider]).Length());
                }
            });
        }

        await HoldAsync(hull, DirectionFlag.North, TickRate * 2);
        await Check();
        for (var tick = 0; tick < TickRate; tick++)
        {
            await RunTicks(1);
            await Check();
        }

        // Turn on the spot (tracked), then drive on.
        await SetMovementKey(DirectionFlag.East, Robust.Shared.Input.BoundKeyState.Down);
        for (var tick = 0; tick < TickRate; tick++)
        {
            await RunTicks(1);
            await Check();
        }

        await SetMovementKey(DirectionFlag.East, Robust.Shared.Input.BoundKeyState.Up);
        await HoldAsync(hull, DirectionFlag.North, TickRate);
        await SettleAsync(hull);
        await Check();

        await Server.WaitAssertion(() =>
        {
            Assert.That((HullPosition(hull) - startHull).Length(), Is.GreaterThan(2f), "precondition: the hull was driven");
            Assert.That(lost, Is.Empty, "every rider stays on");
            Assert.That(worstServer, Is.LessThan(0.05f), "standing still on the deck, on the server");
            Assert.That(worstClient, Is.LessThan(0.05f), "and on the driver's client");
            foreach (var rider in riders)
            {
                Assert.That(Damage(rider), Is.EqualTo(startDamage[rider]), "never run over");
                Assert.That(SEntMan.HasComponent<KsCrewVehicleUnderHullComponent>(rider), Is.False, "nor driven over");
            }
        });
    }

    /// <summary>
    ///     A rider whose contacts with the hull start again while it is driven fast (a grid change does it; here its
    ///         collision is switched off and on) isn't run over: it is up on the deck. Control: a mob on the ground ahead is.
    /// </summary>
    [Test]
    public async Task RidersAreNotRunOverWhenTheirContactsStartAgain()
    {
        var scene = await SpawnHullAsync(Vector2.Zero, _rideHull);
        var hull = scene.Hull;
        var facing = Vector2.Zero;
        await Server.WaitPost(() => facing = Facing(hull));
        var rider = await MountedRiderAsync(hull, facing * 1.6f);
        var ahead = await SpawnMobAsync(facing * 5f);
        await PutInsideAsync(scene);
        await SitAsync(scene.DriverSeat);

        var riderDamage = FixedPoint2.Zero;
        var aheadDamage = FixedPoint2.Zero;
        var minSpeed = 0f;
        await Server.WaitPost(() =>
        {
            riderDamage = Damage(rider);
            aheadDamage = Damage(ahead);
            minSpeed = SComp<KsCrewVehicleRunOverComponent>(hull).MinSpeed;
        });

        var toggled = false;
        await HoldAsync(hull, DirectionFlag.North, TickRate * 3, () =>
        {
            if (!toggled && HullVelocity(hull).Length() > minSpeed + 0.2f)
            {
                var physicsSystem = SEntMan.System<SharedPhysicsSystem>();
                var body = SComp<PhysicsComponent>(rider);
                physicsSystem.SetCanCollide(rider, false, body: body);
                physicsSystem.SetCanCollide(rider, true, body: body);
                toggled = true;
            }

            return toggled && Damage(ahead) > aheadDamage;
        });
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            Assert.That(toggled, Is.True, "precondition: the hull got fast enough to run someone over");
            Assert.That(Damage(ahead), Is.GreaterThan(aheadDamage), "control: the mob on the ground ahead is run over");
            Assert.That(Damage(rider), Is.EqualTo(riderDamage), "the rider isn't");
            Assert.That(SEntMan.HasComponent<KsCrewVehicleUnderHullComponent>(rider), Is.False, "nor driven over");
            Assert.That(IsRider(rider, hull), Is.True, "and still rides");
        });
    }

    /// <summary>
    ///     Walking about the deck, then off its edge: no longer climbing or riding, on the floor's grid, colliding as before,
    ///         and allowed to sleep again.
    /// </summary>
    [Test]
    public async Task WalkingOffTheEdgeGetsOff()
    {
        var scene = await SpawnHullAsync(Vector2.Zero, _rideHull);
        var hull = scene.Hull;
        var rider = await MountedRiderAsync(hull, new Vector2(-1.3f, 0f));
        var startX = 0f;
        var maskBefore = 0;
        await Server.WaitPost(() =>
        {
            startX = SComp<TransformComponent>(rider).LocalPosition.X;
            SetKey(rider, Direction.East, down: true);
        });

        var walkedOnDeck = false;
        var off = false;
        for (var tick = 0; tick < TickRate * 4 && !off; tick++)
        {
            await RunTicks(1);
            await Server.WaitPost(() =>
            {
                off = !IsRider(rider, hull);
                if (!off && SComp<TransformComponent>(rider).LocalPosition.X > startX + 0.2f)
                    walkedOnDeck = true;
            });
        }

        await Server.WaitPost(() => SetKey(rider, Direction.East, down: false));
        await RunTicks(2);
        await Server.WaitAssertion(() =>
        {
            Assert.That(walkedOnDeck, Is.True, "the rider walks about the deck");
            Assert.That(off, Is.True, "and gets off by walking off its edge");
            var transform = SComp<TransformComponent>(rider);
            Assert.That(transform.ParentUid, Is.EqualTo(transform.GridUid), "onto the floor's grid");
            Assert.That(IsClimbing(rider), Is.False, "no longer climbing");
            Assert.That(SComp<PhysicsComponent>(rider).SleepingAllowed, Is.True, "allowed to sleep again");
            maskBefore = SComp<FixturesComponent>(rider).Fixtures.Values.Where(fixture => fixture.Hard).Aggregate(0, (mask, fixture) => mask | fixture.CollisionMask);
            Assert.That(maskBefore & (int)CollisionGroup.MidImpassable, Is.Not.Zero, "colliding with tables and hulls again");
            Assert.That(HardBox(rider).Intersects(HardBox(hull)), Is.False, "beside the hull, not in it");
        });
    }

    /// <summary>
    ///     Climbing down by the verb lands just outside the deck on the near side; with that blocked, another side; with all
    ///         four blocked, nowhere, and the rider stays on.
    /// </summary>
    [Test]
    public async Task ClimbDownVerb()
    {
        var scene = await SpawnHullAsync(Vector2.Zero, _rideHull);
        var hull = scene.Hull;
        var rider = await MountedRiderAsync(hull, new Vector2(1.3f, 0f));

        var posts = new List<EntityUid>();
        await Server.WaitAssertion(() =>
        {
            var riderEntity = SEntity<KsCrewVehicleRiderComponent>(rider);
            var hullBox = HardBox(hull);
            var first = RideSystem.TryGetClimbDownSpot(riderEntity);
            Assert.That(first, Is.Not.Null, "there is somewhere to climb down");
            Assert.That(first!.Value.Position.X, Is.GreaterThan(hullBox.Right), "on the side the rider stands nearest");

            MapCoordinates? previous = first;
            for (var i = 0; i < 4 && previous is { } spot; i++)
            {
                posts.Add(SEntMan.SpawnEntity(_post, spot));
                var next = RideSystem.TryGetClimbDownSpot(riderEntity);
                if (next is { } nextSpot)
                    Assert.That((nextSpot.Position - spot.Position).Length(), Is.GreaterThan(0.5f), "a blocked side is passed over");
                previous = next;
            }

            Assert.That(previous, Is.Null, "with every side blocked, nowhere");
            Assert.That(posts, Has.Count.EqualTo(4), "after all four sides");
            Assert.That(RideSystem.TryClimbDown(riderEntity), Is.False, "so climbing down is refused");
            Assert.That(IsRider(rider, hull), Is.True, "and the rider stays on");
        });

        await Server.WaitPost(() =>
        {
            foreach (var post in posts)
            {
                SEntMan.DeleteEntity(post);
            }
        });
        await RunTicks(2);

        await Server.WaitAssertion(() =>
        {
            var verbs = SEntMan.System<SharedVerbSystem>().GetLocalVerbs(hull, rider, typeof(AlternativeVerb));
            var verb = verbs.SingleOrDefault(verb => verb.Text == Server.ResolveDependency<Robust.Shared.Localization.ILocalizationManager>().GetString(ClimbDownVerbId));
            Assert.That(verb, Is.Not.Null, "a rider has the verb");
            SEntMan.System<SharedVerbSystem>().ExecuteVerb(verb!, rider, hull, forced: true);
        });
        await RunTicks(2);
        await Server.WaitAssertion(() =>
        {
            Assert.That(IsRider(rider, hull), Is.False, "climbed down");
            Assert.That(IsClimbing(rider), Is.False);
            var transform = SComp<TransformComponent>(rider);
            Assert.That(transform.ParentUid, Is.EqualTo(transform.GridUid), "onto the floor");
            Assert.That(HardBox(rider).Intersects(HardBox(hull)), Is.False, "clear of the hull");
            Assert.That(Transform.GetWorldPosition(rider).X, Is.GreaterThan(HardBox(hull).Right), "on the near side");

            var verbs = SEntMan.System<SharedVerbSystem>().GetLocalVerbs(hull, rider, typeof(AlternativeVerb));
            Assert.That(verbs.Any(verb => verb.Text == Server.ResolveDependency<Robust.Shared.Localization.ILocalizationManager>().GetString(ClimbDownVerbId)), Is.False, "nobody else has the verb");
        });
    }

    public enum CrashCase
    {
        Thrown,
        CalmHull,
        Unthrowable,
    }

    /// <summary>
    ///     A hard crash throws riders off along the hull's way, knocked down and marked to slam into what they hit. Controls:
    ///         a crash below the hull's throw-off speed leaves them on; a rider that can't be thrown is only got off.
    /// </summary>
    [TestCase(CrashCase.Thrown)]
    [TestCase(CrashCase.CalmHull)]
    [TestCase(CrashCase.Unthrowable)]
    public async Task HardCrashThrowsRidersOff(CrashCase crashCase)
    {
        var scene = await SpawnHullAsync(Vector2.Zero, crashCase == CrashCase.CalmHull ? _calmRideHull : _rideHull);
        var hull = scene.Hull;
        var facing = Vector2.Zero;
        await Server.WaitPost(() =>
        {
            facing = Facing(hull);
            SEntMan.SpawnAtPosition(TestWallPrototype, OriginCoordinates.Offset(facing * 5f));
        });
        var rider = await MountedRiderAsync(hull, new Vector2(1.3f, 0f));
        if (crashCase == CrashCase.Unthrowable)
            await Server.WaitPost(() => SEntMan.RemoveComponent<Content.Server._KS14.Throwing.KsThrowableMobComponent>(rider));

        await PutInsideAsync(scene);
        await SitAsync(scene.DriverSeat);

        var crashed = false;
        var thrown = false;
        var fastestAlong = 0f;
        var gotOff = false;
        await HoldAsync(hull, DirectionFlag.North, TickRate * 4, () =>
        {
            crashed |= SEntMan.HasComponent<Content.Shared._KS14.CrewVehicle.CrashLockout.KsCrewVehicleCrashLockedComponent>(hull);
            thrown |= SEntMan.HasComponent<KsThrownSlamComponent>(rider);
            gotOff |= !IsRider(rider, hull);
            if (gotOff)
                fastestAlong = MathF.Max(fastestAlong, Vector2.Dot(SEntMan.System<SharedPhysicsSystem>().GetMapLinearVelocity(rider), facing));
            return crashed && (gotOff || crashCase == CrashCase.CalmHull);
        });
        for (var tick = 0; tick < 5; tick++)
        {
            await RunTicks(1);
            await Server.WaitPost(() =>
            {
                thrown |= SEntMan.HasComponent<KsThrownSlamComponent>(rider);
                gotOff |= !IsRider(rider, hull);
                if (gotOff)
                    fastestAlong = MathF.Max(fastestAlong, Vector2.Dot(SEntMan.System<SharedPhysicsSystem>().GetMapLinearVelocity(rider), facing));
            });
        }

        await Server.WaitAssertion(() =>
        {
            Assert.That(crashed, Is.True, "precondition: the hull crashed into the wall");
            switch (crashCase)
            {
                case CrashCase.Thrown:
                    Assert.That(gotOff, Is.True, "the crash throws the rider off");
                    Assert.That(thrown, Is.True, "thrown, marked to slam into what it hits");
                    Assert.That(fastestAlong, Is.GreaterThan(0.5f), "along the hull's way");
                    Assert.That(SEntMan.System<Content.Shared.Standing.StandingStateSystem>().IsDown(rider), Is.True, "knocked down");
                    break;
                case CrashCase.CalmHull:
                    Assert.That(gotOff, Is.False, "control: below the throw-off speed the rider stays on");
                    break;
                case CrashCase.Unthrowable:
                    Assert.That(gotOff, Is.True, "a rider that can't be thrown still gets off");
                    Assert.That(thrown, Is.False, "and isn't thrown");
                    break;
            }
        });
    }

    /// <summary>
    ///     The hull deleted from under its riders leaves them where they were, on the floor and no longer climbing.
    /// </summary>
    [Test]
    public async Task DeletedHullLeavesItsRidersBehind()
    {
        var scene = await SpawnHullAsync(Vector2.Zero, _rideHull);
        var hull = scene.Hull;
        var rider = await MountedRiderAsync(hull, new Vector2(1.3f, 0f));
        var world = Vector2.Zero;
        await Server.WaitPost(() =>
        {
            world = Transform.GetWorldPosition(rider);
            SEntMan.DeleteEntity(hull);
        });
        await RunTicks(2);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.Deleted(rider), Is.False, "the rider outlives the hull");
            var transform = SComp<TransformComponent>(rider);
            Assert.That(transform.ParentUid, Is.EqualTo(transform.GridUid), "on the floor");
            Assert.That((Transform.GetWorldPosition(rider) - world).Length(), Is.LessThan(0.1f), "where it stood");
            Assert.That(IsClimbing(rider), Is.False, "no longer climbing");
            Assert.That(SEntMan.HasComponent<KsCrewVehicleRiderComponent>(rider), Is.False);
        });
    }

    /// <summary>
    ///     A rider standing over a doorway can't get in from the roof, and isn't an occupant. Control: off the hull, at the
    ///         same doorway, it can.
    /// </summary>
    [Test]
    public async Task RidersCantGetInFromTheRoof()
    {
        var scene = await SpawnHullAsync(Vector2.Zero, _rideHull);
        var hull = scene.Hull;
        var rider = await MountedRiderAsync(hull, new Vector2(1.3f, 0f));
        var doorway = Vector2.Zero;
        await Server.WaitPost(() =>
        {
            var interior = SComp<KsCrewVehicleInteriorComponent>(hull);
            doorway = interior.Doorways.Values.First().Offset;
            // To the deck's edge, over the doorway, still climbing.
            Transform.SetLocalPosition(rider, new Vector2(MathF.Sign(doorway.X) * 0.7f, doorway.Y));
        });
        await RunTicks(3);

        await Server.WaitAssertion(() =>
        {
            Assert.That(IsRider(rider, hull), Is.True, "precondition: still riding at the deck's edge");
            Assert.That(SEntMan.System<KsCrewVehicleEntrySystem>().TryStartEnter(SEntity<KsCrewVehicleInteriorComponent>(hull), rider),
                Is.False, "a rider has to climb down first");
            Assert.That(SEntMan.HasComponent<KsCrewVehicleOccupantComponent>(rider), Is.False, "and isn't an occupant");
            Assert.That(RideSystem.TryClimbDown(SEntity<KsCrewVehicleRiderComponent>(rider)), Is.True, "precondition: climbs down");
        });
        await RunTicks(2);
        await Server.WaitPost(() => Transform.SetCoordinates(rider, new EntityCoordinates(hull, doorway)));
        await RunTicks(2);
        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.System<KsCrewVehicleEntrySystem>().TryStartEnter(SEntity<KsCrewVehicleInteriorComponent>(hull), rider),
                Is.True, "control: off the hull, at the doorway, it can get in"));
    }

    /// <summary>
    ///     A shot aimed at a rider isn't stopped by the hull it rides, a ray or a bullet. Control: one not aimed at it hits
    ///         the hull first.
    /// </summary>
    [Test]
    public async Task AimedShotsReachTheRider()
    {
        var scene = await SpawnHullAsync(Vector2.Zero, _rideHull);
        var hull = scene.Hull;
        var rider = await MountedRiderAsync(hull, new Vector2(-1.3f, 0f));

        async Task<(FixedPoint2 Hull, FixedPoint2 Rider)> ShootAsync(EntProtoId gunPrototype, bool aimed)
        {
            var before = (Damage(hull), Damage(rider));
            await Server.WaitAssertion(() =>
            {
                before = (Damage(hull), Damage(rider));
                var riderWorld = Transform.GetWorldPosition(rider);
                var gunUid = SEntMan.SpawnAtPosition(gunPrototype, OriginCoordinates.Offset(new Vector2(-4f, riderWorld.Y - Transform.GetWorldPosition(hull).Y)));
                var gun = SEntity<GunComponent>(gunUid);
                Assert.That(SEntMan.System<SharedGunSystem>().AttemptShoot(gunUid, gun, SComp<TransformComponent>(rider).Coordinates,
                    aimed ? rider : null), Is.True, "precondition: it fired");
            });
            await RunTicks(TickRate);
            var after = (Hull: FixedPoint2.Zero, Rider: FixedPoint2.Zero);
            await Server.WaitPost(() => after = (Damage(hull) - before.Item1, Damage(rider) - before.Item2));
            return after;
        }

        var unaimed = await ShootAsync(_laser, aimed: false);
        Assert.That(unaimed.Hull, Is.GreaterThan(FixedPoint2.Zero), "control: a ray not aimed at the rider hits the hull");
        Assert.That(unaimed.Rider, Is.EqualTo(FixedPoint2.Zero), "control: which covers the rider");

        var aimedRay = await ShootAsync(_laser, aimed: true);
        Assert.That(aimedRay.Rider, Is.GreaterThan(FixedPoint2.Zero), "a ray aimed at the rider hits it");
        Assert.That(aimedRay.Hull, Is.EqualTo(FixedPoint2.Zero), "through the hull");

        var aimedBullet = await ShootAsync(_pistol, aimed: true);
        Assert.That(aimedBullet.Rider, Is.GreaterThan(FixedPoint2.Zero), "a bullet aimed at the rider hits it");
        Assert.That(aimedBullet.Hull, Is.EqualTo(FixedPoint2.Zero), "through the hull");
    }

    /// <summary>
    ///     A rider's own shot passes the hull it stands on, to hit what is beyond.
    /// </summary>
    [Test]
    public async Task RidersShotsPassTheirOwnHull()
    {
        var scene = await SpawnHullAsync(Vector2.Zero, _rideHull);
        var hull = scene.Hull;
        var rider = await MountedRiderAsync(hull, new Vector2(-1.3f, 0f));
        // Another hull beyond, as something a ray hurts that isn't on the rider's side.
        var target = (await SpawnHullAsync(new Vector2(4f, 0f), _calmRideHull)).Hull;
        var before = (Hull: FixedPoint2.Zero, Target: FixedPoint2.Zero);
        await Server.WaitAssertion(() =>
        {
            before = (Damage(hull), Damage(target));
            var riderCoordinates = SComp<TransformComponent>(rider).Coordinates;
            var gunUid = SEntMan.SpawnAtPosition(_laser, riderCoordinates);
            Assert.That(SEntMan.System<SharedGunSystem>().AttemptShoot(rider, SEntity<GunComponent>(gunUid),
                SComp<TransformComponent>(target).Coordinates), Is.True, "precondition: it fired");
        });
        await RunTicks(TickRate);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Damage(target), Is.GreaterThan(before.Target),
                $"the rider's shot hits what is beyond the hull (hull took {Damage(hull) - before.Hull}, rider {Damage(rider)})");
            Assert.That(Damage(hull), Is.EqualTo(before.Hull), "and not the hull it stands on");
        });
    }

    /// <summary>
    ///     A hull driven off the floor's edge drifts with its riders on it; they can still walk about the deck.
    /// </summary>
    [Test]
    public async Task RidesOffIntoSpace()
    {
        var east = new Vector2(1f, 0f).ToWorldAngle();
        var scene = await SpawnHullAsync(new Vector2(FloorRadius - 4f, 0f), _rideHull);
        var hull = scene.Hull;
        await Server.WaitPost(() => Transform.SetWorldRotation(hull, east));
        await RunTicks(2);
        var rider = await MountedRiderAsync(hull, new Vector2(FloorRadius - 4f, 1.4f));
        await PutInsideAsync(scene);
        await SitAsync(scene.DriverSeat);

        var left = false;
        await HoldAsync(hull, DirectionFlag.North, TickRate * 5, () => left = SComp<TransformComponent>(hull).GridUid == null);
        Assert.That(left, Is.True, "precondition: the hull drove off the edge");

        var start = Vector2.Zero;
        await Server.WaitPost(() =>
        {
            Assert.That(IsRider(rider, hull), Is.True, "the rider drifts off with it");
            start = SComp<TransformComponent>(rider).LocalPosition;
            SetKey(rider, Direction.South, down: true);
        });
        // Not for long: with nothing to slow it, a step carries on, and the deck is narrow.
        await RunTicks(5);
        await Server.WaitAssertion(() =>
        {
            SetKey(rider, Direction.South, down: false);
            Assert.That(SEntMan.System<Content.Shared.Gravity.SharedGravitySystem>().IsWeightless(rider), Is.True,
                "precondition: weightless out there");
            Assert.That((SComp<TransformComponent>(rider).LocalPosition - start).Length(), Is.GreaterThan(0.02f),
                "and still walks about the deck");
            Assert.That(IsRider(rider, hull), Is.True, "still on it");
        });
    }

    /// <summary>
    ///     The player's own rider is drawn over the hull, and its eye isn't blinded by the hull's occluder; both put back
    ///         once it climbs down.
    /// </summary>
    [Test]
    public async Task RiderDrawnOverTheHullAndSeesPastIt()
    {
        var scene = await SpawnHullAsync(Vector2.Zero, _occludedRideHull);
        var hull = scene.Hull;
        await Server.WaitPost(() => Transform.SetCoordinates(SPlayer, OriginCoordinates.Offset(new Vector2(1.3f, 0f))));
        await RunTicks(5);

        var originalDepth = 0;
        await Client.WaitPost(() => originalDepth = CEntMan.GetComponent<SpriteComponent>(CPlayer).DrawDepth);
        await ClimbAsync(SPlayer, hull);
        await RunTicks(5);

        await Client.WaitAssertion(() =>
        {
            var clientHull = CEntMan.GetEntity(SEntMan.GetNetEntity(hull));
            Assert.That(CEntMan.GetComponent<SpriteComponent>(CPlayer).DrawDepth,
                Is.EqualTo(CEntMan.GetComponent<SpriteComponent>(clientHull).DrawDepth + 1), "the rider is drawn over the hull");
            Assert.That(CEntMan.GetComponent<Robust.Shared.GameObjects.OccluderComponent>(clientHull).Enabled, Is.False,
                "and the hull doesn't blind its own rider");
        });

        await Server.WaitAssertion(() => Assert.That(RideSystem.TryClimbDown(SEntity<KsCrewVehicleRiderComponent>(SPlayer)), Is.True));
        await RunTicks(5);
        await Client.WaitAssertion(() =>
        {
            var clientHull = CEntMan.GetEntity(SEntMan.GetNetEntity(hull));
            Assert.That(CEntMan.GetComponent<SpriteComponent>(CPlayer).DrawDepth, Is.EqualTo(originalDepth), "its own depth back once off");
            Assert.That(CEntMan.GetComponent<Robust.Shared.GameObjects.OccluderComponent>(clientHull).Enabled, Is.True,
                "and the hull blocks its sight again");
        });
    }

    /// <summary>
    ///     The player riding a hull someone else drives, on their own lagging client: they stay put on the deck, carried
    ///         with the hull as the server has it, with no lead or lag.
    /// </summary>
    [Test]
    public async Task RiderStaysPutOnTheirOwnLaggingClient()
    {
        var scene = await SpawnHullAsync(Vector2.Zero, _rideHull);
        var hull = scene.Hull;
        var driver = await SpawnMobAsync(new Vector2(-3f, -3f));
        await PutInsideAsync(scene, driver);
        await SitAsync(scene.DriverSeat, driver);

        await Server.WaitPost(() => Transform.SetCoordinates(SPlayer, OriginCoordinates.Offset(new Vector2(1.3f, 0f))));
        await RunTicks(5);
        await ClimbAsync(SPlayer, hull);
        await SimulateLatencyAsync(3);

        var startLocal = Vector2.Zero;
        var startHull = Vector2.Zero;
        await Server.WaitPost(() =>
        {
            startLocal = SComp<TransformComponent>(SPlayer).LocalPosition;
            startHull = HullPosition(hull);
            SetKey(hull, Direction.North, down: true);
        });

        var worst = 0f;
        var offHull = 0;
        for (var tick = 0; tick < TickRate * 2; tick++)
        {
            await RunTicks(1);
            await Client.WaitPost(() =>
            {
                var clientHull = CEntMan.GetEntity(SEntMan.GetNetEntity(hull));
                var transform = CEntMan.GetComponent<TransformComponent>(CPlayer);
                if (transform.ParentUid != clientHull)
                    offHull++;
                worst = MathF.Max(worst, (transform.LocalPosition - startLocal).Length());
            });
        }

        await Server.WaitAssertion(() =>
        {
            SetKey(hull, Direction.North, down: false);
            Assert.That((HullPosition(hull) - startHull).Length(), Is.GreaterThan(1f), "precondition: the hull was driven");
            Assert.That(IsRider(SPlayer, hull), Is.True, "the player rides on");
        });
        Assert.That(offHull, Is.Zero, "on their own client the player is on the hull throughout");
        Assert.That(worst, Is.LessThan(0.05f), "standing still on the deck");
    }

    /// <summary>
    ///     Content: every rideable hull, the humvee among them, takes a real human: no collision with the deck while on,
    ///         and walking off it ends the ride.
    /// </summary>
    [Test]
    public async Task EveryRideableHullTakesAHuman()
    {
        var prototypes = ProtoMan.EnumeratePrototypes<EntityPrototype>()
            // Other tests' bare children of the hull base have no body to stand on.
            .Where(prototype => !prototype.Abstract &&
                                prototype.Components.ContainsKey(SEntMan.ComponentFactory.GetComponentName<KsCrewVehicleRideableComponent>()) &&
                                prototype.Components.ContainsKey(SEntMan.ComponentFactory.GetComponentName<FixturesComponent>()))
            .Select(prototype => new EntProtoId(prototype.ID))
            .Where(id => !id.Id.StartsWith("KsCrewVehicleRideTest", StringComparison.Ordinal))
            .ToList();
        Assert.That(prototypes, Does.Contain(Humvee), "precondition: the humvee is rideable");

        foreach (var prototype in prototypes)
        {
            EntityUid hull = default;
            await Server.WaitPost(() => hull = SEntMan.SpawnAtPosition(prototype, OriginCoordinates));
            await RunTicks(5);
            var halfWidth = 0f;
            await Server.WaitPost(() => halfWidth = HardBox(hull).Width / 2f);
            var rider = await SpawnMobAsync(new Vector2(halfWidth + 0.6f, 0f));
            await ClimbAsync(rider, hull);
            await RunTicks(TickRate / 2);
            await Server.WaitAssertion(() =>
            {
                Assert.That(IsRider(rider, hull), Is.True, $"{prototype}: climbed on and riding");
                Assert.That(HardTouching(rider, hull), Is.False, $"{prototype}: never colliding with the deck");
                SetKey(rider, Direction.East, down: true);
            });

            var off = false;
            for (var tick = 0; tick < TickRate * 4 && !off; tick++)
            {
                await RunTicks(1);
                await Server.WaitPost(() => off = !IsRider(rider, hull));
            }

            await Server.WaitAssertion(() =>
            {
                SetKey(rider, Direction.East, down: false);
                Assert.That(off, Is.True, $"{prototype}: walking off the edge ends the ride");
                Assert.That(IsClimbing(rider), Is.False, $"{prototype}: and the climb");
                SEntMan.DeleteEntity(hull);
                SEntMan.DeleteEntity(rider);
            });
            await RunTicks(2);
        }
    }
}
