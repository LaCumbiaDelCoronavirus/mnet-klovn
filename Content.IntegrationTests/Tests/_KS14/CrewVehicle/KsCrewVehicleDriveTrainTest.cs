#nullable enable
using System;
using System.Numerics;
using System.Threading.Tasks;
using Content.Shared._KS14.CrewVehicle;
using Content.Shared._KS14.CrewVehicle.Drive;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._KS14.CrewVehicle;

/// <summary>
///     Wheels turn only while rolling, and the other way backing up; tracks turn on the spot. Up and down only ever drive
///         along the facing. Every number comes from the test hulls' prototypes.
/// </summary>
[TestOf(typeof(KsCrewVehicleSteering))]
public sealed class KsCrewVehicleDriveTrainTest : KsCrewVehicleDriveTestBase
{
    private async Task<HullScene> SeatedAsync(bool wheeled)
    {
        var scene = await SpawnHullAsync(Vector2.Zero, wheeled ? WheeledHullPrototype : HullPrototype);
        await PutInsideAsync(scene);
        await SitAsync(scene.DriverSeat);
        return scene;
    }

    private static double Turned(Angle from, Angle to) => Angle.ShortestDistance(from, to).Degrees;

    /// <summary>
    ///     A turn key alone, at rest: tracks turn on the spot; wheels do nothing at all.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task TurnKeyAloneAtRest(bool wheeled)
    {
        var scene = await SeatedAsync(wheeled);
        var hull = scene.Hull;
        var startRotation = Angle.Zero;
        var startPosition = Vector2.Zero;
        await Server.WaitPost(() =>
        {
            startRotation = HullRotation(hull);
            startPosition = HullPosition(hull);
        });

        await HoldAsync(hull, DirectionFlag.East, TickRate);

        await Server.WaitAssertion(() =>
        {
            var turned = Math.Abs(Turned(startRotation, HullRotation(hull)));
            var moved = (HullPosition(hull) - startPosition).Length();
            if (wheeled)
            {
                Assert.That(turned, Is.LessThan(0.01), "a turn key alone doesn't turn wheels at rest");
                Assert.That(moved, Is.LessThan(0.01f), "nor move them");
            }
            else
            {
                Assert.That(turned, Is.GreaterThan(10), "tracks turn");
                Assert.That(moved, Is.LessThan(0.05f), "on the spot");
            }
        });
    }

    /// <summary>
    ///     Forward alone never changes the facing; forward and a turn key turns either drive train.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task TurnsWhileMoving(bool wheeled)
    {
        var scene = await SeatedAsync(wheeled);
        var hull = scene.Hull;
        var start = Angle.Zero;
        await Server.WaitPost(() => start = HullRotation(hull));
        await HoldAsync(hull, DirectionFlag.North, TickRate / 2);

        var before = Angle.Zero;
        await Server.WaitAssertion(() =>
        {
            before = HullRotation(hull);
            Assert.That(Math.Abs(Turned(start, before)), Is.LessThan(0.01), "driving forward alone never changes the facing");
        });

        await HoldAsync(hull, DirectionFlag.North | DirectionFlag.East, TickRate / 2);
        await Server.WaitAssertion(() =>
            Assert.That(Math.Abs(Turned(before, HullRotation(hull))), Is.GreaterThan(10), "a moving hull turns, wheels or tracks"));
    }

    /// <summary>
    ///     Backing up, wheels steer the other way: down and right turn the nose anticlockwise. Up and right, and tracks
    ///         either way, turn it clockwise.
    /// </summary>
    [TestCase(true, DirectionFlag.South, 1)]
    [TestCase(true, DirectionFlag.North, -1)]
    [TestCase(false, DirectionFlag.South, -1)]
    public async Task ReversingWheelsSteerTheOtherWay(bool wheeled, DirectionFlag driveKey, int expectedSign)
    {
        var scene = await SeatedAsync(wheeled);
        var hull = scene.Hull;
        await HoldAsync(hull, driveKey, TickRate / 2);

        var before = Angle.Zero;
        await Server.WaitPost(() => before = HullRotation(hull));
        await HoldAsync(hull, driveKey | DirectionFlag.East, TickRate / 2);

        await Server.WaitAssertion(() =>
        {
            var turned = Turned(before, HullRotation(hull));
            Assert.That(Math.Abs(turned), Is.GreaterThan(5), "it turns");
            Assert.That(Math.Sign(turned), Is.EqualTo(expectedSign),
                expectedSign > 0 ? "backing up with right held, wheels turn the nose anticlockwise" : "right turns it clockwise");
        });
    }

    private static KsCrewVehicleDriveComponent Wheels()
    {
#pragma warning disable RA0002 // set up as a prototype would
        return new KsCrewVehicleDriveComponent { DriveTrain = KsCrewVehicleDriveTrain.Wheeled, FullTurnSpeed = 2f };
#pragma warning restore RA0002
    }

    /// <summary>
    ///     The turn factor itself: signed and clamped for wheels, always whole for tracks.
    /// </summary>
    [Test]
    public void TurnFactor()
    {
        var wheels = Wheels();
#pragma warning disable RA0002 // set up as a prototype would
        var tracks = new KsCrewVehicleDriveComponent { DriveTrain = KsCrewVehicleDriveTrain.Tracked };
#pragma warning restore RA0002
        var forward = KsCrewVehicleHullFrame.Forward(Angle.Zero);

        KsCrewVehicleSteeringInput Moving(float speed) =>
            new(Vector2.Zero, Angle.Zero, Angle.Zero, forward * speed, 1f / 30f);

        Assert.Multiple(() =>
        {
            Assert.That(KsCrewVehicleSteering.TurnFactor(Moving(0f), wheels), Is.Zero, "wheels at rest don't turn");
            Assert.That(KsCrewVehicleSteering.TurnFactor(Moving(1f), wheels), Is.EqualTo(0.5f).Within(1e-5), "half speed, half the rate");
            Assert.That(KsCrewVehicleSteering.TurnFactor(Moving(5f), wheels), Is.EqualTo(1f), "no more than the full rate");
            Assert.That(KsCrewVehicleSteering.TurnFactor(Moving(-1f), wheels), Is.EqualTo(-0.5f).Within(1e-5), "the other way backing up");
            Assert.That(KsCrewVehicleSteering.TurnFactor(Moving(0f), tracks), Is.EqualTo(1f), "tracks always turn");
        });
    }

    /// <summary>
    ///     The keys decide, not the screen: whatever turn the mover gave them (a screen turned with the hull's grid), up
    ///         drives along the facing and leaves it alone, and right turns clockwise.
    /// </summary>
    [TestCase(0)]
    [TestCase(90)]
    [TestCase(-135)]
    public void KeysNotTheScreenDecide(double screenDegrees)
    {
        var drive = Wheels();
        var screen = Angle.FromDegrees(screenDegrees);
        var hullRotation = Angle.FromDegrees(30);
        var forward = KsCrewVehicleHullFrame.Forward(hullRotation);
        const float speed = 3f;

        KsCrewVehicleSteeringInput Keys(Vector2 stick) =>
            new(screen.RotateVec(stick * speed), screen, hullRotation, forward * speed, 1f / 30f);

        var up = KsCrewVehicleSteering.Tank(Keys(new Vector2(0f, 1f)), drive);
        var upRight = KsCrewVehicleSteering.Tank(Keys(Vector2.Normalize(new Vector2(1f, 1f))), drive);

        Assert.Multiple(() =>
        {
            Assert.That(up.HullRotation.EqualsApprox(hullRotation), Is.True, "up alone keeps the facing");
            Assert.That(Vector2.Dot(Vector2.Normalize(up.Wish), forward), Is.EqualTo(1f).Within(1e-4), "and drives along it");
            Assert.That(Turned(hullRotation, upRight.HullRotation), Is.LessThan(0), "up and right turn it clockwise");
        });
    }
}
