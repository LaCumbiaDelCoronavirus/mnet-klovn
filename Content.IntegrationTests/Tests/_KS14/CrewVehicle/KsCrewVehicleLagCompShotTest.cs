#nullable enable
using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server._KS14.Projectiles;
using Content.Shared._KS14.CrewVehicle;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Projectiles;
using Content.Shared.Standing;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.CrewVehicle;

/// <summary>
///     A shot the server compensates for its shooter's lag still hits a target that never moved: a mounted gun's and a
///         hand-held gun's, at a standing mob and at a downed one aimed at, and at a mob a hull ran over first. A test
///         connection reports no ping, so the lag is put in by hand, as the server would from a real one.
/// </summary>
[TestOf(typeof(LagCompProjectileSystem))]
public sealed class KsCrewVehicleLagCompShotTest : KsCrewVehicleGunnerTestBase
{
    private readonly EntProtoId _rifle = "KsCrewVehicleLagCompTestRifle";
    private readonly EntProtoId _realRoundRifle = "KsCrewVehicleLagCompTestRealRoundRifle";
    private readonly EntProtoId _realRoundHull = "KsCrewVehicleLagCompTestRealRoundHull";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsCrewVehicleLagCompTestRifle
  categories: [ HideSpawnMenu ]
  components:
  - type: Item
  - type: Gun
    fireRate: 4
    minAngle: 0
    maxAngle: 0
    selectedMode: SemiAuto
    availableModes:
    - SemiAuto
    soundGunshot:
      path: /Audio/Weapons/Guns/Gunshots/pistol.ogg
  - type: BallisticAmmoProvider
    proto: KsCrewVehicleGunnerTestCartridge
    capacity: 10
    whitelist:
      tags:
      - KsCrewVehicleGunnerTestRound

# The same, firing a real round: its projectile's fixtures and speed are the game's.
- type: entity
  parent: KsCrewVehicleLagCompTestRifle
  id: KsCrewVehicleLagCompTestRealRoundRifle
  categories: [ HideSpawnMenu ]
  components:
  - type: BallisticAmmoProvider
    proto: CartridgeLightRifle
    capacity: 10

- type: entity
  parent: KsCrewVehicleGunnerTestGun
  id: KsCrewVehicleLagCompTestRealRoundGun
  categories: [ HideSpawnMenu ]
  components:
  - type: BallisticAmmoProvider
    proto: CartridgeLightRifle
    capacity: 10
    whitelist:
      tags:
      - CartridgeLightRifle

- type: entity
  parent: KsCrewVehicleGunnerTestHull
  id: KsCrewVehicleLagCompTestRealRoundHull
  components:
  - type: KsCrewVehicleMounts
    mounts:
      main:
        prototype: KsCrewVehicleLagCompTestRealRoundGun
        offset: 0,-0.6
";

    public enum Shooter
    {
        Mounted,
        HandHeld,
        MountedRealRound,
        HandHeldRealRound,
    }

    public enum TargetKind
    {
        Standing,
        Downed,
        RunOver,
    }

    /// <summary>How far the target stands from the hull's centre or the player, in tiles, ahead of the hull.</summary>
    private const float TargetDistance = 6f;

    [TestCase(Shooter.Mounted, TargetKind.Standing, 0)]
    [TestCase(Shooter.Mounted, TargetKind.Standing, 50)]
    [TestCase(Shooter.Mounted, TargetKind.Standing, 150)]
    [TestCase(Shooter.Mounted, TargetKind.Downed, 0)]
    [TestCase(Shooter.Mounted, TargetKind.Downed, 150)]
    [TestCase(Shooter.HandHeld, TargetKind.Standing, 0)]
    [TestCase(Shooter.HandHeld, TargetKind.Standing, 50)]
    [TestCase(Shooter.HandHeld, TargetKind.Standing, 150)]
    [TestCase(Shooter.MountedRealRound, TargetKind.Standing, 150)]
    [TestCase(Shooter.MountedRealRound, TargetKind.Downed, 150)]
    [TestCase(Shooter.HandHeldRealRound, TargetKind.Standing, 150)]
    [TestCase(Shooter.Mounted, TargetKind.RunOver, 0)]
    [TestCase(Shooter.Mounted, TargetKind.RunOver, 150)]
    public async Task CompensatedShotHitsAStillTarget(Shooter shooter, TargetKind target, int lagMilliseconds)
    {
        EntityUid gun;
        EntityUid shooterUid;
        EntityUid mob = EntityUid.Invalid;

        if (shooter is Shooter.Mounted or Shooter.MountedRealRound)
        {
            HullScene scene;
            (scene, gun) = await SeatGunnerAsync(shooter == Shooter.MountedRealRound ? _realRoundHull : _gunnerHull);
            shooterUid = gun;

            if (target == TargetKind.RunOver)
            {
                mob = await RunOverAMobAsync(scene);
            }
            else
            {
                await Server.WaitPost(() =>
                {
                    var (hullPosition, hullRotation) = Transform.GetWorldPositionRotation(scene.Hull);
                    var position = hullPosition + KsCrewVehicleHullFrame.Forward(hullRotation) * TargetDistance;
                    mob = SEntMan.SpawnAtPosition(Human, Transform.ToCoordinates(MapData.Grid.Owner, new MapCoordinates(position, MapId)));
                });
            }
        }
        else
        {
            await SetCombatMode(true);
            gun = EntityUid.Invalid;
            await Server.WaitPost(() =>
            {
                gun = SEntMan.SpawnAtPosition(shooter == Shooter.HandHeldRealRound ? _realRoundRifle : _rifle, SComp<TransformComponent>(SPlayer).Coordinates);
                Assert.That(SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(SPlayer, gun), "precondition: holding the rifle");
                mob = SEntMan.SpawnAtPosition(Human, SComp<TransformComponent>(SPlayer).Coordinates.Offset(new Vector2(0f, -TargetDistance)));
            });
            shooterUid = SPlayer;
        }

        if (target == TargetKind.Downed)
            await Server.WaitPost(() => SEntMan.System<StandingStateSystem>().Down(mob));

        await RunTicks(10);
        await Server.WaitPost(() => SEntMan.AddComponent<KsCrewVehicleGunnerTestHitRecorderComponent>(mob));

        await Server.WaitPost(() =>
        {
            var before = Rounds(gun).Select(round => round.Owner).ToHashSet();
            var shot = ShotAt(gun, OutsideAt(WorldPosition(mob)));
            shot.Target = SEntMan.GetNetEntity(mob);
            ReceiveFrom(ServerSession, shot);

            var fired = Rounds(gun).Where(round => !before.Contains(round.Owner)).ToList();
            Assert.That(fired, Has.Count.EqualTo(1), "precondition: one round fired");
            if (lagMilliseconds > 0)
            {
                var roundUid = fired[0].Owner;
                SEntMan.System<LagCompProjectileSystem>().CompensateProjectile(roundUid, shooterUid, TimeSpan.FromMilliseconds(lagMilliseconds));
                Assert.That(SEntMan.TryGetComponent<LagCompensatingProjectileComponent>(roundUid, out var compensating) &&
                            compensating.IgnoredRealTargets.Contains(mob),
                    "precondition: the round is compensated for the target, so only its ghost can be hit");
            }
        });

        // Only the shot's damage: the test grid is open to space, so the mob is also hurt by the vacuum, from nowhere.
        var hit = false;
        for (var tick = 0; tick < TickRate && !hit; tick++)
        {
            await RunTicks(1);
            await Server.WaitPost(() => hit = SComp<KsCrewVehicleGunnerTestHitRecorderComponent>(mob).Hits.Any(recorded => recorded.Origin == shooterUid));
        }

        Assert.That(hit, $"{shooter} shot at a {target} mob with {lagMilliseconds} ms of lag passed through it on the server");
    }

    /// <summary>
    ///     Drives the hull at a mob fast enough to run it over, stops, and returns the mob, wherever it ended up.
    /// </summary>
    private async Task<EntityUid> RunOverAMobAsync(HullScene scene)
    {
        if (SEntMan.HasComponent<Content.Shared._KS14.CrewVehicle.Engine.KsCrewVehicleEngineComponent>(scene.Hull))
            await StartEngineAsync(scene.Hull);

        var mob = EntityUid.Invalid;
        var facing = Vector2.Zero;
        await Server.WaitPost(() =>
        {
            var (hullPosition, hullRotation) = Transform.GetWorldPositionRotation(scene.Hull);
            facing = KsCrewVehicleHullFrame.Forward(hullRotation);
            mob = SEntMan.SpawnAtPosition(Human,
                Transform.ToCoordinates(MapData.Grid.Owner, new MapCoordinates(hullPosition + facing * TargetDistance, MapId)));
        });

        var struck = false;
        var mobStart = Vector2.Zero;
        await Server.WaitPost(() => mobStart = WorldPosition(mob));

        // Tank controls: up drives the way the hull faces, whichever way that is on screen.
        await HoldAsync(scene.Hull, Robust.Shared.Maths.DirectionFlag.North, TickRate * 6, () =>
        {
            // Run over, or shoved along: either way struck by the hull.
            struck |= SEntMan.System<Content.Shared.Damage.Systems.DamageableSystem>()
                .GetAllDamage(SEntity<Content.Shared.Damage.Components.DamageableComponent>(mob).AsNullable()).AnyPositive() ||
                (WorldPosition(mob) - mobStart).Length() > 0.5f;
            return struck;
        });

        Assert.That(struck, "precondition: the hull ran the mob over");

        // Back off, so the mob is ahead of the muzzle again, and let everything come to rest.
        await HoldAsync(scene.Hull, Robust.Shared.Maths.DirectionFlag.South, TickRate);
        await SettleAsync(scene.Hull);
        await RunTicks(TickRate);
        return mob;
    }
}
