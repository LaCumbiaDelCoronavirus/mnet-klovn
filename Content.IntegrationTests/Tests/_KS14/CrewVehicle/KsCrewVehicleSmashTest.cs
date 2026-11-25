#nullable enable
using System;
using System.Numerics;
using System.Threading.Tasks;
using Content.Shared._KS14.CrewVehicle;
using Content.Shared._KS14.CrewVehicle.CrashLockout;
using Content.Shared._KS14.CrewVehicle.Smash;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Movement.Components;
using Content.Shared.Whitelist;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.CrewVehicle;

/// <summary>
///     A hull driven fast into an obstacle: a smashable one breaks, a wall is crashed into and stalls the hull, anything
///         else is just hit. Every number comes from the test hulls' prototypes.
/// </summary>
[TestOf(typeof(KsCrewVehicleSmashSystem))]
[TestOf(typeof(KsCrewVehicleCrashLockoutSystem))]
public sealed class KsCrewVehicleSmashTest : KsCrewVehicleDriveTestBase
{
    private readonly EntProtoId _hullFromBase = "KsCrewVehicleSmashTestHullFromBase";
    private static readonly EntProtoId AirlockPrototype = "Airlock";
    private static readonly EntProtoId Window = "Window";
    private static readonly EntProtoId Grille = "Grille";
    private static readonly EntProtoId Table = "Table";
    private static readonly EntProtoId Wall = "WallSolid";

    // The shipped hull base's data, through a child: the base itself is abstract.
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: KsCrewVehicleHullBase
  id: KsCrewVehicleSmashTestHullFromBase
";
    private sealed record Run(HullScene Scene, EntityUid Obstacle, Vector2 Facing);

    /// <summary>
    ///     A hull at the origin with the player at the wheel, and <paramref name="obstacle"/> four tiles ahead.
    /// </summary>
    private async Task<Run> SetUpAsync(EntProtoId obstacle, EntProtoId? hullPrototype = null)
    {
        var scene = await SpawnHullAsync(Vector2.Zero, hullPrototype);
        await PutInsideAsync(scene);
        await SitAsync(scene.DriverSeat);

        EntityUid obstacleUid = default;
        var facing = Vector2.Zero;
        await Server.WaitPost(() =>
        {
            facing = KsCrewVehicleHullFrame.Forward(HullRotation(scene.Hull));
            obstacleUid = SEntMan.SpawnAtPosition(obstacle, OriginCoordinates.Offset(facing * 4f));
        });
        await RunTicks(2);
        return new Run(scene, obstacleUid, facing);
    }

    private FixedPoint2 Damage(EntityUid uid) => SEntMan.System<DamageableSystem>().GetTotalDamage(uid);

    private bool CanMove(EntityUid hull) => SComp<InputMoverComponent>(hull).CanMove;

    [Test]
    public async Task FastHullSmashesThroughAWhitelistedObstacle()
    {
        var run = await SetUpAsync(GlassPrototype);
        var hull = run.Scene.Hull;
        var smashed = false;
        var speedAtImpact = 0f;
        var slowest = float.MaxValue;
        await HoldAsync(hull, DirectionFlag.North, TickRate * 3, () =>
        {
            // The tick it breaks is the tick it was hit: the speed then is already the one it kept.
            var speed = HullVelocity(hull).Length();
            smashed |= SEntMan.Deleted(run.Obstacle) || EntityManagerQueued(run.Obstacle);
            if (smashed)
                slowest = MathF.Min(slowest, speed);
            else
                speedAtImpact = MathF.Max(speedAtImpact, speed);

            return false;
        });

        await Server.WaitAssertion(() =>
        {
            var smash = SComp<KsCrewVehicleSmashComponent>(hull);
            Assert.That(speedAtImpact, Is.GreaterThanOrEqualTo(smash.MinSpeed), "precondition: going fast enough");
            Assert.That(smashed, Is.True, "a whitelisted obstacle hit fast enough should break");
            Assert.That(slowest, Is.LessThanOrEqualTo(speedAtImpact * smash.SmashSpeedRetained + 0.05f), "the hull losing speed");
            Assert.That(Damage(hull), Is.GreaterThan(FixedPoint2.Zero), "and taking damage");
            Assert.That(Vector2.Dot(HullPosition(hull), run.Facing), Is.GreaterThan(4f), "and driving on through");
        });
    }

    /// <summary>
    ///     On the driver's own client, the hull drives straight on through what it smashes: it never steps backward,
    ///         though that client hears the window broke only a round trip later.
    /// </summary>
    [Test]
    public async Task SmashingNeverStepsBackOnTheDriversClient()
    {
        var run = await SetUpAsync(GlassPrototype);
        var clientHull = ToClient(run.Scene.Hull);
        float ClientAlong() => Vector2.Dot(CEntMan.GetComponent<TransformComponent>(clientHull).LocalPosition, run.Facing);

        // Up to speed first: the smash is what this watches.
        await SetMovementKey(DirectionFlag.North, Robust.Shared.Input.BoundKeyState.Down);
        await RunTicks(TickRate / 2);
        var previous = 0f;
        await Client.WaitPost(() => previous = ClientAlong());
        var worstStepBack = 0f;
        var longestStep = 0f;
        for (var tick = 0; tick < TickRate * 3; tick++)
        {
            await RunTicks(1);
            await Client.WaitPost(() =>
            {
                var along = ClientAlong();
                worstStepBack = MathF.Max(worstStepBack, previous - along);
                longestStep = MathF.Max(longestStep, along - previous);
                previous = along;
            });
        }

        await SetMovementKey(DirectionFlag.North, Robust.Shared.Input.BoundKeyState.Up);
        var topStep = 0f;
        await Server.WaitPost(() => topStep = TopSpeed(run.Scene.Hull) / TickRate);
        TestContext.Out.WriteLine($"worst client step back {worstStepBack}, longest step {longestStep} of {topStep} a tick");
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.Deleted(run.Obstacle) || EntityManagerQueued(run.Obstacle), Is.True, "precondition: smashed");
            Assert.That(Vector2.Dot(HullPosition(run.Scene.Hull), run.Facing), Is.GreaterThan(4f), "precondition: and through");
        });
        // Stopped at the window until the server's word, the hull steps back out of it, then jumps on to catch up.
        Assert.Multiple(() =>
        {
            Assert.That(worstStepBack, Is.LessThan(0.001f), "the driver's client shouldn't see the hull step backward");
            Assert.That(longestStep, Is.LessThan(topStep * 1.05f), "nor jump forward to catch up");
        });
    }

    /// <summary>
    ///     The same, a round trip away: the driver's client replays the smash from older states as they come in.
    /// </summary>
    [TestCase(3)]
    [TestCase(6)]
    public async Task SmashingNeverStepsBackOnALaggingDriversClient(int latencyTicks)
    {
        var run = await SetUpAsync(GlassPrototype);
        await SimulateLatencyAsync(latencyTicks);
        var clientHull = ToClient(run.Scene.Hull);
        float ClientAlong() => Vector2.Dot(CEntMan.GetComponent<TransformComponent>(clientHull).LocalPosition, run.Facing);

        await SetMovementKey(DirectionFlag.North, Robust.Shared.Input.BoundKeyState.Down);
        var previous = 0f;
        await Client.WaitPost(() => previous = ClientAlong());
        var worstStepBack = 0f;
        for (var tick = 0; tick < TickRate * 3; tick++)
        {
            await RunTicks(1);
            await Client.WaitPost(() =>
            {
                var along = ClientAlong();
                worstStepBack = MathF.Max(worstStepBack, previous - along);
                previous = along;
            });
        }

        await SetMovementKey(DirectionFlag.North, Robust.Shared.Input.BoundKeyState.Up);
        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.Deleted(run.Obstacle) || EntityManagerQueued(run.Obstacle), Is.True, "precondition: smashed"));
        Assert.That(worstStepBack, Is.LessThan(0.001f), "a lagging driver's client shouldn't see the hull step backward");
    }

    [Test]
    public async Task GentleHullBreaksNothing()
    {
        var run = await SetUpAsync(GlassPrototype, GentleHullPrototype);
        await HoldAsync(run.Scene.Hull, DirectionFlag.North, TickRate * 3);
        await Server.WaitAssertion(() =>
        {
            Assert.That(TopSpeed(run.Scene.Hull), Is.LessThan(SComp<KsCrewVehicleSmashComponent>(run.Scene.Hull).MinSpeed),
                "precondition: this hull can't go fast enough");
            Assert.That(SEntMan.Deleted(run.Obstacle), Is.False, "too slow, nothing breaks");
            Assert.That(Damage(run.Obstacle), Is.EqualTo(FixedPoint2.Zero), "nor is damaged");
            Assert.That(Damage(run.Scene.Hull), Is.EqualTo(FixedPoint2.Zero), "nor the hull");
            Assert.That(CanMove(run.Scene.Hull), Is.True, "and a bump doesn't stall it");
        });
    }

    [Test]
    public async Task OtherObstaclesNeverBreak()
    {
        var run = await SetUpAsync(BlockPrototype);
        await HoldAsync(run.Scene.Hull, DirectionFlag.North, TickRate * 3);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.Deleted(run.Obstacle), Is.False, "an obstacle on neither list isn't broken");
            Assert.That(Damage(run.Obstacle), Is.EqualTo(FixedPoint2.Zero), "nor damaged");
        });
    }

    /// <summary>
    ///     A hard crash into a wall: the wall is damaged but stands, the hull stops, is damaged and stalls for its
    ///         lockout; then it drives again.
    /// </summary>
    [Test]
    public async Task WallCrashStallsTheHull()
    {
        var run = await SetUpAsync(TestWallPrototype);
        var hull = run.Scene.Hull;
        var stalledAt = TimeSpan.Zero;
        await HoldAsync(hull, DirectionFlag.North, TickRate * 3, () =>
        {
            if (!CanMove(hull))
                stalledAt = STiming.CurTime;
            return !CanMove(hull);
        });

        await Server.WaitAssertion(() =>
        {
            Assert.That(stalledAt, Is.Not.EqualTo(TimeSpan.Zero), "a hard crash into a wall should stall the hull");
            Assert.That(SEntMan.Deleted(run.Obstacle), Is.False, "the wall stands");
            Assert.That(Damage(run.Obstacle), Is.GreaterThan(FixedPoint2.Zero), "damaged");
            Assert.That(Damage(hull), Is.GreaterThan(FixedPoint2.Zero), "and so is the hull");
        });

        var duration = TimeSpan.Zero;
        await Server.WaitPost(() => duration = SComp<KsCrewVehicleCrashLockoutComponent>(hull).Duration);
        var free = false;
        var freeAt = TimeSpan.Zero;
        for (var tick = 0; tick < (int)(duration.TotalSeconds * TickRate) + TickRate * 2 && !free; tick++)
        {
            await RunTicks(1);
            await Server.WaitPost(() =>
            {
                free = CanMove(hull);
                freeAt = STiming.CurTime;
            });
        }

        Assert.That(free, Is.True, "the stall should end");
        Assert.That(freeAt - stalledAt, Is.GreaterThanOrEqualTo(duration - STiming.TickPeriod),
            "after the lockout's whole duration");

        // And it drives again: backing away from the wall.
        var before = Vector2.Zero;
        await Server.WaitPost(() => before = HullPosition(hull));
        await HoldAsync(hull, DirectionFlag.South, TickRate);
        await Server.WaitAssertion(() =>
            Assert.That(Vector2.Dot(HullPosition(hull) - before, -run.Facing), Is.GreaterThan(0.1f), "it drives again afterwards"));
    }

    /// <summary>
    ///     The stall ends on the driver's own client when it ends, not when that client hears from the server.
    /// </summary>
    [Test]
    public async Task StallEndsOnTheDriversClientWhenItEnds()
    {
        var run = await SetUpAsync(TestWallPrototype);
        var hull = run.Scene.Hull;
        await HoldAsync(hull, DirectionFlag.North, TickRate * 3, () => !CanMove(hull));

        var until = TimeSpan.Zero;
        await Server.WaitAssertion(() =>
        {
            Assert.That(CanMove(hull), Is.False, "precondition: stalled");
            until = SComp<KsCrewVehicleCrashLockedComponent>(hull).Until;
        });

        // Each client tick past the stall's end, whether its hull may drive again.
        var clientHull = ToClient(hull);
        var lateTicks = 0;
        var sampled = 0;
        while (sampled < TickRate / 2)
        {
            await RunTicks(1);
            await Client.WaitPost(() =>
            {
                if (CTiming.CurTime <= until + CTiming.TickPeriod)
                    return;

                sampled++;
                if (!CEntMan.GetComponent<InputMoverComponent>(clientHull).CanMove)
                    lateTicks++;
            });
        }

        Assert.That(lateTicks, Is.Zero, "past the stall's end, the driver's client should let the hull drive at once");
    }

    [Test]
    public async Task GentleBumpIntoAWallDoesNotStall()
    {
        var run = await SetUpAsync(TestWallPrototype, GentleHullPrototype);
        var everStalled = false;
        await HoldAsync(run.Scene.Hull, DirectionFlag.North, TickRate * 3, () =>
        {
            everStalled |= !CanMove(run.Scene.Hull);
            return false;
        });

        await Server.WaitAssertion(() =>
        {
            Assert.That(everStalled, Is.False, "a gentle bump shouldn't stall the hull");
            Assert.That(Damage(run.Obstacle), Is.EqualTo(FixedPoint2.Zero), "nor damage the wall");
        });
    }

    /// <summary>
    ///     The shipped hulls' lists: windows, grilles and tables are smashed, walls crashed into, and doors neither:
    ///         they are bumped open.
    /// </summary>
    [Test]
    public async Task HullBaseSmashesTheRightThings()
    {
        await Server.WaitAssertion(() =>
        {
            Assert.That(ProtoMan.Index(_hullFromBase).TryComp<KsCrewVehicleSmashComponent>(out var smash, Factory), Is.True);
            var whitelistSystem = SEntMan.System<EntityWhitelistSystem>();

            foreach (var id in new[] { Window, Grille, Table, Wall, AirlockPrototype })
            {
                var uid = SEntMan.SpawnAtPosition(id, OriginCoordinates.Offset(new Vector2(6f, 6f)));
                var smashable = whitelistSystem.IsWhitelistPass(smash!.Smashable, uid);
                var wall = whitelistSystem.IsWhitelistPass(smash.Walls, uid);
                SEntMan.DeleteEntity(uid);

                if (id == Wall)
                    Assert.That(wall && !smashable, Is.True, "walls are crashed into");
                else if (id == AirlockPrototype)
                    Assert.That(smashable || wall, Is.False, "doors are bumped open, never smashed");
                else
                    Assert.That(smashable, Is.True, $"{id} is smashed through");
            }
        });
    }

    private bool EntityManagerQueued(EntityUid uid) => SEntMan is EntityManager entityManager && entityManager.IsQueuedForDeletion(uid);
}
