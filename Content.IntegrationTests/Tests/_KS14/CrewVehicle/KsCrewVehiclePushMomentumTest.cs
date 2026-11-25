#nullable enable
using System;
using System.Numerics;
using System.Threading.Tasks;
using Content.Shared._KS14.CrewVehicle;
using Content.Shared._KS14.CrewVehicle.Push;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;

namespace Content.IntegrationTests.Tests._KS14.CrewVehicle;

/// <summary>
///     What a driven hull pushes takes its share of the hull's momentum, by mass, and no more: a light crate barely slows
///         it, a parked hull its own weight halves its speed. Let go, it slides on as anything loose would, and the
///         driver's client keeps showing it slide.
/// </summary>
[TestOf(typeof(KsCrewVehiclePushController))]
public sealed class KsCrewVehiclePushMomentumTest : KsCrewVehicleDriveTestBase
{
    /// <summary>Where the hulls start, so there's room ahead to push in.</summary>
    private static readonly Vector2 Start = new(0f, 8f);

    private float Along(EntityUid uid, Vector2 facing) => Vector2.Dot(HullPosition(uid), facing);

    private float Mass(EntityUid uid) => SComp<PhysicsComponent>(uid).Mass;

    private async Task<(HullScene Scene, Vector2 Facing)> DrivenAsync()
    {
        var scene = await SpawnHullAsync(Start);
        await PutInsideAsync(scene);
        await SitAsync(scene.DriverSeat);
        var facing = Vector2.Zero;
        await Server.WaitPost(() => facing = KsCrewVehicleHullFrame.Forward(HullRotation(scene.Hull)));
        return (scene, facing);
    }

    /// <summary>
    ///     A crate a twentieth of the hull's mass: the hull keeps its pace pushing it, and the crate, let go, slides no
    ///         further than the same crate given the same speed by hand. The driver's client shows it sliding throughout.
    /// </summary>
    [Test]
    public async Task PushedCrateTakesItsShareAndSlidesLikeAnyCrate()
    {
        var (scene, facing) = await DrivenAsync();
        var hull = scene.Hull;
        EntityUid crate = default, control = default;
        await Server.WaitPost(() =>
        {
            crate = SEntMan.SpawnAtPosition(PushCratePrototype, OriginCoordinates.Offset(Start + facing * 3f));
            control = SEntMan.SpawnAtPosition(PushCratePrototype, OriginCoordinates.Offset(Start + facing * 3f + new Vector2(facing.Y, -facing.X) * 4f));
        });
        await RunTicks(5);

        // Up to speed and pushing it.
        var slowestPushing = float.MaxValue;
        var share = 0f;
        await HoldAsync(hull, DirectionFlag.North, TickRate * 2, () =>
        {
            share = Mass(hull) / (Mass(hull) + Mass(crate));
            if (SEntMan.HasComponent<KsCrewVehiclePushedComponent>(crate))
                slowestPushing = MathF.Min(slowestPushing, Vector2.Dot(HullVelocity(hull), facing));
            return false;
        });

        // Key let go: the hull slows; from the tick it no longer touches the crate, the crate is on its own.
        var clientCrate = ToClient(crate);
        var releasedAt = float.NaN;
        var releasedSpeed = 0f;
        var stalledTicks = 0;
        var previousServer = 0f;
        var previousClient = 0f;
        await Server.WaitPost(() => previousServer = Along(crate, facing));
        await Client.WaitPost(() => previousClient = Vector2.Dot(CEntMan.GetComponent<TransformComponent>(clientCrate).LocalPosition, facing));
        for (var tick = 0; tick < TickRate * 3; tick++)
        {
            await RunTicks(1);
            var serverStep = 0f;
            await Server.WaitPost(() =>
            {
                var along = Along(crate, facing);
                serverStep = along - previousServer;
                previousServer = along;
                if (float.IsNaN(releasedAt) && !HardBox(hull).Intersects(HardBox(crate).Enlarged(0.02f)))
                {
                    releasedAt = along;
                    releasedSpeed = Vector2.Dot(SComp<PhysicsComponent>(crate).LinearVelocity, facing);
                }
            });
            await Client.WaitPost(() =>
            {
                var along = Vector2.Dot(CEntMan.GetComponent<TransformComponent>(clientCrate).LocalPosition, facing);
                if (serverStep > 0.01f && along - previousClient < serverStep * 0.1f)
                    stalledTicks++;
                previousClient = along;
            });
        }

        var slid = 0f;
        await Server.WaitPost(() => slid = Along(crate, facing) - releasedAt);

        // The control: the same crate, the same speed, by hand.
        var controlStart = 0f;
        await Server.WaitPost(() =>
        {
            controlStart = Along(control, facing);
            SEntMan.System<Robust.Shared.Physics.Systems.SharedPhysicsSystem>().SetLinearVelocity(control, facing * releasedSpeed);
        });
        await RunTicks(TickRate * 3);
        var controlSlid = 0f;
        await Server.WaitPost(() => controlSlid = Along(control, facing) - controlStart);

        TestContext.Out.WriteLine($"crate: hull slowest {slowestPushing} of {TopSpeed(hull)} (share {share}), released at {releasedSpeed} m/s, slid {slid} m against {controlSlid} m, client stalled {stalledTicks} ticks");
        Assert.That(float.IsNaN(releasedAt), Is.False, "precondition: the crate came off the hull");
        Assert.Multiple(() =>
        {
            Assert.That(slowestPushing, Is.GreaterThan(TopSpeed(hull) * share * 0.9f),
                "pushing it, the hull should lose no more than the crate's share of its speed");
            Assert.That(slid, Is.LessThanOrEqualTo(controlSlid + 0.01f), "let go, it should slide no further than any crate");
            Assert.That(stalledTicks, Is.Zero, "and the driver's client should show it sliding, not stuck");
        });
    }

    /// <summary>
    ///     A driver who gets up mid-push stops predicting what the hull was pushing, though it slides on a while.
    /// </summary>
    [Test]
    public async Task ExDriverStopsPredictingWhatTheHullPushed()
    {
        var (scene, facing) = await DrivenAsync();
        EntityUid crate = default;
        await Server.WaitPost(() => crate = SEntMan.SpawnAtPosition(PushCratePrototype, OriginCoordinates.Offset(Start + facing * 3f)));
        await RunTicks(5);
        var pushedTicks = 0;
        await SetMovementKey(DirectionFlag.North, Robust.Shared.Input.BoundKeyState.Down);
        for (var tick = 0; tick < TickRate * 2 && pushedTicks < 10; tick++)
        {
            await RunTicks(1);
            await Server.WaitPost(() => pushedTicks += SEntMan.HasComponent<KsCrewVehiclePushedComponent>(crate) ? 1 : 0);
        }

        await Client.WaitAssertion(() =>
            Assert.That(CEntMan.GetComponent<PhysicsComponent>(ToClient(crate)).Predict, Is.True, "precondition: the driver's client predicts it"));

        // Up mid-push, the key still held.
        await Server.WaitPost(() => Server.System<Content.Shared.Buckle.SharedBuckleSystem>().Unbuckle((SPlayer, null), SPlayer));
        await RunTicks(3);
        await SetMovementKey(DirectionFlag.North, Robust.Shared.Input.BoundKeyState.Up);

        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.HasComponent<KsCrewVehiclePushedComponent>(crate), Is.True, "precondition: still sliding, still marked"));
        await Client.WaitAssertion(() =>
            Assert.That(CEntMan.GetComponent<PhysicsComponent>(ToClient(crate)).Predict, Is.False, "the ex-driver's client shouldn't predict it any longer"));
    }

    /// <summary>
    ///     The markers a moving hull leaves on what it touches are runtime state: a map saved meanwhile carries none of
    ///         them, and one left with nothing to say (as a save from before could load it) is let go.
    /// </summary>
    [Test]
    public async Task RunningMarkersAreNeitherSavedNorKept()
    {
        // On a grid of its own, so that the save holds nothing but it: a crate with every marker at once, as each could
        //      be on something a hull was moving when the map was saved.
        EntityUid grid = default, crate = default;
        await Server.WaitPost(() =>
        {
            var gridEntity = MapSystem.CreateGridEntity(MapData.MapId);
            grid = gridEntity.Owner;
            MapSystem.SetTile(gridEntity, Vector2i.Zero, MapData.Tile.Tile);
            Transform.SetWorldPosition(grid, new Vector2(FloorRadius * 4f, 0f));
            crate = SEntMan.SpawnAtPosition(PushCratePrototype, new Robust.Shared.Map.EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            SEntMan.EnsureComponent<Content.Shared._KS14.CrewVehicle.RunOver.KsCrewVehicleUnderHullComponent>(crate);
            SEntMan.EnsureComponent<Content.Shared._KS14.CrewVehicle.RunOver.KsCrewVehicleStruckComponent>(crate);
            SEntMan.EnsureComponent<Content.Shared._KS14.CrewVehicle.Smash.KsCrewVehicleSmashedComponent>(crate);
            SEntMan.EnsureComponent<Content.Shared._KS14.CrewVehicle.CrashLockout.KsCrewVehicleCrashLockedComponent>(crate);
            SEntMan.EnsureComponent<KsCrewVehiclePushedComponent>(crate);
        });

        await Server.WaitAssertion(() =>
        {
            var writer = new System.IO.StringWriter();
            Assert.That(SEntMan.System<Robust.Shared.EntitySerialization.Systems.MapLoaderSystem>().TrySaveGrid(grid, writer), Is.True);
            var saved = writer.ToString();
            Assert.That(saved, Does.Contain($"proto: {PushCratePrototype.Id}"), "precondition: the crate is saved");
            foreach (var marker in new[] { "KsCrewVehiclePushed", "KsCrewVehicleUnderHull", "KsCrewVehicleStruck", "KsCrewVehicleSmashed", "KsCrewVehicleCrashLocked" })
                Assert.That(saved, Does.Not.Contain($"type: {marker}\n").And.Not.Contain($"type: {marker}\r"), $"{marker} shouldn't be saved");
        });

        // And one naming no hull, as such a save from before would load, is let go.
        await RunTicks(2);
        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.HasComponent<KsCrewVehiclePushedComponent>(crate), Is.False, "a pushed marker naming no hull is let go"));
    }
    /// <summary>
    ///     A parked hull the driven one's own weight: struck, both go on at half its speed.
    /// </summary>
    [Test]
    public async Task ParkedHullIsShovedAtHalfSpeed()
    {
        var (scene, facing) = await DrivenAsync();
        var parked = await SpawnHullAsync(Start + facing * 5f);
        var impactSpeed = 0f;
        var parkedSpeed = 0f;
        await HoldAsync(scene.Hull, DirectionFlag.North, TickRate * 2, () =>
        {
            var speed = Vector2.Dot(HullVelocity(parked.Hull), facing);
            if (speed > 0.01f && parkedSpeed == 0f)
            {
                parkedSpeed = speed;
                return true;
            }

            impactSpeed = Vector2.Dot(HullVelocity(scene.Hull), facing);
            return false;
        });

        // Half, less what a step or two of the parked hull's ground friction takes.
        TestContext.Out.WriteLine($"parked: struck at {impactSpeed} m/s, shoved to {parkedSpeed} m/s");
        Assert.That(parkedSpeed / impactSpeed, Is.InRange(0.35f, 0.55f), "struck, a parked hull should take half the speed");
    }
}
