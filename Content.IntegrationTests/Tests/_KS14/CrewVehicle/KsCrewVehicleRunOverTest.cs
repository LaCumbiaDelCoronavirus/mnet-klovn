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
        await HoldAsync(hull, DirectionFlag.North, TickRate * 4, CountHits);
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
        await HoldAsync(hull, DirectionFlag.North, TickRate * 4, () =>
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
    ///     Too slow to run it over, a driven hull carries a standing mob along ahead of it, unhurt and on its feet.
    /// </summary>
    [Test]
    public async Task SlowHullCarriesAStandingMobAlong()
    {
        var course = await SetUpAsync(GentleHullPrototype);
        var hull = course.Scene.Hull;
        await Server.WaitAssertion(() =>
            Assert.That(TopSpeed(hull), Is.LessThan(SComp<KsCrewVehicleRunOverComponent>(hull).MinSpeed),
                "precondition: this hull can't go fast enough to run anyone over"));

        var mobStart = Vector2.Zero;
        await Server.WaitPost(() => mobStart = HullPosition(course.Mob));

        var overlapped = false;
        await HoldAsync(hull, DirectionFlag.North, TickRate * 3, () =>
        {
            overlapped |= HardBox(hull).Intersects(HardBox(course.Mob).Enlarged(-0.05f));
            return false;
        });

        await Server.WaitAssertion(() =>
        {
            Assert.That(overlapped, Is.False, "a slow hull shouldn't drive into a standing mob");
            Assert.That(Vector2.Dot(HullPosition(course.Mob) - HullPosition(hull), course.Facing), Is.GreaterThan(0f),
                "which stays ahead of it");
            Assert.That(Vector2.Dot(HullPosition(course.Mob) - mobStart, course.Facing), Is.GreaterThan(1f),
                "carried along");
            Assert.That(Damage(course.Mob), Is.EqualTo(FixedPoint2.Zero), "unhurt");
            Assert.That(IsDown(course.Mob), Is.False, "and standing");
            Assert.That(SEntMan.HasComponent<KsCrewVehicleUnderHullComponent>(course.Mob), Is.False, "and not under it");
        });
    }

    /// <summary>
    ///     The player walks, through their own predicting client, into a hull someone is at the wheel of but not
    ///         driving: it stops them as a parked hull would, and they can walk away again.
    /// </summary>
    [Test]
    public async Task MobWalkingIntoADrivenHullIsStopped()
    {
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

    /// <summary>
    ///     Above its throw speed, a hull hurts a standing mob once and throws it along its way rather than going over it.
    ///         Control: a mob that can't be thrown is run over instead.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task FastHullThrowsAStandingMob(bool throwable)
    {
        var course = await SetUpAsync(ThrowerHullPrototype);
        var hull = course.Scene.Hull;
        await Server.WaitAssertion(() =>
        {
            Assert.That(TopSpeed(hull), Is.GreaterThan(SComp<KsCrewVehicleRunOverComponent>(hull).ThrowSpeed ?? float.PositiveInfinity),
                "precondition: this hull is fast enough to throw");
            if (!throwable)
                SEntMan.RemoveComponent<Content.Server._KS14.Throwing.KsThrowableMobComponent>(course.Mob);
        });

        var hits = 0;
        var lastDamage = FixedPoint2.Zero;
        var thrown = false;
        var under = false;
        var fastestMob = 0f;
        await HoldAsync(hull, DirectionFlag.North, TickRate * 2, () =>
        {
            var damage = Damage(course.Mob);
            if (damage > lastDamage)
                hits++;
            lastDamage = damage;
            thrown |= SEntMan.HasComponent<Content.Shared._KS14.Throwing.KsThrownSlamComponent>(course.Mob);
            under |= SEntMan.HasComponent<KsCrewVehicleUnderHullComponent>(course.Mob);
            fastestMob = MathF.Max(fastestMob, Vector2.Dot(SComp<Robust.Shared.Physics.Components.PhysicsComponent>(course.Mob).LinearVelocity, course.Facing));
            return false;
        });

        await Server.WaitAssertion(() =>
        {
            Assert.That(hits, Is.EqualTo(1), "it is hurt once");
            if (throwable)
            {
                Assert.That(thrown, Is.True, "and thrown, marked to slam into what it hits");
                Assert.That(under, Is.False, "not driven over");
                Assert.That(fastestMob, Is.GreaterThan(TopSpeed(hull)), "flying ahead faster than the hull drives");
            }
            else
            {
                Assert.That(thrown, Is.False, "control: a mob that can't be thrown isn't");
                Assert.That(under, Is.True, "it is run over instead");
            }
        });
    }

    /// <summary>
    ///     A round trip away, the driver's client still sees a carried mob held at the hull's leading edge, and a struck
    ///         one in front of it until the throw arrives: never inside the hull. Once the hull stops, the mob stops as the
    ///         same mob given the same speed would.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task MobAheadIsNeverInsideTheHullOnALaggingClient(bool struck)
    {
        var course = await SetUpAsync(struck ? ThrowerHullPrototype : GentleHullPrototype);
        var hull = course.Scene.Hull;
        EntityUid control = default;
        await Server.WaitPost(() =>
        {
            control = SEntMan.SpawnAtPosition(Human, OriginCoordinates.Offset(new Vector2(course.Facing.Y, -course.Facing.X) * 4f));
            SEntMan.RemoveComponent<BarotraumaComponent>(control);
        });
        await SimulateLatencyAsync(6);

        var clientHull = ToClient(hull);
        var clientMob = ToClient(course.Mob);
        var clientPhysics = Client.System<Robust.Shared.Physics.Systems.SharedPhysicsSystem>();
        var worstOverlap = 0f;
        var thrownAway = false;
        await SetMovementKey(DirectionFlag.North, Robust.Shared.Input.BoundKeyState.Down);
        for (var tick = 0; tick < TickRate * 3 / 2 && !thrownAway; tick++)
        {
            await RunTicks(1);
            await Client.WaitPost(() =>
            {
                var hullBox = clientPhysics.GetHardAABB(clientHull);
                var mobBox = clientPhysics.GetHardAABB(clientMob);
                if (hullBox.Intersects(mobBox))
                    worstOverlap = MathF.Max(worstOverlap, hullBox.Intersect(mobBox).Size.Length());
                thrownAway = struck && Vector2.Dot(CEntMan.GetComponent<Robust.Shared.Physics.Components.PhysicsComponent>(clientMob).LinearVelocity, course.Facing) >
                    Vector2.Dot(CEntMan.GetComponent<Robust.Shared.Physics.Components.PhysicsComponent>(clientHull).LinearVelocity, course.Facing) + 1f;
            });
        }

        await SetMovementKey(DirectionFlag.North, Robust.Shared.Input.BoundKeyState.Up);
        Assert.That(worstOverlap, Is.LessThan(0.02f), "the driver's client should never show the mob inside the hull");
        if (struck)
            return;

        // From when the hull has stopped: how far the mob goes on, against the control given its speed then.
        for (var tick = 0; tick < TickRate * 3; tick++)
        {
            await RunTicks(1);
            var stopped = false;
            await Server.WaitPost(() => stopped = HullVelocity(hull).Length() < 0.05f);
            if (stopped)
                break;
        }

        var mobStart = Vector2.Zero;
        var controlStart = Vector2.Zero;
        await Server.WaitPost(() =>
        {
            mobStart = Transform.GetWorldPosition(course.Mob);
            controlStart = Transform.GetWorldPosition(control);
            SEntMan.System<Robust.Shared.Physics.Systems.SharedPhysicsSystem>().SetLinearVelocity(control,
                SComp<Robust.Shared.Physics.Components.PhysicsComponent>(course.Mob).LinearVelocity);
        });
        await RunTicks(TickRate * 2);
        await Server.WaitAssertion(() =>
        {
            var slid = (Transform.GetWorldPosition(course.Mob) - mobStart).Length();
            var controlSlid = (Transform.GetWorldPosition(control) - controlStart).Length();
            Assert.That(slid, Is.LessThanOrEqualTo(controlSlid + 0.05f), "with the hull stopped, the mob stops as any mob does");
        });
    }

    /// <summary>
    ///     A thrown mob whose flight is cut short (by a wall, here by stopping it) is down in front of the hull while it is
    ///         still struck: the hull must drive on over it, not carry it along on its front.
    /// </summary>
    [TestCase(0)]
    [TestCase(6)]
    public async Task ThrownMobIsNotCarriedOnceItsThrowHasEnded(int latencyTicks)
    {
        var course = await SetUpAsync(ThrowerHullPrototype);
        var hull = course.Scene.Hull;
        if (latencyTicks > 0)
            await SimulateLatencyAsync(latencyTicks);

        var struckAt = Vector2.Zero;
        var stopped = false;
        await SetMovementKey(DirectionFlag.North, Robust.Shared.Input.BoundKeyState.Down);
        for (var tick = 0; tick < TickRate * 3 && !stopped; tick++)
        {
            await RunTicks(1);
            await Server.WaitPost(() =>
            {
                if (!SEntMan.HasComponent<KsCrewVehicleStruckComponent>(course.Mob))
                    return;

                if (struckAt == Vector2.Zero)
                    struckAt = HullPosition(course.Mob);

                // A short flight, ended where it is, as a wall would.
                if (Vector2.Dot(HullPosition(course.Mob) - struckAt, course.Facing) > 0.4f)
                {
                    SEntMan.System<Robust.Shared.Physics.Systems.SharedPhysicsSystem>().SetLinearVelocity(course.Mob, Vector2.Zero);
                    stopped = true;
                }
            });
        }

        var stoppedAt = Vector2.Zero;
        await Server.WaitPost(() => stoppedAt = HullPosition(course.Mob));
        var carriedFurthest = 0f;
        for (var tick = 0; tick < TickRate / 2; tick++)
        {
            await RunTicks(1);
            await Server.WaitPost(() =>
            {
                if (SEntMan.TryGetComponent<KsCrewVehicleStruckComponent>(course.Mob, out var struckComponent) && struckComponent.Launched)
                    carriedFurthest = MathF.Max(carriedFurthest, Vector2.Dot(HullPosition(course.Mob) - stoppedAt, course.Facing));
            });
        }

        await SetMovementKey(DirectionFlag.North, Robust.Shared.Input.BoundKeyState.Up);
        Assert.That(stopped, Is.True, "precondition: struck, thrown, and its flight ended");
        Assert.That(carriedFurthest, Is.LessThan(0.3f), "once its throw has ended, the hull shouldn't carry it on its front");
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
