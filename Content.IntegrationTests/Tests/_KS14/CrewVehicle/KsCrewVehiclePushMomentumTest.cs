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
