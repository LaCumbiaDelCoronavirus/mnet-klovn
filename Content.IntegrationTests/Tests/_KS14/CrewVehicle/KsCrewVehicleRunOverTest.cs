#nullable enable
using System;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server.Atmos.Components;
using Content.Server._KS14.NPC.Pushing;
using Content.Shared._KS14.CrewVehicle;
using Content.Shared._KS14.CrewVehicle.RunOver;
using Content.Shared.FixedPoint;
using Content.Shared.Standing;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._KS14.CrewVehicle;

/// <summary>
///     Running mobs over: once, fast enough, standing; a slower hull is stopped by them. Every number comes from the
///         test hulls' prototypes.
/// </summary>
[TestOf(typeof(KsCrewVehicleRunOverSystem))]
[TestOf(typeof(KsCrewVehicleRunOverController))]
public sealed class KsCrewVehicleRunOverTest : KsCrewVehicleDriveTestBase
{
    private sealed record Course(HullScene Scene, EntityUid Mob, Vector2 Facing);

    /// <summary>
    ///     A hull parked at the origin with the player at the wheel, and a standing mob four tiles ahead of it.
    /// </summary>
    private async Task<Course> SetUpAsync(Robust.Shared.Prototypes.EntProtoId? hullPrototype = null)
    {
        await SetTankControlsAsync(false);
        var scene = await SpawnHullAsync(Vector2.Zero, hullPrototype);
        await PutInsideAsync(scene);
        await SitAsync(scene.DriverSeat);

        EntityUid mob = default;
        var facing = Vector2.Zero;
        await Server.WaitPost(() =>
        {
            facing = KsCrewVehicleHullFrame.Forward(HullRotation(scene.Hull));
            mob = SEntMan.SpawnAtPosition(Human, OriginCoordinates.Offset(facing * 4f));
            // Only the hull hurts it: not the open test grid's air.
            SEntMan.RemoveComponent<BarotraumaComponent>(mob);
        });
        await RunTicks(5);
        return new Course(scene, mob, facing);
    }

    private FixedPoint2 Damage(EntityUid uid) => SEntMan.System<Content.Shared.Damage.Systems.DamageableSystem>().GetTotalDamage(uid);

    private bool IsDown(EntityUid uid) => SEntMan.System<StandingStateSystem>().IsDown(uid);

    private bool IsUnder(EntityUid mob, EntityUid hull) => SEntMan.System<KsCrewVehicleRunOverSystem>().IsUnder(mob, hull);

    [Test]
    public async Task FastHullRunsAStandingMobOverOnce()
    {
        var course = await SetUpAsync();
        var hull = course.Scene.Hull;
        KsCrewVehicleRunOverComponent runOver = default!;
        await Server.WaitAssertion(() =>
        {
            runOver = SComp<KsCrewVehicleRunOverComponent>(hull);
            Assert.That(TopSpeed(hull), Is.GreaterThan(runOver.MinSpeed), "precondition: this hull is fast enough");
        });

        var hits = 0;
        var lastDamage = FixedPoint2.Zero;
        var playerDamage = FixedPoint2.Zero;
        await Server.WaitPost(() => playerDamage = Damage(SPlayer));

        bool CountHits()
        {
            var damage = Damage(course.Mob);
            if (damage > lastDamage)
                hits++;
            lastDamage = damage;
            return IsUnder(course.Mob, hull);
        }

        // Up to it, until it is under the hull; then stop on top of it.
        await HoldAsync(hull, DirectionFor(course.Facing), TickRate * 4, CountHits);
        await SettleAsync(hull);
        await Server.WaitAssertion(() =>
        {
            CountHits();
            Assert.That(IsUnder(course.Mob, hull), Is.True, "the mob should be under the hull");
            Assert.That(hits, Is.EqualTo(1), "it should be hurt once");
            Assert.That(IsDown(course.Mob), Is.True, "and knocked down");
        });

        // Parked on top of it for longer than the knockdown: still down, and not hit again.
        for (var tick = 0; tick < (int)(runOver.Knockdown.TotalSeconds * TickRate) + TickRate; tick++)
        {
            await RunTicks(1);
            await Server.WaitPost(() => CountHits());
        }

        await Server.WaitAssertion(() =>
        {
            Assert.That(hits, Is.EqualTo(1), "a mob still under the hull isn't hurt twice");
            Assert.That(IsDown(course.Mob), Is.True, "and stays down while the hull is over it");
        });

        // Drive on until the hull has left it; then it gets up.
        await HoldAsync(hull, DirectionFor(course.Facing), TickRate * 4, () =>
        {
            CountHits();
            return !SEntMan.HasComponent<KsCrewVehicleUnderHullComponent>(course.Mob);
        });

        var stood = false;
        for (var tick = 0; tick < (int)(runOver.Knockdown.TotalSeconds * TickRate) + TickRate * 5 && !stood; tick++)
        {
            await RunTicks(1);
            await Server.WaitPost(() =>
            {
                CountHits();
                stood = !IsDown(course.Mob);
            });
        }

        await Server.WaitAssertion(() =>
        {
            Assert.That(stood, Is.True, "once the hull has left it, the mob gets back up");
            Assert.That(hits, Is.EqualTo(1), "having been hurt only the once");
            // Not equal: they heal what the open test grid's air did to them before they got in.
            Assert.That(Damage(SPlayer), Is.LessThanOrEqualTo(playerDamage), "the driver, inside, is never hurt");
            Assert.That(SEntMan.HasComponent<KsCrewVehicleUnderHullComponent>(SPlayer), Is.False, "nor driven over");
        });
    }

    /// <summary>
    ///     Too slow to run it over, the driven hull is stopped by a standing mob: it doesn't pass into it, nor hurt it,
    ///         nor push it.
    /// </summary>
    [Test]
    public async Task SlowHullIsStoppedByAStandingMob()
    {
        var course = await SetUpAsync(GentleHullPrototype);
        var hull = course.Scene.Hull;
        await Server.WaitAssertion(() =>
            Assert.That(TopSpeed(hull), Is.LessThan(SComp<KsCrewVehicleRunOverComponent>(hull).MinSpeed),
                "precondition: this hull can't go fast enough to run anyone over"));

        var mobStart = Vector2.Zero;
        await Server.WaitPost(() => mobStart = HullPosition(course.Mob));

        var overlapped = false;
        await HoldAsync(hull, DirectionFor(course.Facing), TickRate * 3, () =>
        {
            overlapped |= HardBox(hull).Intersects(HardBox(course.Mob).Enlarged(-0.05f));
            return false;
        });

        await Server.WaitAssertion(() =>
        {
            Assert.That(overlapped, Is.False, "a slow hull should stop at a standing mob, not drive into it");
            Assert.That(Vector2.Dot(HullPosition(course.Mob) - HullPosition(hull), course.Facing), Is.GreaterThan(0f),
                "and stay on its side of it");
            Assert.That(Damage(course.Mob), Is.EqualTo(FixedPoint2.Zero), "unhurt");
            Assert.That(IsDown(course.Mob), Is.False, "and standing");
            Assert.That((HullPosition(course.Mob) - mobStart).Length(), Is.LessThan(0.1f), "and not pushed");
        });
    }

    /// <summary>
    ///     The player walks, through their own predicting client, into a hull someone is at the wheel of but not
    ///         driving: it stops them as a parked hull would, and they can walk away again.
    /// </summary>
    [Test]
    public async Task MobWalkingIntoADrivenHullIsStopped()
    {
        await SetTankControlsAsync(false);
        var scene = await SpawnHullAsync(new Vector2(3f, 0f));
        EntityUid driver = default;
        await Server.WaitPost(() =>
            driver = SEntMan.SpawnAtPosition(Human, SComp<TransformComponent>(scene.DriverSeat).Coordinates));
        await RunTicks(5);
        await SitAsync(scene.DriverSeat, driver);
        await Server.WaitAssertion(() =>
            Assert.That(SComp<Robust.Shared.Physics.Components.PhysicsComponent>(scene.Hull).BodyType,
                Is.EqualTo(Robust.Shared.Physics.BodyType.KinematicController), "precondition: the hull is driven"));

        var overlapped = false;
        await SetMovementKey(DirectionFlag.East, BoundKeyState.Down);
        for (var tick = 0; tick < TickRate * 2; tick++)
        {
            await RunTicks(1);
            await Server.WaitPost(() => overlapped |= HardBox(SPlayer).Intersects(HardBox(scene.Hull).Enlarged(-0.05f)));
        }

        await SetMovementKey(DirectionFlag.East, BoundKeyState.Up);
        var stoppedAt = Vector2.Zero;
        await Server.WaitAssertion(() =>
        {
            Assert.That(overlapped, Is.False, "walking into a driven hull, a mob shouldn't get inside it");
            Assert.That(HullPosition(SPlayer).X, Is.LessThan(HullPosition(scene.Hull).X), "nor past it");
            stoppedAt = HullPosition(SPlayer);
        });

        await Move(DirectionFlag.West, 0.5f);
        await Server.WaitAssertion(() =>
            Assert.That(HullPosition(SPlayer).X, Is.LessThan(stoppedAt.X - 0.5f), "and it can walk away from it"));
    }

    [Test]
    public async Task NpcsDontPushAParkedHull()
    {
        var scene = await SpawnHullAsync(Vector2.Zero);
        EntityUid crate = default;
        await Server.WaitPost(() => crate = SEntMan.SpawnAtPosition(CratePrototype, OriginCoordinates.Offset(new Vector2(3f, 3f))));
        await RunTicks(2);

        await Server.WaitAssertion(() =>
        {
            var pushSystem = SEntMan.System<NpcPushSystem>();
            Assert.That(pushSystem.IsPushable(crate, null), Is.True, "control: a loose crate is pushable");
            Assert.That(pushSystem.IsPushable(scene.Hull, null), Is.False, "a parked hull isn't");
        });
    }
}
