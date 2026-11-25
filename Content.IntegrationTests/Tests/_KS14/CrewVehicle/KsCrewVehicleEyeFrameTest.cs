#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Shared._KS14.CrewVehicle.Gunner;
using Content.Shared._KS14.CrewVehicle.Turret;
using Content.Shared._KS14.CrewVehicle.View;
using Content.Shared._KS14.Movement;
using Content.Shared.Movement.Components;
using Content.Shared.Vehicle.Components;
using NUnit.Framework;
using Robust.Client.Graphics;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._KS14.CrewVehicle;

/// <summary>
///     From a view seat the outside is turned as it is for a mob standing at the hull: to the grid the hull is on, or
///         to the map off any grid, easing when that changes, as a mob's view does crossing grids.
/// </summary>
/// <remarks>
///     The headless client's eye system runs (<c>EyeLerpingSystem</c>), so the eye rotation asserted is the one the
///         client would draw with; nothing is drawn.
/// </remarks>
[TestFixture]
[TestOf(typeof(KsCrewVehicleViewSystem))]
public sealed class KsCrewVehicleEyeFrameTest : KsCrewVehicleGunnerTestBase
{
    /// <summary>Not a quarter turn, so a frame that is wrong by any cardinal still shows.</summary>
    private static readonly Angle GridRotation = Angle.FromDegrees(30);

    private const double Tolerance = 0.01;

    private async Task<Angle> ClientEyeRotation()
    {
        var rotation = Angle.Zero;
        await Client.WaitPost(() => rotation = CEntMan.GetComponent<EyeComponent>(CPlayer).Rotation);
        return rotation;
    }

    private static bool SameAngle(Angle a, Angle b) => Math.Abs(Angle.ShortestDistance(a, b).Theta) < Tolerance;

    /// <summary>
    ///     Runs until the client's eye stops turning, under a generous cap, returning each tick's rotation.
    /// </summary>
    private async Task<List<Angle>> SettleEyeAsync()
    {
        var samples = new List<Angle> { await ClientEyeRotation() };
        var still = 0;
        for (var tick = 0; tick < TickRate * 10 && still < 5; tick++)
        {
            await RunTicks(1);
            var rotation = await ClientEyeRotation();
            still = SameAngle(rotation, samples[^1]) && Math.Abs(Angle.ShortestDistance(rotation, samples[^1]).Theta) < 0.0001
                ? still + 1
                : 0;
            samples.Add(rotation);
        }

        return samples;
    }

    /// <summary>
    ///     Turns the outside grid, with the player standing on it, and returns the eye rotation a mob standing there
    ///         settles on: what a view seat on that grid should show.
    /// </summary>
    private async Task<Angle> TurnGridAsync()
    {
        await Server.WaitPost(() => Transform.SetWorldRotation(MapData.Grid.Owner, GridRotation));
        var mobRotation = (await SettleEyeAsync())[^1];
        Assert.That(SameAngle(mobRotation, Angle.Zero), Is.False, "precondition: a mob on the turned grid sees it turned");
        return mobRotation;
    }

    /// <summary>
    ///     The player inside the turned grid's hull, seated: at the wheel of the driver-gunner hull, or in the gunner-only
    ///         seat of the several-gunner hull.
    /// </summary>
    private async Task<HullScene> SitInsideAsync(bool driver, Vector2? offset = null)
    {
        var scene = await SpawnHullAsync(offset ?? Vector2.Zero, driver ? _gunnerHull : _multiGunnerHull);
        await PutInsideAsync(scene);
        Assert.That(SameAngle((await SettleEyeAsync())[^1], Angle.Zero), Is.True, "precondition: the interior is upright");

        var seat = EntityUid.Invalid;
        await Server.WaitPost(() => seat = driver ? scene.DriverSeat : GunnerSeat(scene, LeftMount));
        await SitAsync(seat);
        await SetCombatMode(true);
        if (driver)
        {
            await Server.WaitAssertion(() =>
                Assert.That(SComp<VehicleComponent>(scene.Hull).Operator, Is.EqualTo(SPlayer), "precondition: driving, so relaying"));
        }

        return scene;
    }

    /// <param name="driver">At the wheel, whose keys are relayed to the hull; otherwise in a gunner-only seat.</param>
    [TestCase(true)]
    [TestCase(false)]
    public async Task SeatedEyeTurnsAsAMobsOnTheGrid(bool driver)
    {
        var mobRotation = await TurnGridAsync();
        await SitInsideAsync(driver);

        var settled = (await SettleEyeAsync())[^1];
        Assert.That(SameAngle(settled, mobRotation), Is.True,
            $"seated, the eye should be turned as a mob's on the hull's grid ({mobRotation.Degrees:F1}°), was {settled.Degrees:F1}°");

        Angle serverRotation = default;
        await Server.WaitAssertion(() =>
        {
            var mover = SComp<InputMoverComponent>(SPlayer);
            Assert.That(mover.RelativeEntity, Is.EqualTo(MapData.Grid.Owner), "the occupant's frame is the hull's grid");
            serverRotation = mover.RelativeRotation;
        });
        await Client.WaitAssertion(() =>
        {
            var mover = CEntMan.GetComponent<InputMoverComponent>(CPlayer);
            Assert.That(mover.RelativeEntity, Is.EqualTo(ToClient(MapData.Grid.Owner)), "and the client's agrees");
            Assert.That(SameAngle(mover.RelativeRotation, serverRotation), Is.True, "with the same turn: nothing mispredicted");
        });
    }

    [Test]
    public async Task OffGridTheViewIsTheMaps()
    {
        await TurnGridAsync();
        // Well past the floor, off any grid.
        await SitInsideAsync(driver: true, offset: new Vector2(FloorRadius * 3f, 0f));

        await Server.WaitAssertion(() =>
            Assert.That(SComp<TransformComponent>(SComp<KsCrewVehicleViewerComponent>(SPlayer).Vehicle).GridUid, Is.Null,
                "precondition: the hull is off the grid"));
        var settled = (await SettleEyeAsync())[^1];
        Assert.That(SameAngle(settled, Angle.Zero), Is.True, $"off any grid the view is the map's, north up; was {settled.Degrees:F1}°");
        await Server.WaitAssertion(() =>
            Assert.That(SComp<InputMoverComponent>(SPlayer).RelativeEntity, Is.EqualTo(MapData.MapUid), "the frame is the map"));
    }

    [Test]
    public async Task ChangingGridEasesTheView()
    {
        var mobRotation = await TurnGridAsync();
        var scene = await SitInsideAsync(driver: false);
        Assert.That(SameAngle((await SettleEyeAsync())[^1], mobRotation), Is.True, "precondition: seated on the turned grid");

        // A second, upright grid beside the first, and the hull put on it. Held still, as the floor grid is: a grid this
        //      light turns a little under the hull put on it, and the view should then end at that turn instead.
        await Server.WaitPost(() =>
        {
            var gridEntity = MapSystem.CreateGridEntity(MapId);
            SEntMan.System<Robust.Shared.Physics.Systems.SharedPhysicsSystem>().SetBodyType(gridEntity.Owner, Robust.Shared.Physics.BodyType.Static);
            Transform.SetWorldPosition(gridEntity.Owner, new Vector2(FloorRadius * 4f, 0f));
            for (var x = -2; x <= 2; x++)
            {
                for (var y = -2; y <= 2; y++)
                {
                    MapSystem.SetTile(gridEntity, new Vector2i(x, y), MapData.Tile.Tile);
                }
            }

            Transform.SetCoordinates(scene.Hull, new EntityCoordinates(gridEntity.Owner, new Vector2(0.5f, 0.5f)));
        });

        var samples = await SettleEyeAsync();
        Assert.That(SameAngle(samples[^1], Angle.Zero), Is.True,
            $"on the upright grid, the view should end upright; was {samples[^1].Degrees:F1}°");
        Assert.That(samples.Any(sample => !SameAngle(sample, Angle.Zero) && !SameAngle(sample, mobRotation)), Is.True,
            "and get there by easing, as a mob's view does, not at once");
    }

    [Test]
    public async Task StandingSnapsBackToTheInterior()
    {
        var mobRotation = await TurnGridAsync();
        var scene = await SitInsideAsync(driver: true);
        Assert.That(SameAngle((await SettleEyeAsync())[^1], mobRotation), Is.True, "precondition: seated on the turned grid");

        await StandAsync();
        await RunTicks(1);

        var standing = await ClientEyeRotation();
        Assert.That(SameAngle(standing, Angle.Zero), Is.True,
            $"standing up, the view should be back to the upright interior at once; was {standing.Degrees:F1}°");
        await Server.WaitAssertion(() =>
        {
            Assert.That(SComp<InputMoverComponent>(SPlayer).RelativeEntity, Is.EqualTo(scene.InteriorGrid), "the frame is the interior again");
            Assert.That(SEntMan.HasComponent<KsMoverRelativeFrameComponent>(SPlayer), Is.False);
        });
    }

    /// <summary>
    ///     Resetting the camera reads the frame from the mover's own transform, which is the interior: it must not leave
    ///         the view there.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task ResettingTheCameraKeepsTheView(bool driver)
    {
        var mobRotation = await TurnGridAsync();
        await SitInsideAsync(driver);
        Assert.That(SameAngle((await SettleEyeAsync())[^1], mobRotation), Is.True, "precondition: seated on the turned grid");

        // Turned away first, so that the reset has something to undo and is seen to have run.
        await SetKey(EngineKeyFunctions.CameraRotateLeft, BoundKeyState.Down);
        await SetKey(EngineKeyFunctions.CameraRotateLeft, BoundKeyState.Up);
        Assert.That(SameAngle((await SettleEyeAsync())[^1], mobRotation), Is.False, "precondition: the camera turned");

        await SetKey(EngineKeyFunctions.CameraReset, BoundKeyState.Down);
        await SetKey(EngineKeyFunctions.CameraReset, BoundKeyState.Up);

        var settled = (await SettleEyeAsync())[^1];
        Assert.That(SameAngle(settled, mobRotation), Is.True,
            $"after resetting the camera, the view should still be the hull's grid's; was {settled.Degrees:F1}°");
    }

    /// <summary>
    ///     A point straight up the turned screen, through the client's own viewport, is a point up the hull's grid; a shot
    ///         at it goes there, and a turret aimed at it turns to it.
    /// </summary>
    [Test]
    public async Task AimingGoesWhereTheTurnedScreenPoints()
    {
        await TurnGridAsync();
        var scene = await SitInsideAsync(driver: false);
        await SettleEyeAsync();

        var weapon = EntityUid.Invalid;
        await Server.WaitPost(() => weapon = Weapon(scene.Hull, LeftMount));
        var weaponPosition = Vector2.Zero;
        await Server.WaitPost(() => weaponPosition = WorldPosition(weapon));

        // Three tiles above the middle of the screen, as the mouse would point. The headless viewport has no eye, so
        //      this takes the step its PixelToMap ends with (Viewport.LocalToWorld): the current eye's inverse view
        //      matrix, the one the cursor goes through in a real client.
        var cursorWorld = Vector2.Zero;
        var eyeCentre = Vector2.Zero;
        await Client.WaitPost(() =>
        {
            var eye = Client.ResolveDependency<IEyeManager>().CurrentEye;
            Assert.That(eye.Position.MapId, Is.EqualTo(CEntMan.GetComponent<TransformComponent>(ToClient(scene.Hull)).MapID),
                "precondition: the eye is outside");
            eye.GetViewMatrixInv(out var viewToWorld, Vector2.One);
            cursorWorld = Vector2.Transform(new Vector2(0f, 3f * eye.Zoom.Y), viewToWorld);
            eyeCentre = Vector2.Transform(Vector2.Zero, viewToWorld);
        });

        var gridUp = GridRotation.RotateVec(Vector2.UnitY);
        var screenUp = Vector2.Normalize(cursorWorld - eyeCentre);
        Assert.That(Vector2.Dot(screenUp, gridUp), Is.GreaterThan(0.999f),
            $"up the screen should be up the hull's grid {gridUp}, was {screenUp}");

        // The turret first, as the mouse moving does; it fires only once it has turned that way.
        var weaponNetEntity = SEntMan.GetNetEntity(weapon);
        var target = OutsideAt(cursorWorld);
        await Client.WaitPost(() => CEntMan.RaisePredictiveEvent(new KsCrewVehicleAimEvent { Weapon = weaponNetEntity, Target = target }));
        await RunTicks(5);
        await Server.WaitAssertion(() =>
        {
            var expected = ((cursorWorld - WorldPosition(weapon)).ToWorldAngle() - Transform.GetWorldRotation(scene.Hull)).Reduced();
            Assert.That(SameAngle(SComp<KsCrewVehicleTurretAimComponent>(weapon).TargetAngle, expected), Is.True,
                "the turret should turn toward the cursor");
        });
        // A half turn at the slowest turret there is, and then some.
        await RunTicks(TickRate * 4);

        await RequestShotAsync(weapon, target);
        await Server.WaitAssertion(() =>
        {
            var round = Rounds(weapon).Select(entity => entity.Owner).FirstOrDefault();
            Assert.That(round, Is.Not.EqualTo(EntityUid.Invalid), "precondition: a round was fired");
            var aim = Vector2.Normalize(cursorWorld - weaponPosition);
            var flown = Vector2.Normalize(WorldPosition(round) - weaponPosition);
            Assert.That(Vector2.Dot(aim, flown), Is.GreaterThan(0.99f), "the shot should go toward the cursor");
        });
    }

}
