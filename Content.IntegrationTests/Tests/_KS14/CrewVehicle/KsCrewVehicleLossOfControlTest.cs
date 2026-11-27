#nullable enable
using System;
using System.Numerics;
using System.Threading.Tasks;
using Content.Shared._KS14.CrewVehicle;
using Content.Shared._KS14.CrewVehicle.Drive;
using Content.Shared.Stunnable;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.CrewVehicle;

/// <summary>
///     A driver who can't drive any longer: the hull rolls once, and either goes out of control on the throttle held,
///         steering wandering, until it hits something, or rolls to a stop. Each outcome is forced by its chance here.
/// </summary>
[TestOf(typeof(KsCrewVehicleLossOfControlSystem))]
public sealed class KsCrewVehicleLossOfControlTest : KsCrewVehicleDriveTestBase
{
    private readonly EntProtoId _alwaysLoses = "KsCrewVehicleLossTestAlways";
    private readonly EntProtoId _neverLoses = "KsCrewVehicleLossTestNever";
    private readonly EntProtoId _wanders = "KsCrewVehicleLossTestWanders";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: KsCrewVehicleDriveTestHull
  id: KsCrewVehicleLossTestAlways
  components:
  - type: KsCrewVehicleLossOfControl
    chance: 1
    wanderStrength: 0

- type: entity
  parent: KsCrewVehicleDriveTestHull
  id: KsCrewVehicleLossTestNever
  components:
  - type: KsCrewVehicleLossOfControl
    chance: 0

- type: entity
  parent: KsCrewVehicleDriveTestHull
  id: KsCrewVehicleLossTestWanders
  components:
  - type: KsCrewVehicleLossOfControl
    chance: 1
    wanderStrength: 1
    wanderInterval: 0.2
";

    private static readonly TimeSpan StunTime = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     Driving forward from <paramref name="start"/>, the driver stunned at full speed with the key still held.
    /// </summary>
    private async Task<(HullScene Scene, Vector2 Facing)> StunAtSpeedAsync(EntProtoId prototype, Vector2 start, TimeSpan? stunTime = null)
    {
        var scene = await SpawnHullAsync(start, prototype);
        await PutInsideAsync(scene);
        await SitAsync(scene.DriverSeat);
        var facing = Vector2.Zero;
        await Server.WaitPost(() => facing = KsCrewVehicleHullFrame.Forward(HullRotation(scene.Hull)));
        await SetMovementKey(DirectionFlag.North, Robust.Shared.Input.BoundKeyState.Down);
        await RunTicks(TickRate);
        await Server.WaitPost(() => SEntMan.System<SharedStunSystem>().TryAddStunDuration(SPlayer, stunTime ?? StunTime));
        await RunTicks(1);
        await SetMovementKey(DirectionFlag.North, Robust.Shared.Input.BoundKeyState.Up);
        return (scene, facing);
    }

    private float Speed(EntityUid hull, Vector2 facing) => Vector2.Dot(HullVelocity(hull), facing);

    [TestCase(true)]
    [TestCase(false)]
    public async Task IncapacitatedDriverLosesControlOrRollsToAStop(bool losesControl)
    {
        var (scene, facing) = await StunAtSpeedAsync(losesControl ? _alwaysLoses : _neverLoses, new Vector2(0f, 8f));
        await RunTicks(TickRate * 2);
        await Server.WaitAssertion(() =>
        {
            var speed = Speed(scene.Hull, facing);
            if (losesControl)
                Assert.That(speed, Is.GreaterThan(TopSpeed(scene.Hull) * 0.9f), "out of control, the throttle stays where it was");
            else
                Assert.That(speed, Is.LessThan(0.05f), "otherwise it rolls to a stop");
        });
    }

    [Test]
    public async Task OutOfControlSteeringWanders()
    {
        var (scene, _) = await StunAtSpeedAsync(_wanders, new Vector2(0f, 8f));
        var start = Angle.Zero;
        var cap = 0;
        await Server.WaitPost(() =>
        {
            start = HullRotation(scene.Hull);
            // Each interval picks left, right or straight on, a third each: plenty of intervals to see a turn, whichever
            //      tick this starts on, and turns that cancel out don't count against it (the furthest it turned does).
            var component = SComp<KsCrewVehicleLossOfControlComponent>(scene.Hull);
            cap = (int)(component.WanderInterval.TotalSeconds * TickRate * 30 / Math.Max(component.WanderStrength, 0.05f));
        });

        var furthest = 0.0;
        for (var tick = 0; tick < cap && furthest <= 1; tick++)
        {
            await RunTicks(1);
            await Server.WaitPost(() =>
                furthest = Math.Max(furthest, Math.Abs(Angle.ShortestDistance(start, HullRotation(scene.Hull)).Degrees)));
        }

        Assert.That(furthest, Is.GreaterThan(1), "out of control, the steering wanders");
    }

    /// <summary>
    ///     Out of control into a wall: it stops there, and being stunned again while still out doesn't roll again.
    /// </summary>
    [Test]
    public async Task HittingSomethingEndsItWithoutARoll()
    {
        var (scene, facing) = await StunAtSpeedAsync(_alwaysLoses, new Vector2(0f, 8f));
        await Server.WaitPost(() => SEntMan.SpawnAtPosition(TestWallPrototype, OriginCoordinates.Offset(new Vector2(0f, 8f) + facing * 5f)));
        await RunTicks(TickRate * 3);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SComp<KsCrewVehicleLossOfControlComponent>(scene.Hull).OutOfControl, Is.False, "hitting the wall ends it");
            Assert.That(Speed(scene.Hull, facing), Is.LessThan(0.05f), "and it stays stopped");
            SEntMan.System<SharedStunSystem>().TryAddStunDuration(SPlayer, StunTime);
        });
        await RunTicks(TickRate);
        await Server.WaitAssertion(() =>
            Assert.That(SComp<KsCrewVehicleLossOfControlComponent>(scene.Hull).OutOfControl, Is.False,
                "stunned again while still out, it doesn't roll again"));
    }

    /// <summary>
    ///     The driver recovering ends it, and the next incapacitation rolls afresh.
    /// </summary>
    [Test]
    public async Task RecoveryEndsItAndTheNextRollsAgain()
    {
        // A short stun, which wears off.
        var (scene, _) = await StunAtSpeedAsync(_alwaysLoses, new Vector2(0f, 8f), TimeSpan.FromSeconds(0.5));
        await RunTicks(TickRate * 3);
        await Server.WaitAssertion(() =>
        {
            var component = SComp<KsCrewVehicleLossOfControlComponent>(scene.Hull);
            Assert.That(component.OutOfControl || component.Rolled, Is.False, "recovered, it is the driver's again");
        });

        await SetMovementKey(DirectionFlag.North, Robust.Shared.Input.BoundKeyState.Down);
        await RunTicks(TickRate / 2);
        await Server.WaitPost(() => SEntMan.System<SharedStunSystem>().TryAddStunDuration(SPlayer, StunTime));
        await RunTicks(1);
        await SetMovementKey(DirectionFlag.North, Robust.Shared.Input.BoundKeyState.Up);
        await RunTicks(TickRate);
        await Server.WaitAssertion(() =>
            Assert.That(SComp<KsCrewVehicleLossOfControlComponent>(scene.Hull).OutOfControl, Is.True,
                "the next incapacitation rolls again"));
    }
}
