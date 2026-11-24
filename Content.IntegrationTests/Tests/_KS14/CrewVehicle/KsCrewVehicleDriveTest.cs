#nullable enable
using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Shared._KS14.CrewVehicle;
using Content.Shared._KS14.CrewVehicle.Drive;
using Content.Shared._KS14.CrewVehicle.Horn;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Audio;
using Content.Shared.Light.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Throwing;
using Content.Shared.Vehicle.Components;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;

namespace Content.IntegrationTests.Tests._KS14.CrewVehicle;

/// <summary>
///     Driving a crew vehicle from the driver seat in its interior, with the player's own keys through their client.
///         Every number comes from the test hull's prototype.
/// </summary>
[TestOf(typeof(KsCrewVehicleDriveSystem))]
public sealed class KsCrewVehicleDriveTest : KsCrewVehicleDriveTestBase
{
    [Test]
    public async Task OnlyTheSeatedDriverDrives()
    {
        var scene = await SpawnHullAsync(Vector2.Zero);
        await PutInsideAsync(scene);
        var start = Vector2.Zero;
        await Server.WaitPost(() => start = HullPosition(scene.Hull));

        await HoldAsync(scene.Hull, DirectionFlag.South, TickRate);
        await Server.WaitAssertion(() =>
        {
            Assert.That((HullPosition(scene.Hull) - start).Length(), Is.LessThan(0.05f),
                "an occupant standing inside, not at the wheel, shouldn't move the vehicle");
            Assert.That(SComp<VehicleComponent>(scene.Hull).Operator, Is.Null);
        });

        // They walked off across the interior: back to the wheel.
        await PutInsideAsync(scene);
        await SitAsync(scene.DriverSeat);
        await Server.WaitAssertion(() =>
            Assert.That(SComp<VehicleComponent>(scene.Hull).Operator, Is.EqualTo(SPlayer), "sitting at the wheel makes them the driver"));

        await HoldAsync(scene.Hull, DirectionFlag.South, TickRate);
        var driven = Vector2.Zero;
        await Server.WaitAssertion(() =>
        {
            driven = HullPosition(scene.Hull);
            Assert.That(start.Y - driven.Y, Is.GreaterThan(TopSpeed(scene.Hull) * 0.25f),
                "the seated driver's keys should drive the vehicle outside");
        });

        await StandAsync();
        await SettleAsync(scene.Hull);
        await Server.WaitPost(() => driven = HullPosition(scene.Hull));
        await HoldAsync(scene.Hull, DirectionFlag.South, TickRate);
        await Server.WaitAssertion(() =>
        {
            Assert.That((HullPosition(scene.Hull) - driven).Length(), Is.LessThan(0.05f),
                "after standing up, their keys shouldn't drive it any more");
            Assert.That(SComp<VehicleComponent>(scene.Hull).Operator, Is.Null);
        });
    }

    /// <summary>
    ///     Pressing a direction: the way it faces drives on, the way behind backs up without turning, and a side turns it
    ///         to face that way.
    /// </summary>
    [Test]
    public async Task PressDirectionSteering()
    {
        await SetTankControlsAsync(false);
        var scene = await SpawnHullAsync(Vector2.Zero);
        await PutInsideAsync(scene);
        await SitAsync(scene.DriverSeat);
        var hull = scene.Hull;

        var start = Vector2.Zero;
        var startRotation = Angle.Zero;
        await Server.WaitPost(() =>
        {
            start = HullPosition(hull);
            startRotation = HullRotation(hull);
        });
        var facing = KsCrewVehicleHullFrame.Forward(startRotation);
        var facingKey = DirectionFor(facing);
        var behindKey = DirectionFor(-facing);

        // On the way it faces.
        await HoldAsync(hull, facingKey, TickRate);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Vector2.Dot(HullPosition(hull) - start, facing), Is.GreaterThan(TopSpeed(hull) * 0.25f),
                "pressing the way it faces drives it that way");
            Assert.That(Angle.ShortestDistance(startRotation, HullRotation(hull)).Degrees, Is.EqualTo(0).Within(0.5),
                "without turning it");
        });
        await SettleAsync(hull);

        // Behind it: backs up, slower, still facing the same way.
        var beforeReverse = Vector2.Zero;
        var fastestReverse = 0f;
        await Server.WaitPost(() => beforeReverse = HullPosition(hull));
        await HoldAsync(hull, behindKey, TickRate, () =>
        {
            fastestReverse = MathF.Max(fastestReverse, HullVelocity(hull).Length());
            return false;
        });
        await Server.WaitAssertion(() =>
        {
            Assert.That(Vector2.Dot(HullPosition(hull) - beforeReverse, -facing), Is.GreaterThan(0.1f),
                "pressing the way behind backs it up");
            Assert.That(Angle.ShortestDistance(startRotation, HullRotation(hull)).Degrees, Is.EqualTo(0).Within(0.5),
                "without turning it round");
            Assert.That(fastestReverse, Is.LessThanOrEqualTo(TopSpeed(hull) * Drive(hull).ReverseSpeedFraction * 1.05f),
                "at its reverse speed");
        });
        await SettleAsync(hull);

        // To the side: turns to face it, then drives that way.
        var side = new Vector2(facing.Y, -facing.X);
        var sideHeading = side.ToWorldAngle();
        var maxTurnTicks = (int)(Math.PI / 2 / Drive(hull).TurnRate.Theta * TickRate) * 2 + TickRate;
        await HoldAsync(hull, DirectionFor(side), maxTurnTicks + TickRate);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Angle.ShortestDistance(sideHeading, HullRotation(hull)).Degrees, Is.EqualTo(0).Within(1),
                "pressing a side turns it to face that way");
            Assert.That(Vector2.Dot(HullVelocity(hull), side), Is.GreaterThan(0f), "and drives that way");
        });
    }

    /// <summary>
    ///     Tank controls: up drives forward and down backs up, whichever way it faces; left and right turn it.
    /// </summary>
    [Test]
    public async Task TankSteering()
    {
        await SetTankControlsAsync(true);
        var scene = await SpawnHullAsync(Vector2.Zero);
        await PutInsideAsync(scene);
        await SitAsync(scene.DriverSeat);
        var hull = scene.Hull;

        var start = Vector2.Zero;
        var startRotation = Angle.Zero;
        await Server.WaitPost(() =>
        {
            start = HullPosition(hull);
            startRotation = HullRotation(hull);
        });
        var facing = KsCrewVehicleHullFrame.Forward(startRotation);

        await HoldAsync(hull, DirectionFlag.North, TickRate);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Vector2.Dot(HullPosition(hull) - start, facing), Is.GreaterThan(TopSpeed(hull) * 0.25f),
                "up drives it the way it faces");
            Assert.That(Angle.ShortestDistance(startRotation, HullRotation(hull)).Degrees, Is.EqualTo(0).Within(0.5));
        });
        await SettleAsync(hull);

        var beforeReverse = Vector2.Zero;
        await Server.WaitPost(() => beforeReverse = HullPosition(hull));
        await HoldAsync(hull, DirectionFlag.South, TickRate);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Vector2.Dot(HullPosition(hull) - beforeReverse, -facing), Is.GreaterThan(0.1f), "down backs it up");
            Assert.That(Angle.ShortestDistance(startRotation, HullRotation(hull)).Degrees, Is.EqualTo(0).Within(0.5),
                "without turning it");
        });
        await SettleAsync(hull);

        // Right turns it clockwise on the spot, never faster than its turn rate. A quarter turn's worth of holding.
        var beforeTurn = Vector2.Zero;
        await Server.WaitPost(() => beforeTurn = HullPosition(hull));
        var maxStep = Drive(hull).TurnRate.Degrees / TickRate;
        var previous = startRotation;
        var fastestStep = 0.0;
        var clockwise = true;
        await HoldAsync(hull, DirectionFlag.East, (int)(90 / maxStep), () =>
        {
            var step = Angle.ShortestDistance(previous, HullRotation(hull)).Degrees;
            fastestStep = Math.Max(fastestStep, Math.Abs(step));
            clockwise &= step <= 1e-6;
            previous = HullRotation(hull);
            return false;
        });
        var turned = Angle.Zero;
        await Server.WaitAssertion(() =>
        {
            turned = HullRotation(hull);
            Assert.That(Angle.ShortestDistance(startRotation, turned).Degrees, Is.LessThan(-45),
                "holding right turns it clockwise");
            Assert.That(clockwise, Is.True, "only ever clockwise");
            Assert.That(fastestStep, Is.LessThanOrEqualTo(maxStep * 1.01), "never faster than its turn rate");
            Assert.That((HullPosition(hull) - beforeTurn).Length(), Is.LessThan(0.05f), "on the spot");
        });

        // And up now drives the new way.
        var beforeNew = Vector2.Zero;
        await Server.WaitPost(() => beforeNew = HullPosition(hull));
        await HoldAsync(hull, DirectionFlag.North, TickRate);
        await Server.WaitAssertion(() =>
            Assert.That(Vector2.Dot(HullPosition(hull) - beforeNew, KsCrewVehicleHullFrame.Forward(turned)),
                Is.GreaterThan(TopSpeed(hull) * 0.25f), "up drives it the way it now faces"));
    }

    [Test]
    public async Task HullStopsAtAWall()
    {
        await SetTankControlsAsync(false);
        var scene = await SpawnHullAsync(Vector2.Zero);
        EntityUid wall = default;
        var facing = Vector2.Zero;
        await Server.WaitPost(() =>
        {
            facing = KsCrewVehicleHullFrame.Forward(HullRotation(scene.Hull));
            wall = SEntMan.SpawnAtPosition(WallPrototype, OriginCoordinates.Offset(facing * 4f));
        });
        await PutInsideAsync(scene);
        await SitAsync(scene.DriverSeat);

        // Bounds compared in the same tick: the test grid drifts.
        var intoWall = 0f;
        await HoldAsync(scene.Hull, DirectionFor(facing), TickRate * 4, () =>
        {
            intoWall = MathF.Max(intoWall, HardBox(wall).Top - HardBox(scene.Hull).Bottom);
            return false;
        });
        Assert.That(intoWall, Is.LessThan(0.05f), "the hull should stop at the wall, not pass into it");
    }

    [Test]
    public async Task DriverActions()
    {
        var scene = await SpawnHullAsync(Vector2.Zero);
        await PutInsideAsync(scene);
        var actionsSystem = SEntMan.System<SharedActionsSystem>();

        EntityUid horn = default, headlight = default;
        await Server.WaitPost(() =>
        {
            horn = SComp<KsCrewVehicleHornComponent>(scene.Hull).ActionEntity!.Value;
            headlight = SComp<UnpoweredFlashlightComponent>(scene.Hull).ToggleActionEntity!.Value;
        });

        bool HasAction(EntityUid action) => actionsSystem.GetActions(SPlayer).Any(owned => owned.Owner == action);

        // A passenger seat gives nothing.
        await SitAsync(scene.PassengerSeat);
        await Server.WaitAssertion(() =>
            Assert.That(HasAction(horn) || HasAction(headlight), Is.False, "a passenger gets no driver actions"));
        await StandAsync();

        await SitAsync(scene.DriverSeat);
        await Server.WaitAssertion(() =>
        {
            Assert.That(HasAction(horn), Is.True, "the driver gets the horn");
            Assert.That(HasAction(headlight), Is.True, "and the headlight");
            Assert.That(SComp<AmbientSoundComponent>(scene.Hull).Enabled, Is.True, "and the engine runs");

            actionsSystem.PerformAction(SPlayer, SEntity<ActionComponent>(headlight));
            Assert.That(SEntMan.System<SharedPointLightSystem>().TryGetLight(scene.Hull, out var light) && light.Enabled, Is.True,
                "the headlight action switches the vehicle's light on");

            actionsSystem.PerformAction(SPlayer, SEntity<ActionComponent>(horn));
            Assert.That(SComp<ActionComponent>(horn).Cooldown, Is.Not.Null, "the horn sounds, and goes on its cooldown");
        });

        await StandAsync();
        await Server.WaitAssertion(() =>
        {
            Assert.That(HasAction(horn) || HasAction(headlight), Is.False, "standing up takes the driver actions away");
            Assert.That(SComp<AmbientSoundComponent>(scene.Hull).Enabled, Is.False, "and the engine stops");
        });
    }

    /// <summary>
    ///     A driver who goes critical while holding a key stops driving: the held key doesn't keep the hull going.
    /// </summary>
    [Test]
    public async Task CriticalDriverStops()
    {
        var scene = await SpawnHullAsync(Vector2.Zero);
        await PutInsideAsync(scene);
        await SitAsync(scene.DriverSeat);
        var hull = scene.Hull;

        await SetMovementKey(DirectionFlag.South, Robust.Shared.Input.BoundKeyState.Down);
        await RunTicks(TickRate / 2);
        await Server.WaitPost(() =>
        {
            Assert.That(HullVelocity(hull).Length(), Is.GreaterThan(TopSpeed(hull) * 0.25f), "precondition: driving");
            SEntMan.System<MobStateSystem>().ChangeMobState(SPlayer, MobState.Critical);
        });

        var stopped = false;
        for (var tick = 0; tick < TickRate * 5 && !stopped; tick++)
        {
            await RunTicks(1);
            await Server.WaitPost(() => stopped = HullVelocity(hull).Length() < 0.01f);
        }

        await SetMovementKey(DirectionFlag.South, Robust.Shared.Input.BoundKeyState.Up);
        Assert.That(stopped, Is.True, "with its driver critical, the key they still hold shouldn't keep the hull going");
    }

    /// <summary>
    ///     Parked, a hull is a loose body a mob walks into and can't push, and nothing can throw it.
    /// </summary>
    [Test]
    public async Task ParkedHullIsSolidAndCannotBeThrown()
    {
        var scene = await SpawnHullAsync(Vector2.Zero);
        // Out of the way: the player starts where the hull is parked.
        await PutInsideAsync(scene);
        EntityUid mob = default, crate = default;
        var hullStart = Vector2.Zero;
        await Server.WaitPost(() =>
        {
            hullStart = HullPosition(scene.Hull);
            mob = SEntMan.SpawnAtPosition(Human, OriginCoordinates.Offset(new Vector2(-3f, 0f)));
            crate = SEntMan.SpawnAtPosition(CratePrototype, OriginCoordinates.Offset(new Vector2(0f, 4f)));
            var mover = SComp<InputMoverComponent>(mob);
            SEntMan.System<SharedMoverController>().SetVelocityDirection((mob, mover), Direction.East, 0, true);
        });
        await RunTicks(TickRate * 2);

        await Server.WaitAssertion(() =>
        {
            Assert.That(HardBox(mob).Right, Is.LessThanOrEqualTo(HardBox(scene.Hull).Left + 0.05f),
                "a mob walking into a parked hull should be stopped by it");
            Assert.That((HullPosition(scene.Hull) - hullStart).Length(), Is.LessThan(0.01f), "and not push it");

            var throwingSystem = SEntMan.System<ThrowingSystem>();
            throwingSystem.TryThrow(crate, new Vector2(3f, 0f), baseThrowSpeed: 8f);
            throwingSystem.TryThrow(scene.Hull, new Vector2(3f, 0f), baseThrowSpeed: 8f);
            Assert.That(SEntMan.HasComponent<ThrownItemComponent>(crate), Is.True, "control: a loose crate is thrown");
            Assert.That(SEntMan.HasComponent<ThrownItemComponent>(scene.Hull), Is.False, "a hull shouldn't be");
            Assert.That(HullVelocity(scene.Hull).Length(), Is.Zero);
        });
    }

    /// <summary>
    ///     Two driven hulls are both kinematic controllers, which physics lets pass through each other: one driven into
    ///         the other should stop against it instead.
    /// </summary>
    [Test]
    public async Task DrivenHullsBlockEachOther()
    {
        await SetTankControlsAsync(false);
        var ours = await SpawnHullAsync(Vector2.Zero);
        var facing = Vector2.Zero;
        await Server.WaitPost(() => facing = KsCrewVehicleHullFrame.Forward(HullRotation(ours.Hull)));
        var theirs = await SpawnHullAsync(facing * 4f);

        EntityUid otherDriver = default;
        await Server.WaitPost(() =>
            otherDriver = SEntMan.SpawnAtPosition(Human, SComp<TransformComponent>(theirs.DriverSeat).Coordinates));
        await RunTicks(5);
        await SitAsync(theirs.DriverSeat, otherDriver);
        await PutInsideAsync(ours);
        await SitAsync(ours.DriverSeat);

        var theirStart = Vector2.Zero;
        await Server.WaitAssertion(() =>
        {
            Assert.That(SComp<PhysicsComponent>(theirs.Hull).BodyType, Is.EqualTo(Robust.Shared.Physics.BodyType.KinematicController),
                "precondition: the other hull is being driven");
            theirStart = HullPosition(theirs.Hull);
        });

        // Long enough to have driven right through it: checked every tick, not only at the end.
        var overlapped = false;
        await HoldAsync(ours.Hull, DirectionFor(facing), TickRate * 4, () =>
        {
            overlapped |= HardBox(ours.Hull).Intersects(HardBox(theirs.Hull).Enlarged(-0.05f));
            return false;
        });
        await Server.WaitAssertion(() =>
        {
            Assert.That(overlapped, Is.False, "a driven hull should stop against another driven hull, not drive into it");
            Assert.That(Vector2.Dot(HullPosition(theirs.Hull) - HullPosition(ours.Hull), facing), Is.GreaterThan(0f),
                "and stay on its own side of it");
            Assert.That((HullPosition(theirs.Hull) - theirStart).Length(), Is.LessThan(0.05f), "and not shove it");
        });
    }

    [Test]
    public async Task InteriorWithTwoDriverSeatsIsRefused()
    {
        var errors = new System.Collections.Generic.List<string>();
        await Server.WaitPost(() => errors = RefusedInterior(TwoDriversHullPrototype));
        Assert.That(errors.Any(error => error.Contains("second driver seat") || error.Contains("a second one")), Is.True,
            $"the error should name the second driver seat; got: {string.Join("; ", errors)}");
    }

    [Test]
    public async Task DriverSeatThatIsNoViewSeatIsRefused()
    {
        var errors = new System.Collections.Generic.List<string>();
        await Server.WaitPost(() => errors = RefusedInterior(BlindHullPrototype));
        Assert.That(errors.Any(error => error.Contains("blind driver seat") && error.Contains("view seat")), Is.True,
            $"the error should name the seat and say it needs to be a view seat; got: {string.Join("; ", errors)}");
    }

    /// <summary>
    ///     The movement key for the nearest of the four directions to <paramref name="direction"/>.
    /// </summary>
    private static DirectionFlag DirectionFor(Vector2 direction)
    {
        if (MathF.Abs(direction.X) > MathF.Abs(direction.Y))
            return direction.X > 0 ? DirectionFlag.East : DirectionFlag.West;

        return direction.Y > 0 ? DirectionFlag.North : DirectionFlag.South;
    }
}
