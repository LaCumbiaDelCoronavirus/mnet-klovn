#nullable enable
using System;
using System.Collections.Generic;
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
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.CrewVehicle;

/// <summary>
///     A compensated round hits a still target wherever it stands along the line of fire. The round moves more than a
///         tile a tick, and the target's lag-compensation ghost is soft, so physics alone only finds it when a step
///         happens to end inside it: the sweep puts the target at every point across one step.
/// </summary>
[TestOf(typeof(LagCompProjectileSystem))]
public sealed class KsCrewVehicleLagCompSweepTest : KsCrewVehicleGunnerTestBase
{
    private readonly EntProtoId _rifle = "KsCrewVehicleLagCompSweepTestRifle";
    private readonly EntProtoId _hull = "KsCrewVehicleLagCompSweepTestHull";
    private readonly EntProtoId _fastRifle = "KsCrewVehicleLagCompSweepTestFastRifle";
    private readonly EntProtoId _fastHull = "KsCrewVehicleLagCompSweepTestFastHull";

    // Real rounds, their fixtures and speed the game's, enough of them for a sweep. The parents are
    //      KsCrewVehicleLagCompShotTest's.
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: KsCrewVehicleLagCompTestRealRoundRifle
  id: KsCrewVehicleLagCompSweepTestRifle
  categories: [ HideSpawnMenu ]
  components:
  - type: BallisticAmmoProvider
    proto: CartridgeLightRifle
    capacity: 100

- type: entity
  parent: KsCrewVehicleLagCompTestRealRoundGun
  id: KsCrewVehicleLagCompSweepTestGun
  categories: [ HideSpawnMenu ]
  components:
  - type: BallisticAmmoProvider
    proto: CartridgeLightRifle
    capacity: 100
    whitelist:
      tags:
      - CartridgeLightRifle

- type: entity
  parent: KsCrewVehicleGunnerTestHull
  id: KsCrewVehicleLagCompSweepTestHull
  components:
  - type: KsCrewVehicleMounts
    mounts:
      main:
        prototype: KsCrewVehicleLagCompSweepTestGun
        offset: 0,-0.6

# Twice the usual speed, as the fastest guns fire (ShuttleGunFriendship): a step longer than a mob and a round together,
#   even split into physics' substeps.
- type: entity
  parent: KsCrewVehicleLagCompSweepTestRifle
  id: KsCrewVehicleLagCompSweepTestFastRifle
  categories: [ HideSpawnMenu ]
  components:
  - type: Gun
    fireRate: 4
    minAngle: 0
    maxAngle: 0
    projectileSpeed: 80
    selectedMode: SemiAuto
    availableModes:
    - SemiAuto
    soundGunshot:
      path: /Audio/Weapons/Guns/Gunshots/pistol.ogg

- type: entity
  parent: KsCrewVehicleLagCompSweepTestGun
  id: KsCrewVehicleLagCompSweepTestFastGun
  categories: [ HideSpawnMenu ]
  components:
  - type: Gun
    fireRate: 4
    minAngle: 0
    maxAngle: 0
    projectileSpeed: 80
    selectedMode: SemiAuto
    availableModes:
    - SemiAuto
    soundGunshot:
      path: /Audio/Weapons/Guns/Gunshots/pistol.ogg

- type: entity
  parent: KsCrewVehicleGunnerTestHull
  id: KsCrewVehicleLagCompSweepTestFastHull
  components:
  - type: KsCrewVehicleMounts
    mounts:
      main:
        prototype: KsCrewVehicleLagCompSweepTestFastGun
        offset: 0,-0.6
";

    public enum Shooter
    {
        Mounted,
        HandHeld,
    }

    /// <summary>Where the sweep starts, in tiles from the muzzle: far enough that the round is in flight.</summary>
    private const float StartDistance = 3f;

    /// <summary>How far apart the target's positions are along the line of fire.</summary>
    private const float Increment = 0.05f;

    private static readonly TimeSpan Lag = TimeSpan.FromMilliseconds(100);

    [TestCase(Shooter.HandHeld, true, true, true)]
    [TestCase(Shooter.HandHeld, false, true, true)]
    [TestCase(Shooter.Mounted, true, true, true)]
    [TestCase(Shooter.HandHeld, true, true, false)]
    [TestCase(Shooter.Mounted, false, true, false)]
    // The baseline: with no compensation the real target is hit directly.
    [TestCase(Shooter.HandHeld, true, false, true)]
    public async Task CompensatedRoundHitsAtEveryDistance(Shooter shooter, bool lying, bool compensated, bool fast)
    {
        EntityUid gun;
        EntityUid shooterUid;
        Func<Vector2> origin;
        Func<Vector2> forward;

        if (shooter == Shooter.Mounted)
        {
            HullScene scene;
            (scene, gun) = await SeatGunnerAsync(fast ? _fastHull : _hull);
            shooterUid = gun;
            var weapon = gun;
            origin = () => MuzzlePosition(weapon);
            forward = () => KsCrewVehicleHullFrame.Forward(Transform.GetWorldRotation(scene.Hull));
        }
        else
        {
            await SetCombatMode(true);
            gun = EntityUid.Invalid;
            await Server.WaitPost(() =>
            {
                gun = SEntMan.SpawnAtPosition(fast ? _fastRifle : _rifle, SComp<TransformComponent>(SPlayer).Coordinates);
                Assert.That(SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(SPlayer, gun), "precondition: holding the rifle");
            });
            shooterUid = SPlayer;
            origin = () => WorldPosition(SPlayer);
            forward = () => new Vector2(0f, -1f);
        }

        // Picking a gun up holds its fire for a moment.
        await RunTicks(TickRate);

        var misses = new List<float>();
        var step = 0f;
        var shots = 0;
        for (var distance = StartDistance; step == 0f || distance <= StartDistance + step; distance += Increment)
        {
            var mob = EntityUid.Invalid;
            var targetDistance = distance;
            await Server.WaitPost(() =>
            {
                mob = SEntMan.SpawnAtPosition(Human,
                    Transform.ToCoordinates(MapData.Grid.Owner, new MapCoordinates(origin() + forward() * targetDistance, MapId)));
                if (lying)
                    SEntMan.System<StandingStateSystem>().Down(mob);
            });
            await RunTicks(2);

            EntityUid roundUid = default;
            await Server.WaitPost(() =>
            {
                SEntMan.AddComponent<KsCrewVehicleGunnerTestHitRecorderComponent>(mob);

                var before = Rounds(gun).Select(round => round.Owner).ToHashSet();
                var shot = ShotAt(gun, OutsideAt(WorldPosition(mob)));
                shot.Target = SEntMan.GetNetEntity(mob);
                ReceiveFrom(ServerSession, shot);

                var fired = Rounds(gun).Where(round => !before.Contains(round.Owner)).ToList();
                Assert.That(fired, Has.Count.EqualTo(1), $"precondition: one round fired at {targetDistance}");
                roundUid = fired[0].Owner;

                // A step is how far the round goes in one tick: the sweep covers one.
                if (step == 0f)
                    step = SComp<PhysicsComponent>(roundUid).LinearVelocity.Length() * (float)STiming.TickPeriod.TotalSeconds;

                if (compensated)
                {
                    SEntMan.System<LagCompProjectileSystem>().CompensateProjectile(roundUid, shooterUid, Lag);
                    Assert.That(SEntMan.TryGetComponent<LagCompensatingProjectileComponent>(roundUid, out var compensating) &&
                                compensating.IgnoredRealTargets.Contains(mob),
                        "precondition: the round is compensated for the target, so only its ghost can be hit");
                }
            });
            shots++;

            // Only the shot's damage: the test grid is open to space, and the vacuum hurts the mob from nowhere.
            var hit = false;
            for (var tick = 0; tick < TickRate && !hit; tick++)
            {
                await RunTicks(1);
                await Server.WaitPost(() => hit = SComp<KsCrewVehicleGunnerTestHitRecorderComponent>(mob).Hits.Any(recorded => recorded.Origin == shooterUid));
            }

            if (!hit)
                misses.Add(targetDistance);

            await Server.WaitPost(() =>
            {
                SEntMan.DeleteEntity(mob);
                if (!SEntMan.Deleted(roundUid))
                    SEntMan.DeleteEntity(roundUid);
            });

            // The next shot waits for the gun.
            await RunTicks(TickRate / 2);
        }

        TestContext.Out.WriteLine($"sweep: {shots} shots, step {step}, misses {misses.Count}");
        Assert.That(shots, Is.GreaterThan(1), "precondition: the sweep fired at more than one distance");
        Assert.That(misses, Is.Empty,
            $"{(compensated ? "compensated" : "uncompensated")} {(fast ? "fast " : "")}{shooter} rounds passed through a {(lying ? "lying, aimed-at" : "standing")} " +
            $"target at these distances from the muzzle (step {step:0.###}): {string.Join(", ", misses.Select(miss => miss.ToString("0.00")))}");
    }
}
