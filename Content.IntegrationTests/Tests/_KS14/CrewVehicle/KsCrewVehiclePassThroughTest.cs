#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._KS14.CrewVehicle.Interior;
using Content.Server._KS14.CrewVehicle.PassThrough;
using Content.Server._KS14.KillCredit;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.CrewVehicle.Interior;
using Content.Shared._KS14.Projectiles;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Destructible;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Systems;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.UnitTesting;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.CrewVehicle;

/// <summary>
///     A round that penetrates a crew vehicle's hull goes into its interior, at the point and the way that match where it
///         hit, hits whoever is in its way there, and comes out of the far side if it can still go on.
/// </summary>
[TestFixture]
[TestOf(typeof(KsCrewVehiclePassThroughSystem))]
public sealed class KsCrewVehiclePassThroughTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false, Dirty = true };

    private readonly EntProtoId _hull = "KsCrewVehiclePassThroughTestHull";

    /// <summary>
    ///     A cabin outline wider than the interior grid: a round crossing it is off the grid, on the interior map, before
    ///         it reaches the far side, which is not where the grid's off-grid rule puts things out.
    /// </summary>
    private readonly EntProtoId _wideCabinHull = "KsCrewVehiclePassThroughTestHullWideCabin";
    private readonly EntProtoId _mob = "KsCrewVehiclePassThroughTestMob";
    private readonly EntProtoId _wall = "KsCrewVehiclePassThroughTestWall";
    private readonly EntProtoId _shooter = "KsCrewVehiclePassThroughTestShooter";

    /// <summary>
    ///     Has the budget to go through the hull once and part of the way through a second time.
    /// </summary>
    private readonly EntProtoId _entryOnlyRound = "KsCrewVehiclePassThroughTestRoundEntryOnly";

    /// <summary>
    ///     Has the budget to go through the hull twice and more.
    /// </summary>
    private readonly EntProtoId _throughRound = "KsCrewVehiclePassThroughTestRoundThrough";

    /// <summary>
    ///     Penetrates nothing.
    /// </summary>
    private readonly EntProtoId _plainRound = "KsCrewVehiclePassThroughTestRoundPlain";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsCrewVehiclePassThroughTestHull
  components:
  - type: Physics
    bodyType: Static
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeAabb
          bounds: ""-1,-1.5,1,1.5""
        layer:
        - MidImpassable
        - BulletImpassable
  - type: Damageable
    damageModifierSet: Metallic
  - type: Injurable
    damageContainer: StructuralInorganic
  - type: Destructible
    thresholds:
    - trigger:
        !type:DamageTrigger
        damage: 1000
      behaviors:
      - !type:DoActsBehavior
        acts: [ ""Destruction"" ]
  - type: KsCrewVehicleInterior
    interiorPath: /Maps/Test/_KS14/CrewVehicle/basic.yml
    doorways:
      left:
        offset: 1,0
      right:
        offset: -1,0

- type: entity
  parent: KsCrewVehiclePassThroughTestHull
  id: KsCrewVehiclePassThroughTestHullWideCabin
  components:
  - type: KsCrewVehicleInterior
    interiorPath: /Maps/Test/_KS14/CrewVehicle/basic.yml
    passThroughBounds: -3,-3,8,8
    doorways:
      left:
        offset: 1,0
      right:
        offset: -1,0

- type: entity
  id: KsCrewVehiclePassThroughTestMob
  components:
  - type: Physics
    bodyType: KinematicController
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeCircle
          radius: 0.35
        layer:
        - MobLayer
        mask:
        - MobMask
  - type: MobState
  - type: Damageable
  - type: Injurable
    damageContainer: Biological

- type: entity
  parent: KsCrewVehicleInteriorBaseWall
  id: KsCrewVehiclePassThroughTestWall

- type: entity
  id: KsCrewVehiclePassThroughTestShooter

- type: entity
  parent: BaseBullet
  id: KsCrewVehiclePassThroughTestRoundPlain
  components:
  - type: Projectile
    damage:
      types:
        Structural: 100
        Piercing: 10
  - type: KsProjectileFalloff

- type: entity
  parent: KsCrewVehiclePassThroughTestRoundPlain
  id: KsCrewVehiclePassThroughTestRoundEntryOnly
  components:
  - type: Projectile
    damage:
      types:
        Structural: 100
        Piercing: 10
    penetrationThreshold: 375
    penetrationDamageTypeRequirement:
    - Structural

- type: entity
  parent: KsCrewVehiclePassThroughTestRoundPlain
  id: KsCrewVehiclePassThroughTestRoundThrough
  components:
  - type: Projectile
    damage:
      types:
        Structural: 100
        Piercing: 10
    penetrationThreshold: 2000
    penetrationDamageTypeRequirement:
    - Structural
";

    /// <summary>
    ///     A share of the hull's destruction to go through it once (<see cref="KsCCVars.GunPenetrationMinShots"/>): a
    ///         quarter of the test hull's 1000, so that it survives a round going in and out, and the test rounds'
    ///         budgets are in those terms.
    /// </summary>
    private const float MinShots = 4f;

    private const float Speed = 20f;

    /// <summary>
    ///     Where, across the hull's east side, the round hits it: off its middle, so a mapping that loses the offset shows.
    /// </summary>
    private const float HitOffset = 0.5f;

    private static readonly Vector2 HullPosition = new(0.5f, 30.5f);

    private SharedTransformSystem TransformSystem => SEntMan.System<SharedTransformSystem>();

    private sealed record Scene(EntityUid Hull, Angle HullRotation, MapId MapId, EntityUid? InteriorGrid);

    /// <summary>
    ///     A round as it was after a tick, with what it was stamped with: it may be gone by the time anything is asserted.
    /// </summary>
    private sealed record Sighting(EntityUid Uid, MapId MapId, Vector2 Position, Vector2 Velocity, NetUserId? Owner, MapCoordinates? FalloffOrigin);

    private float Damage(EntityUid uid) =>
        SEntMan.System<DamageableSystem>().GetTotalDamage(SEntity<DamageableComponent>(uid).AsNullable()).Float();

    private async Task<Scene> SpawnHullAsync(double degrees, bool loadInterior, bool wideCabin = false, EntProtoId? hullPrototype = null, bool overrideMinShots = true)
    {
        if (overrideMinShots)
            await OverrideCVar(Side.Server, KsCCVars.GunPenetrationMinShots, MinShots);
        var testMap = await Pair.CreateTestMap();
        var rotation = Angle.FromDegrees(degrees);
        EntityUid hull = default;
        EntityUid? grid = null;
        await Server.WaitPost(() =>
        {
            hull = SEntMan.Spawn(hullPrototype ?? (wideCabin ? _wideCabinHull : _hull), new MapCoordinates(HullPosition, testMap.MapId));
            TransformSystem.SetWorldRotation(hull, rotation);
            if (!loadInterior)
                return;

            Assert.That(SEntMan.System<KsCrewVehicleInteriorSystem>()
                .EnsureInterior(SEntity<KsCrewVehicleInteriorComponent>(hull), out var interiorMap), Is.True, "precondition: the interior loads");
            var children = SComp<TransformComponent>(interiorMap!.Value).ChildEnumerator;
            while (children.MoveNext(out var childUid))
            {
                if (SEntMan.HasComponent<MapGridComponent>(childUid))
                    grid = childUid;
            }

            Assert.That(grid, Is.Not.Null, "precondition: the interior has its grid");
        });
        await Pair.RunTicksSync(2);
        return new Scene(hull, rotation, testMap.MapId, grid);
    }

    /// <summary>
    ///     The hull's body in its own frame, and the cabin's outline on its grid: the hull's own, or (the test interior
    ///         has nothing solid in it) its tiles.
    /// </summary>
    private (Box2 Hull, Box2 Cabin) Boxes(Scene scene)
    {
        var hullBox = SComp<FixturesComponent>(scene.Hull).Fixtures["fix1"].Shape.ComputeAABB(Robust.Shared.Physics.Transform.Empty, 0);
        var cabin = SComp<KsCrewVehicleInteriorComponent>(scene.Hull).PassThroughBounds
                    ?? (scene.InteriorGrid is { } grid ? SComp<MapGridComponent>(grid).LocalAABB : default);
        return (hullBox, cabin);
    }

    /// <summary>
    ///     A world point given in the hull's frame.
    /// </summary>
    private Vector2 HullToWorld(Scene scene, Vector2 hullLocal) => HullPosition + scene.HullRotation.RotateVec(hullLocal);

    private async Task<EntityUid> SpawnMobAsync(MapCoordinates coordinates)
    {
        var mob = EntityUid.Invalid;
        await Server.WaitPost(() => mob = SEntMan.Spawn(_mob, coordinates));
        return mob;
    }

    /// <summary>
    ///     Fires <paramref name="prototype"/> at the hull's east side, <see cref="HitOffset"/> along it, flying west in the
    ///         hull's frame, by a shooter far away. Every round of it seen after each tick is recorded until none is left.
    /// </summary>
    private async Task<(EntityUid Round, List<Sighting> Sightings)> FireAsync(Scene scene, EntProtoId prototype, NetUserId? owner = null)
    {
        var round = EntityUid.Invalid;
        await Server.WaitPost(() =>
        {
            var shooter = SEntMan.Spawn(_shooter, new MapCoordinates(HullPosition + new Vector2(40f, 40f), scene.MapId));
            round = SEntMan.Spawn(prototype, new MapCoordinates(HullToWorld(scene, new Vector2(6f, HitOffset)), scene.MapId));
            if (owner is { } ownerUserId)
                SEntMan.System<KsKillCreditSystem>().SetOwner(round, ownerUserId);
            SEntMan.System<SharedGunSystem>().ShootProjectile(round,
                scene.HullRotation.RotateVec(new Vector2(-1f, 0f)),
                Vector2.Zero,
                gunUid: null,
                user: shooter,
                speed: Speed);
        });

        var sightings = new List<Sighting>();
        var ticks = (int)(3f * SGameTiming.TickRate);
        for (var tick = 0; tick < ticks; tick++)
        {
            await Pair.RunTicksSync(1);
            var any = false;
            await Server.WaitPost(() =>
            {
                var query = SEntMan.EntityQueryEnumerator<ProjectileComponent, PhysicsComponent, MetaDataComponent>();
                while (query.MoveNext(out var uid, out _, out var physicsComponent, out var metaDataComponent))
                {
                    if (metaDataComponent.EntityPrototype?.ID != prototype.Id || SEntMan.IsQueuedForDeletion(uid))
                        continue;

                    any = true;
                    var coordinates = TransformSystem.GetMapCoordinates(uid);
                    sightings.Add(new Sighting(uid,
                        coordinates.MapId,
                        coordinates.Position,
                        SEntMan.System<SharedPhysicsSystem>().GetMapLinearVelocity(uid, physicsComponent),
                        SEntMan.TryGetComponent<KsCreditOwnerComponent>(uid, out var ownerComponent) ? ownerComponent.OwnerUserId : null,
                        SEntMan.TryGetComponent<KsProjectileFalloffComponent>(uid, out var falloffComponent)
                            ? new MapCoordinates(falloffComponent.OriginPosition, falloffComponent.OriginMap)
                            : null));
                }
            });

            if (!any && tick > 2)
                break;
        }

        return (round, sightings);
    }

    private Sighting? FirstOn(List<Sighting> sightings, MapId mapId, EntityUid except)
    {
        foreach (var sighting in sightings)
        {
            if (sighting.MapId == mapId && sighting.Uid != except)
                return sighting;
        }

        return null;
    }

    private MapId InteriorMapId(Scene scene) => SComp<TransformComponent>(scene.InteriorGrid!.Value).MapID;

    /// <summary>
    ///     A grid-local position on the interior.
    /// </summary>
    private Vector2 ToGrid(Scene scene, Vector2 world) =>
        Vector2.Transform(world, TransformSystem.GetInvWorldMatrix(scene.InteriorGrid!.Value));

    private static void AssertDirection(Vector2 velocity, Vector2 expected, string what)
    {
        Assert.That(velocity.Length(), Is.EqualTo(Speed).Within(0.01f), $"{what} keeps its speed");
        var direction = Vector2.Normalize(velocity);
        Assert.That(Vector2.Dot(direction, expected), Is.GreaterThan(0.9999f), $"{what} flies {expected}, not {direction}");
    }

    /// <summary>
    ///     Enough budget to get in, not out: a copy appears inside where the hull's east side maps to, flying the mapped
    ///         way, and hits the occupant in its way; nothing comes out. A round that penetrates nothing makes no copy.
    /// </summary>
    [Test]
    public async Task ApRoundGoesInHitsAndStops()
    {
        var scene = await SpawnHullAsync(0d, loadInterior: true);
        var (hullBox, cabin) = Boxes(scene);
        var share = SEntMan.System<SharedDestructibleSystem>().DestroyedAt(scene.Hull).Float() / MinShots;

        // Where the hull's east side, HitOffset along it, is in the cabin: the cabin is the hull turned half round, so its
        //      west side, the same fraction along it the other way.
        var entryY = cabin.Center.Y - HitOffset / (hullBox.Height / 2f) * (cabin.Height / 2f);
        EntityUid occupant = default;
        await Server.WaitPost(() =>
        {
            var gridTransform = TransformSystem.GetWorldMatrix(scene.InteriorGrid!.Value);
            occupant = SEntMan.Spawn(_mob, new MapCoordinates(Vector2.Transform(new Vector2(cabin.Center.X, entryY), gridTransform), InteriorMapId(scene)));
        });
        await Pair.RunTicksSync(2);

        var (round, sightings) = await FireAsync(scene, _entryOnlyRound);

        await Server.WaitAssertion(() =>
        {
            var inside = FirstOn(sightings, InteriorMapId(scene), round);
            Assert.That(inside, Is.Not.Null, "a round appears inside");
            var local = ToGrid(scene, inside!.Position);
            Assert.That(local.Y, Is.EqualTo(entryY).Within(0.05f), "at the height its hit maps to");
            Assert.That(local.X, Is.LessThan(cabin.Left + Speed / SGameTiming.TickRate + 0.1f), "coming in by the cabin's west side");
            AssertDirection(inside.Velocity, new Vector2(1f, 0f), "the round inside");

            Assert.That(Damage(occupant), Is.GreaterThan(0f), "the occupant in its way is hit");
            Assert.That(Damage(scene.Hull), Is.EqualTo(share).Within(1f), "the hull is holed once");
            Assert.That(FirstOn(sightings, scene.MapId, round), Is.Null, "nothing comes out");
        });

        // The control: a round that goes through nothing ends at the hull.
        var (plainRound, plainSightings) = await FireAsync(scene, _plainRound);
        await Server.WaitAssertion(() =>
        {
            Assert.That(FirstOn(plainSightings, InteriorMapId(scene), plainRound), Is.Null, "a round that penetrates nothing is not carried in");
            Assert.That(FirstOn(plainSightings, scene.MapId, plainRound), Is.Null, "nor on");
        });
    }

    /// <summary>
    ///     Enough budget to get in and out: the hull is holed twice, the round crosses the cabin (a wall piece in its way or
    ///         not) and comes out of the far side, flying on the way it went in, and hits what is behind; it answers to the
    ///         same player and counts its falloff from the same muzzle.
    /// </summary>
    [TestCase(0d, false)]
    [TestCase(90d, false)]
    [TestCase(0d, true)]
    [TestCase(0d, false, true)]
    public async Task ApRoundGoesThroughAndOut(double degrees, bool wallInside, bool wideCabin = false)
    {
        var scene = await SpawnHullAsync(degrees, loadInterior: true, wideCabin);
        var (hullBox, cabin) = Boxes(scene);
        var share = SEntMan.System<SharedDestructibleSystem>().DestroyedAt(scene.Hull).Float() / MinShots;
        var entryY = cabin.Center.Y - HitOffset / (hullBox.Height / 2f) * (cabin.Height / 2f);

        if (wallInside)
        {
            await Server.WaitPost(() =>
            {
                var gridTransform = TransformSystem.GetWorldMatrix(scene.InteriorGrid!.Value);
                SEntMan.Spawn(_wall, new MapCoordinates(Vector2.Transform(new Vector2(cabin.Center.X, entryY), gridTransform), InteriorMapId(scene)));
            });
        }

        var target = await SpawnMobAsync(new MapCoordinates(HullToWorld(scene, new Vector2(-5f, HitOffset)), scene.MapId));
        await Pair.RunTicksSync(2);

        var owner = new NetUserId(Guid.NewGuid());
        var (round, sightings) = await FireAsync(scene, _throughRound, owner);

        await Server.WaitAssertion(() =>
        {
            var inside = FirstOn(sightings, InteriorMapId(scene), round);
            Assert.That(inside, Is.Not.Null, "a round appears inside");
            AssertDirection(inside!.Velocity, new Vector2(1f, 0f), "the round inside");

            var outside = FirstOn(sightings, scene.MapId, round);
            Assert.That(outside, Is.Not.Null, wallInside ? "a wall piece inside doesn't stop it: it comes out" : "it comes out");
            var hullLocal = (-scene.HullRotation).RotateVec(outside!.Position - HullPosition);
            Assert.That(hullLocal.X, Is.LessThan(hullBox.Left), "out of the hull's west side, the far side");
            Assert.That(hullLocal.X, Is.GreaterThan(hullBox.Left - Speed / SGameTiming.TickRate - 0.2f), "just out of it");
            Assert.That(hullLocal.Y, Is.EqualTo(HitOffset).Within(0.05f), "in line with where it went in");
            AssertDirection(outside.Velocity, scene.HullRotation.RotateVec(new Vector2(-1f, 0f)), "the round outside");

            Assert.That(Damage(scene.Hull), Is.EqualTo(share * 2f).Within(2f), "the hull is holed going in and going out");
            Assert.That(Damage(target), Is.GreaterThan(0f), "what is behind the hull is hit");

            Assert.That(inside.Owner, Is.EqualTo(owner), "inside, it answers to the player the round fired did");
            Assert.That(outside.Owner, Is.EqualTo(owner), "and out again");
            Assert.That(outside.FalloffOrigin, Is.Not.Null, "it keeps its falloff");
            Assert.That(outside.FalloffOrigin!.Value.MapId, Is.EqualTo(scene.MapId), "counted from the muzzle's map");
            Assert.That(Vector2.Distance(outside.FalloffOrigin.Value.Position, HullToWorld(scene, new Vector2(6f, HitOffset))), Is.LessThan(0.01f),
                "and from the muzzle");
        });
    }

    /// <summary>
    ///     Nobody has been inside: the hull is holed going in and out at once, without loading the interior, and the
    ///         round comes out of the far side.
    /// </summary>
    [Test]
    public async Task UnloadedInteriorChargesBoth()
    {
        var scene = await SpawnHullAsync(0d, loadInterior: false);
        var (hullBox, _) = Boxes(scene);
        var share = SEntMan.System<SharedDestructibleSystem>().DestroyedAt(scene.Hull).Float() / MinShots;
        var target = await SpawnMobAsync(new MapCoordinates(HullToWorld(scene, new Vector2(-5f, HitOffset)), scene.MapId));
        await Pair.RunTicksSync(2);

        var (round, sightings) = await FireAsync(scene, _throughRound);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SComp<KsCrewVehicleInteriorComponent>(scene.Hull).InteriorMap, Is.Null, "the interior is not loaded for it");
            Assert.That(Damage(scene.Hull), Is.EqualTo(share * 2f).Within(2f), "the hull is holed going in and going out");

            var outside = FirstOn(sightings, scene.MapId, round);
            Assert.That(outside, Is.Not.Null, "it comes out");
            Assert.That(outside!.Position.X - HullPosition.X, Is.LessThan(hullBox.Left), "out of the far side");
            Assert.That(outside.Position.Y - HullPosition.Y, Is.EqualTo(HitOffset).Within(0.05f), "in line with where it went in");
            AssertDirection(outside.Velocity, new Vector2(-1f, 0f), "the round outside");
            Assert.That(Damage(target), Is.GreaterThan(0f), "what is behind the hull is hit");
        });
    }

    /// <summary>
    ///     What the penetration framework does with a round against a hull needing <paramref name="required"/> a hit
    ///         (<c>PredictedProjectileSystem.TryPenetrate</c>): a hit deals at most what is left of the round's budget, and
    ///         lets it go on only if it dealt what the hull needs and the budget is not used up by it.
    /// </summary>
    private static (bool Enters, bool Exits) WorkOutPassThrough(float required, float threshold)
    {
        var spent = 0f;
        var entered = false;
        for (var hit = 0; hit < 2; hit++)
        {
            var dealt = MathF.Min(required, threshold - spent);
            if (dealt < required)
                return (entered, false);

            spent += required;
            if (spent >= threshold)
                return (entered, false);

            entered = true;
        }

        return (true, true);
    }

    /// <summary>
    ///     The shipped tank's armour-piercing shell against the shipped vehicles' hulls: whether it gets into the
    ///         interior and whether it comes out of the far side follow from its penetration budget and what the hull needs
    ///         a hit, worked out as the framework does, so a retune of either moves the expectation with it.
    /// </summary>
    [TestCase("KsCrewVehicleHumvee")]
    [TestCase("KsCrewVehicleTank")]
    public async Task LiveApShellAgainstLiveHull(string hullId)
    {
        EntProtoId hullPrototype = hullId;
        EntProtoId shell = "KsBulletTankShellAP";
        var scene = await SpawnHullAsync(0d, loadInterior: true, hullPrototype: hullPrototype, overrideMinShots: false);

        var minShots = Server.CfgMan.GetCVar(KsCCVars.GunPenetrationMinShots);
        var required = SEntMan.System<SharedDestructibleSystem>().DestroyedAt(scene.Hull).Float() / minShots;
        Assert.That(SProtoMan.Index(shell).TryGetComponent<ProjectileComponent>(out var shellProjectile, SEntMan.ComponentFactory), Is.True);
        var threshold = shellProjectile!.PenetrationThreshold.Float();
        var (enters, exits) = WorkOutPassThrough(required, threshold);
        Assert.That(enters, Is.True, $"precondition: {hullId}'s hull is one the shell penetrates (needs {required} a hit, shell has {threshold})");

        // Before the shot: a hull the exit hole destroys takes its interior with it.
        var interiorMapId = InteriorMapId(scene);
        var (round, sightings) = await FireAsync(scene, shell);

        var inside = FirstOn(sightings, interiorMapId, round);
        var outside = FirstOn(sightings, scene.MapId, round);
        Assert.That(inside, Is.Not.Null, $"the shell goes into {hullId}");
        if (exits)
            Assert.That(outside, Is.Not.Null, $"and, with {threshold} to spend at {required} a hull, comes out of its far side");
        else
            Assert.That(outside, Is.Null, $"and, with {threshold} to spend at {required} a hull, is spent by the second hole and does not come out");
    }
}
