#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Shared._KS14.CrewVehicle;
using Content.Shared._KS14.Damage.Events;
using Content.Shared._KS14.Portal;
using Content.Shared._KS14.PredictedSpawning;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Maps;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Projectiles;
using Content.Shared.Standing;
using Content.Shared.Teleportation.Systems;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using NUnit.Framework;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.CrewVehicle;

/// <summary>
///     A driver-gunner fires their hull's full-auto gun through their own client at a mob lying ahead, after the hull has
///         driven through a portal onto another map, as in play: every round hits it on the server, and every input of the
///         rule that lets rounds pass over lying mobs (<see cref="Content.Shared.Damage.Systems.RequireProjectileTargetSystem"/>)
///         is the same on the client and the server, on every tick. Then again after standing up and sitting back down.
/// </summary>
[TestOf(typeof(KsPortalTravelSystem))]
public sealed class KsCrewVehiclePortalGunnerTest : KsCrewVehicleGunnerTestBase
{
    private readonly EntProtoId _autoHull = "KsCrewVehiclePortalGunnerTestHull";
    private readonly EntProtoId _portal = "KsViewPortal";
    private static readonly ProtoId<DamageTypePrototype> _blunt = "Blunt";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: KsCrewVehicleGunnerTestGun
  id: KsCrewVehiclePortalGunnerTestGun
  categories: [ HideSpawnMenu ]
  components:
  - type: Gun
    fireRate: 8
    minAngle: 0
    maxAngle: 0
    selectedMode: FullAuto
    availableModes:
    - FullAuto
    soundGunshot:
      path: /Audio/Weapons/Guns/Gunshots/pistol.ogg
  - type: BallisticAmmoProvider
    proto: CartridgeLightRifle
    capacity: 60
    whitelist:
      tags:
      - CartridgeLightRifle

- type: entity
  parent: KsCrewVehicleGunnerTestHull
  id: KsCrewVehiclePortalGunnerTestHull
  components:
  - type: KsCrewVehicleMounts
    mounts:
      main:
        prototype: KsCrewVehiclePortalGunnerTestGun
        offset: 0,-0.6
";

    public enum Lying
    {
        /// <summary>Put down where it stands, by <see cref="StandingStateSystem.Down"/>.</summary>
        Downed,

        /// <summary>Run over by the hull, then put in crit so it stays down, as the bodies shot in play were.</summary>
        RunOverCritical,
    }

    /// <summary>How far ahead of the hull the mob is put, in tiles.</summary>
    private const float MobDistance = 6f;

    /// <summary>How far ahead of the hull the first portal stands.</summary>
    private const float PortalDistance = 3f;

    /// <summary>How far behind the second floor's centre the portal the hull comes out of stands.</summary>
    private const float SecondPortalSetBack = 7f;

    /// <summary>How far past that portal the hull is driven before anything else.</summary>
    private const float ClearOfPortal = 4.5f;

    [TestCase(true, Lying.RunOverCritical, true)]
    [TestCase(false, Lying.RunOverCritical, true)]
    [TestCase(true, Lying.Downed, true)]
    [TestCase(false, Lying.Downed, true)]
    // The click missed it: the round's target is nothing, as when the topmost thing under the cursor was something else.
    [TestCase(true, Lying.RunOverCritical, false)]
    [TestCase(false, Lying.Downed, false)]
    public async Task LyingMobIsHitAfterAPortalTrip(bool throughPortal, Lying lying, bool clickedTheMob)
    {
        var (scene, weapon) = await SeatGunnerAsync(_autoHull);
        if (SEntMan.HasComponent<Content.Shared._KS14.CrewVehicle.Engine.KsCrewVehicleEngineComponent>(scene.Hull))
            await StartEngineAsync(scene.Hull);

        var gridUid = MapData.Grid.Owner;
        var mapId = MapId;
        if (throughPortal)
            (gridUid, mapId) = await DriveThroughAPortalAsync(scene);

        var mob = lying == Lying.RunOverCritical
            ? await RunOverAMobAsync(scene, gridUid, mapId)
            : await DownAMobAheadAsync(scene, gridUid, mapId);

        // Never dead: the hits of the first stage, on top of crit and the vacuum of the test floor, killed it before the
        //      second, and a dead body sleeps out of every collision (CollisionWakeComponent), so every round passed through
        //      it for that reason alone.
        await Server.WaitPost(() =>
            SEntMan.System<MobThresholdSystem>().SetMobStateThreshold(mob, FixedPoint2.MaxValue, MobState.Dead));

        // Hurt into crit, not set to it: a mob state set by hand is put back by the next change to its damage.
        if (lying == Lying.RunOverCritical)
            await PutInCritAsync(mob);

        await RunTicks(10);
        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.System<StandingStateSystem>().IsDown(mob), "precondition: the mob lies down"));

        var mobCoordinates = default(EntityCoordinates);
        await Server.WaitPost(() => mobCoordinates = SComp<TransformComponent>(mob).Coordinates);

        await FireAndCompareAsync(weapon, mob, clickedTheMob, "first seating");

        // Up and back down into the same seat: what fixed it in play.
        await StandAsync();
        await SitAsync(scene.DriverSeat);

        // Back where it lay: the hits shoved it along.
        await Server.WaitPost(() =>
        {
            Transform.SetCoordinates(mob, mobCoordinates);
            SEntMan.System<Robust.Shared.Physics.Systems.SharedPhysicsSystem>().SetLinearVelocity(mob, Vector2.Zero);
        });
        await RunTicks(5);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<StandingStateSystem>().IsDown(mob), "precondition: the mob still lies down");
            Assert.That(SEntMan.System<MobStateSystem>().IsDead(mob), Is.False, "precondition: the mob is not dead");
        });
        await FireAndCompareAsync(weapon, mob, clickedTheMob, "after sitting back down");
    }

    /// <summary>
    ///     Two linked portals, one ahead of the hull and one on a second map with floor of its own, and the hull driven across
    ///         the first, so the portal's own travel moves it. Returns the second map's grid and map.
    /// </summary>
    private async Task<(EntityUid Grid, MapId Map)> DriveThroughAPortalAsync(HullScene scene)
    {
        var secondMap = await Pair.CreateTestMap();
        var secondPortal = EntityUid.Invalid;
        await Server.WaitPost(() =>
        {
            var tile = new Tile(ProtoMan.Index<ContentTileDefinition>(Plating).TileId);
            var tiles = new List<(Vector2i GridIndices, Tile Tile)>();
            for (var x = -FloorRadius; x <= FloorRadius; x++)
            {
                for (var y = -FloorRadius; y <= FloorRadius; y++)
                {
                    var indices = new Vector2i(x, y);
                    if (MapSystem.GetTileRef(secondMap.Grid, indices).Tile.IsEmpty)
                        tiles.Add((indices, tile));
                }
            }

            MapSystem.SetTiles(secondMap.Grid.Owner, secondMap.Grid.Comp, tiles);

            var (hullPosition, hullRotation) = Transform.GetWorldPositionRotation(scene.Hull);
            var forward = KsCrewVehicleHullFrame.Forward(hullRotation);
            // A portal is crossed along its own local Y.
            var portalRotation = new Angle(MathF.Atan2(forward.Y, forward.X) - MathF.PI / 2f);

            var first = SEntMan.SpawnEntity(_portal, new MapCoordinates(hullPosition + forward * PortalDistance, MapId));
            // Far enough back on the second floor to leave room ahead for the run-over.
            var second = SEntMan.SpawnEntity(_portal, new MapCoordinates(Transform.GetWorldPosition(secondMap.Grid.Owner) - forward * SecondPortalSetBack, secondMap.MapId));
            secondPortal = second;
            Transform.SetWorldRotation(first, portalRotation);
            Transform.SetWorldRotation(second, portalRotation + Transform.GetWorldRotation(secondMap.Grid.Owner));
            Assert.That(SEntMan.System<LinkedEntitySystem>().TryLink(first, second), "precondition: the portals link");
        });
        await RunTicks(5);

        await HoldAsync(scene.Hull, DirectionFlag.North, TickRate * 6, () => SComp<TransformComponent>(scene.Hull).MapID == secondMap.MapId);

        // On, well clear of the portal it came out of, so backing off after the run-over doesn't take it back through.
        await HoldAsync(scene.Hull, DirectionFlag.North, TickRate * 4, () =>
            (WorldPosition(scene.Hull) - Transform.GetWorldPosition(secondPortal)).Length() > ClearOfPortal);
        await SettleAsync(scene.Hull);
        await RunTicks(5);

        await Server.WaitAssertion(() =>
            Assert.That(SComp<TransformComponent>(scene.Hull).MapID, Is.EqualTo(secondMap.MapId), "precondition: the hull drove through the portal"));
        await Client.WaitAssertion(() =>
            Assert.That(CEntMan.GetComponent<TransformComponent>(ToClient(scene.Hull)).MapUid, Is.EqualTo(secondMap.CMapUid),
                "precondition: the client has the hull through the portal too"));

        return (secondMap.Grid.Owner, secondMap.MapId);
    }

    /// <summary>
    ///     Hurts the mob into crit.
    /// </summary>
    private async Task PutInCritAsync(EntityUid mob)
    {
        await Server.WaitPost(() =>
        {
            Assert.That(SEntMan.System<MobThresholdSystem>().TryGetThresholdForState(mob, MobState.Critical, out var critical),
                "precondition: the mob can go into crit");
            var damageableSystem = SEntMan.System<DamageableSystem>();
            damageableSystem.TryChangeDamage(mob,
                new DamageSpecifier(ProtoMan.Index(_blunt), critical!.Value + 1), ignoreResistances: true);
            Assert.That(SEntMan.System<MobStateSystem>().IsCritical(mob), "precondition: the mob is in crit");
        });
    }

    private async Task<EntityUid> DownAMobAheadAsync(HullScene scene, EntityUid gridUid, MapId mapId)
    {
        var mob = EntityUid.Invalid;
        await Server.WaitPost(() =>
        {
            var (hullPosition, hullRotation) = Transform.GetWorldPositionRotation(scene.Hull);
            var position = hullPosition + KsCrewVehicleHullFrame.Forward(hullRotation) * MobDistance;
            mob = SEntMan.SpawnAtPosition(Human, Transform.ToCoordinates(gridUid, new MapCoordinates(position, mapId)));
            SEntMan.System<StandingStateSystem>().Down(mob);
        });
        return mob;
    }

    /// <summary>
    ///     Drives the hull at a mob until it strikes it, backs off, and returns the mob, wherever it ended up.
    /// </summary>
    private async Task<EntityUid> RunOverAMobAsync(HullScene scene, EntityUid gridUid, MapId mapId)
    {
        var mob = EntityUid.Invalid;
        var mobStart = Vector2.Zero;
        await Server.WaitPost(() =>
        {
            var (hullPosition, hullRotation) = Transform.GetWorldPositionRotation(scene.Hull);
            var position = hullPosition + KsCrewVehicleHullFrame.Forward(hullRotation) * MobDistance;
            mob = SEntMan.SpawnAtPosition(Human, Transform.ToCoordinates(gridUid, new MapCoordinates(position, mapId)));
            mobStart = position;
        });

        // Not by its damage: the test floor is open to space, and the vacuum hurts it from the start.
        var struck = false;
        await HoldAsync(scene.Hull, DirectionFlag.North, TickRate * 6, () =>
        {
            struck |= SEntMan.HasComponent<Content.Shared._KS14.CrewVehicle.RunOver.KsCrewVehicleUnderHullComponent>(mob) ||
                      (WorldPosition(mob) - mobStart).Length() > 0.5f;
            return struck;
        });
        Assert.That(struck, "precondition: the hull ran the mob over");

        await HoldAsync(scene.Hull, DirectionFlag.South, TickRate);
        await SettleAsync(scene.Hull);
        await RunTicks(TickRate);
        return mob;
    }

    /// <summary>
    ///     One second of full-auto fire from the player's client at the mob, as its fire input sends it, recording each tick on
    ///         both sides what <see cref="Content.Shared.Damage.Systems.RequireProjectileTargetSystem"/> reads; then asserts that
    ///         the server dealt damage from the weapon and that both sides read the same.
    /// </summary>
    private async Task FireAndCompareAsync(EntityUid weapon, EntityUid mob, bool clickedTheMob, string stage)
    {
        var clientMob = ToClient(mob);
        var clientWeapon = ToClient(weapon);
        var weaponNetEntity = SEntMan.GetNetEntity(weapon);
        var mobNetEntity = SEntMan.GetNetEntity(mob);

        await Server.WaitPost(() =>
        {
            SEntMan.RemoveComponent<KsCrewVehicleGunnerTestHitRecorderComponent>(mob);
            SEntMan.AddComponent<KsCrewVehicleGunnerTestHitRecorderComponent>(mob);
        });
        await Client.WaitPost(() =>
        {
            CEntMan.RemoveComponent<KsCrewVehiclePortalGunnerTestClientHitsComponent>(clientMob);
            CEntMan.AddComponent<KsCrewVehiclePortalGunnerTestClientHitsComponent>(clientMob);
        });

        var serverRecord = new Dictionary<uint, string>();
        var clientRecord = new Dictionary<uint, string>();
        var serverRounds = new HashSet<string>();
        var clientRounds = new HashSet<string>();

        var fireTicks = TickRate;
        var flyTicks = TickRate / 2;
        for (var tick = 0; tick < fireTicks + flyTicks; tick++)
        {
            if (tick < fireTicks)
            {
                await Client.WaitPost(() =>
                {
                    // Aimed where the client sees the mob, in the frame of the grid under it.
                    var mobMap = CEntMan.System<SharedTransformSystem>().GetMapCoordinates(clientMob);
                    var gridUid = CEntMan.GetComponent<TransformComponent>(clientMob).GridUid ?? CEntMan.GetComponent<TransformComponent>(clientMob).MapUid!.Value;
                    var coordinates = CEntMan.System<SharedTransformSystem>().ToCoordinates(gridUid, mobMap);
                    CEntMan.RaisePredictiveEvent(new RequestShootEvent
                    {
                        Gun = weaponNetEntity,
                        Coordinates = CEntMan.GetNetCoordinates(coordinates),
                        Target = clickedTheMob ? mobNetEntity : null,
                    });
                });
            }

            await RunTicks(1);

            await Server.WaitPost(() =>
            {
                serverRecord[STiming.CurTick.Value] = DescribeMob(SEntMan, mob);
                foreach (var round in SEntMan.AllEntities<ProjectileComponent>().Where(round => round.Comp.Weapon == weapon))
                {
                    serverRounds.Add(DescribeRound(SEntMan, round, mob, weapon));
                }
            });
            await Client.WaitPost(() =>
            {
                clientRecord[CTiming.CurTick.Value] = DescribeMob(CEntMan, clientMob);
                var predictedSpawnSystem = CEntMan.System<KsSharedPredictedSpawnSystem>();
                foreach (var round in CEntMan.AllEntities<ProjectileComponent>()
                             .Where(round => round.Comp.Weapon == clientWeapon && predictedSpawnSystem.IsKeptLocally(round.Owner)))
                {
                    clientRounds.Add(DescribeRound(CEntMan, round, clientMob, clientWeapon));
                }
            });
        }

        var serverHits = 0;
        await Server.WaitPost(() => serverHits = SComp<KsCrewVehicleGunnerTestHitRecorderComponent>(mob).Hits.Count(hit => hit.Origin == weapon));
        var clientHits = 0;
        await Client.WaitPost(() => clientHits = CEntMan.GetComponent<KsCrewVehiclePortalGunnerTestClientHitsComponent>(clientMob).Hits);

        // Compared on the ticks both sides recorded, the client running ahead of the server, and only where neither side's
        //      reading changed for a while before: what the server alone changes reaches the client a state later.
        var settleTicks = TickRate / 4;
        bool Steady(Dictionary<uint, string> record, uint tick) =>
            Enumerable.Range(0, settleTicks).All(back => !record.TryGetValue(tick - (uint)back, out var earlier) || earlier == record[tick]);
        var disagreements = serverRecord.Keys.Intersect(clientRecord.Keys)
            .Where(tick => serverRecord[tick] != clientRecord[tick] && Steady(serverRecord, tick) && Steady(clientRecord, tick))
            .Select(tick => $"tick {tick}: server {serverRecord[tick]}, client {clientRecord[tick]}")
            .ToList();

        await Server.WaitPost(() =>
        {
            var hullUid = SComp<TransformComponent>(weapon).ParentUid;
            TestContext.Out.WriteLine($"[{stage}] mob at {WorldPosition(mob)} map {SComp<TransformComponent>(mob).MapID}, hull at {WorldPosition(hullUid)} map {SComp<TransformComponent>(hullUid).MapID}, muzzle {MuzzlePosition(weapon)}, rounds left {RoundsLeft(weapon)}");
        });
        TestContext.Out.WriteLine($"[{stage}] server hits {serverHits}, client hits {clientHits}");
        TestContext.Out.WriteLine($"[{stage}] server mob: {string.Join(" | ", serverRecord.Values.Distinct())}");
        TestContext.Out.WriteLine($"[{stage}] client mob: {string.Join(" | ", clientRecord.Values.Distinct())}");
        TestContext.Out.WriteLine($"[{stage}] server rounds: {string.Join(" | ", serverRounds)}");
        TestContext.Out.WriteLine($"[{stage}] client rounds: {string.Join(" | ", clientRounds)}");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(disagreements, Is.Empty, $"[{stage}] the client and the server read the lying mob differently");
            // A target the client sent is the same on both sides. One worked out from the aim point can differ while the body
            //      slides: the server alone shoves a lying body it hits, and the client aims where it last saw it.
            if (clickedTheMob)
            {
                Assert.That(serverRounds.Except(clientRounds).Concat(clientRounds.Except(serverRounds)), Is.Empty,
                    $"[{stage}] the client's rounds and the server's read differently: server {string.Join(" | ", serverRounds)}; client {string.Join(" | ", clientRounds)}");
            }

            Assert.That(serverHits, Is.GreaterThan(0), $"[{stage}] every round passed over the lying mob on the server (client hits: {clientHits})");
        }
    }

    private static string DescribeMob(IEntityManager entityManager, EntityUid mob)
    {
        var active = entityManager.GetComponent<RequireProjectileTargetComponent>(mob).Active;
        var standing = entityManager.GetComponent<StandingStateComponent>(mob).Standing;
        return $"active={active} standing={standing}";
    }

    /// <summary>
    ///     Each input of the pass-over rule, for one round: whether its target is the mob, its shooter the weapon, and
    ///         whether that shooter is gone or in a container.
    /// </summary>
    private static string DescribeRound(IEntityManager entityManager, Entity<ProjectileComponent> round, EntityUid mob, EntityUid weapon)
    {
        var target = entityManager.TryGetComponent<TargetedProjectileComponent>(round, out var targeted) ? targeted.Target : (EntityUid?)null;
        var shooter = round.Comp.Shooter;
        var shooterGone = shooter is { } shooterUid && entityManager.Deleted(shooterUid);
        var inContainer = shooter is { } containedUid && !shooterGone &&
                          entityManager.System<SharedContainerSystem>().IsEntityOrParentInContainer(containedUid);
        var targetText = target == null ? "none" : target == mob ? "mob" : "other";
        var shooterText = shooter == null ? "none" : shooter == weapon ? "weapon" : "other";
        return $"target={targetText} shooter={shooterText} gone={shooterGone} contained={inContainer}";
    }
}

/// <summary>
///     On a client: counts the hits its own kept rounds predicted on this entity.
/// </summary>
[RegisterComponent]
public sealed partial class KsCrewVehiclePortalGunnerTestClientHitsComponent : Component
{
    public int Hits;
}

public sealed partial class KsCrewVehiclePortalGunnerTestClientHitsSystem : EntitySystem
{
    [SubscribeLocalEvent]
    private void OnPreviewedDamage(Entity<KsCrewVehiclePortalGunnerTestClientHitsComponent> entity, ref KsPreviewedDamageEvent args)
    {
        entity.Comp.Hits++;
    }
}
