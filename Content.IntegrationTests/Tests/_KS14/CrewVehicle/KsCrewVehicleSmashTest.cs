#nullable enable
using System;
using System.Numerics;
using System.Threading.Tasks;
using Content.Shared._KS14.CrewVehicle;
using Content.Shared._KS14.CrewVehicle.CrashLockout;
using Content.Shared._KS14.CrewVehicle.Smash;
using Content.Shared.Damage.Systems;
using Content.Shared.Destructible;
using Content.Shared.FixedPoint;
using Content.Shared.Movement.Components;
using Content.Shared.Whitelist;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.CrewVehicle;

/// <summary>
///     A hull driven fast into an obstacle: anything solid takes the hull's smash damage and breaks, letting it through,
///         or stands and is crashed into; a wall is crashed into at once and stalls the hull; a loose body is pushed.
///         Every number comes from the test hulls' prototypes.
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
    private static readonly EntProtoId SteelChair = "Chair";
    private static readonly EntProtoId Humvee = "KsCrewVehicleHumvee";

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
    private async Task<Run> SetUpAsync(EntProtoId obstacle, EntProtoId? hullPrototype = null, float distance = 4f)
    {
        var scene = await SpawnHullAsync(Vector2.Zero, hullPrototype);
        await PutInsideAsync(scene);
        await SitAsync(scene.DriverSeat);

        EntityUid obstacleUid = default;
        var facing = Vector2.Zero;
        await Server.WaitPost(() =>
        {
            facing = KsCrewVehicleHullFrame.Forward(HullRotation(scene.Hull));
            obstacleUid = SEntMan.SpawnAtPosition(obstacle, OriginCoordinates.Offset(facing * distance));
        });
        await RunTicks(2);
        return new Run(scene, obstacleUid, facing);
    }

    private FixedPoint2 Damage(EntityUid uid) => SEntMan.System<DamageableSystem>().GetTotalDamage(uid);

    private bool CanMove(EntityUid hull) => SComp<InputMoverComponent>(hull).CanMove;

    [Test]
    public async Task FastHullSmashesThroughAWhitelistedObstacle()
    {
        var run = await SetUpAsync(BlockPrototype);
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
        var run = await SetUpAsync(BlockPrototype);
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
        var run = await SetUpAsync(BlockPrototype);
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

    /// <summary>
    ///     Two hulls smash the same thing, one after the other: both pass through it, the first as well as the second.
    ///         Control: a hull that smashed nothing, put into it alike, is pushed back out.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task BothHullsPassThroughWhatTheySmashed(bool smashed)
    {
        var first = await SpawnHullAsync(new Vector2(-6f, 0f));
        var second = await SpawnHullAsync(new Vector2(6f, 0f));
        EntityUid window = default;
        var windowPosition = Vector2.Zero;
        await Server.WaitPost(() =>
        {
            window = SEntMan.SpawnAtPosition(BlockPrototype, OriginCoordinates.Offset(new Vector2(0f, 6f)));
            var smashedSystem = SEntMan.System<KsCrewVehicleSmashedSystem>();
            var passTime = SComp<KsCrewVehicleSmashComponent>(first.Hull).SmashedPassTime;
            if (smashed)
                smashedSystem.MarkSmashed(window, first.Hull, passTime);
            smashedSystem.MarkSmashed(window, second.Hull, passTime);
        });
        await RunTicks(1);
        await Server.WaitPost(() =>
        {
            windowPosition = HullPosition(window);
            Transform.SetLocalPosition(first.Hull, windowPosition);
            // A sleeping body moved keeps its old contacts: awake, it finds what it now overlaps.
            SEntMan.System<Robust.Shared.Physics.Systems.SharedPhysicsSystem>().WakeBody(first.Hull);
        });
        await RunTicks(TickRate / 4);
        await Server.WaitAssertion(() =>
        {
            var moved = (HullPosition(first.Hull) - windowPosition).Length();
            if (smashed)
                Assert.That(moved, Is.LessThan(0.01f), "the first hull should still pass through what it smashed");
            else
                Assert.That(moved, Is.GreaterThan(0.05f), "control: a hull that smashed nothing is pushed out of it");
        });
    }

    [Test]
    public async Task GentleHullBreaksNothing()
    {
        var run = await SetUpAsync(BlockPrototype, GentleHullPrototype);
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

    /// <summary>
    ///     What isn't a wall or a loose body is no longer left alone: a weak obstacle on no list at all, anchored and
    ///         (as a steel chair does) colliding only through its mask, is broken and driven through, slower.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task UnlistedWeakObstacleIsSmashed(bool chairLike)
    {
        var run = await SetUpAsync(chairLike ? ChairLikePrototype : BlockPrototype);
        var hull = run.Scene.Hull;
        var smashed = false;
        var speedAtImpact = 0f;
        var slowest = float.MaxValue;
        var everStalled = false;
        await HoldAsync(hull, DirectionFlag.North, TickRate * 3, () =>
        {
            var speed = HullVelocity(hull).Length();
            everStalled |= !CanMove(hull);
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
            Assert.That(smashed, Is.True, "an obstacle on no list, weaker than the smash damage, breaks");
            Assert.That(slowest, Is.LessThanOrEqualTo(speedAtImpact * smash.SmashSpeedRetained + 0.05f), "the hull losing speed");
            Assert.That(Vector2.Dot(HullPosition(hull), run.Facing), Is.GreaterThan(4f), "and driving on through");
            Assert.That(Damage(hull), Is.GreaterThan(FixedPoint2.Zero), "at a cost to itself");
            Assert.That(everStalled, Is.False, "smashing through is not a crash: no stall");
        });
    }

    /// <summary>
    ///     An obstacle sturdier than one hit: damaged, still standing, the hull stopped, damaged and, the impact being
    ///         hard, stalled. Backing off and ramming again breaks it after as many hits as its <c>Destructible</c> and
    ///         the smash damage say.
    /// </summary>
    [Test]
    public async Task UnlistedStrongObstacleIsCrashedInto()
    {
        var run = await SetUpAsync(StrongBlockPrototype);
        var hull = run.Scene.Hull;
        var smashDamage = FixedPoint2.Zero;
        var destroyedAt = FixedPoint2.Zero;
        await Server.WaitPost(() =>
        {
            var smashSpecifier = SComp<KsCrewVehicleSmashComponent>(hull).SmashDamage;
            smashDamage = smashSpecifier.GetTotal();
            destroyedAt = SEntMan.System<SharedDestructibleSystem>().DestroyedAt(run.Obstacle);
        });
        // The block has no modifier set, so what the hit deals is the smash damage itself.
        var hits = (int)Math.Ceiling((float)destroyedAt / (float)smashDamage);
        Assert.That(hits, Is.GreaterThan(1), "precondition: it takes more than one hit");

        for (var hit = 1; hit <= hits; hit++)
        {
            await HoldAsync(hull, DirectionFlag.North, TickRate * 3, () => !CanMove(hull) || SEntMan.Deleted(run.Obstacle));
            await Server.WaitAssertion(() =>
            {
                if (hit < hits)
                {
                    Assert.That(SEntMan.Deleted(run.Obstacle) || EntityManagerQueued(run.Obstacle), Is.False, $"hit {hit}: it stands");
                    Assert.That(Damage(run.Obstacle), Is.GreaterThan(FixedPoint2.Zero), $"hit {hit}: damaged");
                    Assert.That(CanMove(hull), Is.False, $"hit {hit}: the hull is stalled");
                    Assert.That(Damage(hull), Is.GreaterThan(FixedPoint2.Zero), $"hit {hit}: and damaged");
                }
                else
                {
                    Assert.That(SEntMan.Deleted(run.Obstacle) || EntityManagerQueued(run.Obstacle), Is.True, $"hit {hit}: it breaks");
                }
            });

            if (hit == hits)
                break;

            // Wait out the stall and back off to ram again from speed.
            var lockout = TimeSpan.Zero;
            await Server.WaitPost(() => lockout = SComp<KsCrewVehicleCrashLockoutComponent>(hull).Duration);
            await RunTicks((int)(lockout.TotalSeconds * TickRate) + TickRate / 2);
            await HoldAsync(hull, DirectionFlag.South, TickRate * 2);
        }
    }

    /// <summary>
    ///     A hard Static thing nothing can damage is still crashed into: the hull is damaged and stalls, and nothing
    ///         logs an error.
    /// </summary>
    [Test]
    public async Task IndestructibleObstacleIsCrashedInto()
    {
        var run = await SetUpAsync(IndestructibleBlockPrototype);
        var hull = run.Scene.Hull;
        await HoldAsync(hull, DirectionFlag.North, TickRate * 3, () => !CanMove(hull));
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.Deleted(run.Obstacle), Is.False, "it stands");
            Assert.That(CanMove(hull), Is.False, "the hull stalls");
            Assert.That(Damage(hull), Is.GreaterThan(FixedPoint2.Zero), "and is damaged");
        });
    }

    /// <summary>
    ///     A loose body is the push controller's however weak: pushed along, never damaged.
    /// </summary>
    [Test]
    public async Task LooseObstacleIsPushedNotSmashed()
    {
        var run = await SetUpAsync(LooseBlockPrototype);
        var hull = run.Scene.Hull;
        await HoldAsync(hull, DirectionFlag.North, TickRate * 3);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.Deleted(run.Obstacle), Is.False, "a loose block isn't smashed");
            Assert.That(Damage(run.Obstacle), Is.EqualTo(FixedPoint2.Zero), "nor damaged");
            Assert.That(Vector2.Dot(HullPosition(run.Obstacle), run.Facing), Is.GreaterThan(4.2f), "it is pushed along");
        });
    }

    /// <summary>
    ///     The reported bug: the shipped humvee driven at a live steel chair isn't stopped dead without a word. It
    ///         breaks the chair and drives on, or crashes into it, but either way the chair is hurt and the hull
    ///         is not just stopped.
    /// </summary>
    [Test]
    public async Task HumveeIsNotStoppedSilentlyBySteelChair()
    {
        var run = await SetUpAsync(SteelChair, Humvee, distance: 9f);
        var hull = run.Scene.Hull;
        await StartEngineAsync(hull);
        await HoldAsync(hull, DirectionFlag.North, TickRate * 6, () =>
            SEntMan.Deleted(run.Obstacle) || EntityManagerQueued(run.Obstacle) || !CanMove(hull));

        await Server.WaitAssertion(() =>
        {
            var gone = SEntMan.Deleted(run.Obstacle) || EntityManagerQueued(run.Obstacle);
            Assert.That(gone || Damage(run.Obstacle) > FixedPoint2.Zero, Is.True, "the chair feels the hit");
            Assert.That(gone || !CanMove(hull) || Damage(hull) > FixedPoint2.Zero, Is.True, "and a chair that stands stops the hull with a crash");
            Assert.That(!gone || HullVelocity(hull).Length() > 0f, Is.True, "a chair that breaks lets it drive on");
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
    ///     The shipped hulls tell walls apart and nothing else: walls are crashed into at once, windows, grilles, tables,
    ///         chairs and doors are not, and go by the damage they take.
    /// </summary>
    [Test]
    public async Task HullBaseOnlyListsWalls()
    {
        await Server.WaitAssertion(() =>
        {
            Assert.That(ProtoMan.Index(_hullFromBase).TryComp<KsCrewVehicleSmashComponent>(out var smash, Factory), Is.True);
            var whitelistSystem = SEntMan.System<EntityWhitelistSystem>();

            foreach (var id in new[] { Window, Grille, Table, Wall, AirlockPrototype, SteelChair })
            {
                var uid = SEntMan.SpawnAtPosition(id, OriginCoordinates.Offset(new Vector2(6f, 6f)));
                var wall = whitelistSystem.IsWhitelistPass(smash!.Walls, uid);
                SEntMan.DeleteEntity(uid);

                Assert.That(wall, Is.EqualTo(id == Wall), $"{id}: only walls are crashed into outright");
            }
        });
    }

    private bool EntityManagerQueued(EntityUid uid) => SEntMan is EntityManager entityManager && entityManager.IsQueuedForDeletion(uid);
}
