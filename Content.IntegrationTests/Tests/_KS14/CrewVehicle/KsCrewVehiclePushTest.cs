#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Content.Shared._KS14.CrewVehicle;
using Content.Shared._KS14.CrewVehicle.Push;
using Content.Shared.Throwing;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.CrewVehicle;

/// <summary>
///     A driven hull shoves what it drives into, and ignores items altogether. Every number comes from the test
///         prototypes.
/// </summary>
[TestOf(typeof(KsCrewVehiclePushController))]
public sealed class KsCrewVehiclePushTest : KsCrewVehicleDriveTestBase
{
    private static readonly EntProtoId Casing = "CartridgeLightRifle";
    private static readonly EntProtoId DroppedItem = "Crowbar";

    private async Task<(HullScene Scene, Vector2 Facing)> DrivenAsync(EntProtoId? prototype = null)
    {
        var scene = await SpawnHullAsync(Vector2.Zero, prototype);
        await PutInsideAsync(scene);
        await SitAsync(scene.DriverSeat);
        var facing = Vector2.Zero;
        await Server.WaitPost(() => facing = KsCrewVehicleHullFrame.Forward(HullRotation(scene.Hull)));
        return (scene, facing);
    }

    private async Task<EntityUid> SpawnAheadAsync(EntProtoId prototype, Vector2 facing, float distance)
    {
        EntityUid uid = default;
        await Server.WaitPost(() => uid = SEntMan.SpawnAtPosition(prototype, OriginCoordinates.Offset(facing * distance)));
        await RunTicks(2);
        return uid;
    }

    private float Along(EntityUid uid, Vector2 facing) => Vector2.Dot(HullPosition(uid), facing);

    [Test]
    public async Task PushesALooseCrate()
    {
        var (scene, facing) = await DrivenAsync();
        var hull = scene.Hull;
        var crate = await SpawnAheadAsync(PushCratePrototype, facing, 3f);
        var crateStart = 0f;
        await Server.WaitPost(() => crateStart = Along(crate, facing));

        var overlapped = false;
        var slowestWhilePushing = float.MaxValue;
        await HoldAsync(hull, DirectionFlag.North, TickRate * 3, () =>
        {
            overlapped |= HardBox(hull).Intersects(HardBox(crate).Enlarged(-0.05f));
            if (Along(crate, facing) > crateStart + 0.5f)
                slowestWhilePushing = MathF.Min(slowestWhilePushing, HullVelocity(hull).Length());
            return false;
        });

        await Server.WaitAssertion(() =>
        {
            Assert.That(Along(crate, facing) - crateStart, Is.GreaterThan(2f), "the crate should be pushed ahead");
            Assert.That(overlapped, Is.False, "and the hull shouldn't pass into it");
            Assert.That(slowestWhilePushing, Is.GreaterThan(0.1f), "the hull keeps moving while it pushes");
            Assert.That(slowestWhilePushing, Is.LessThan(TopSpeed(hull)), "slower than it drives unloaded");
        });
    }

    [Test]
    public async Task CrateIntoAWallStopsTheHull()
    {
        var (scene, facing) = await DrivenAsync();
        var hull = scene.Hull;
        var crate = await SpawnAheadAsync(PushCratePrototype, facing, 3f);
        var wall = await SpawnAheadAsync(WallPrototype, facing, 6f);

        var crateIntoWall = false;
        var hullIntoCrate = false;
        await HoldAsync(hull, DirectionFlag.North, TickRate * 4, () =>
        {
            crateIntoWall |= HardBox(crate).Intersects(HardBox(wall).Enlarged(-0.1f));
            hullIntoCrate |= HardBox(hull).Intersects(HardBox(crate).Enlarged(-0.1f));
            return false;
        });

        await Server.WaitAssertion(() =>
        {
            Assert.That(crateIntoWall, Is.False, "the wall stops the crate");
            Assert.That(hullIntoCrate, Is.False, "and the crate the hull");
            Assert.That(HullVelocity(hull).Length(), Is.LessThan(0.5f), "which ends up stopped");
        });
    }

    [Test]
    public async Task PushesAParkedHull()
    {
        var (scene, facing) = await DrivenAsync();
        var parked = await SpawnHullAsync(facing * 4f);
        var parkedStart = 0f;
        await Server.WaitAssertion(() =>
        {
            Assert.That(SComp<PhysicsComponent>(parked.Hull).BodyType, Is.EqualTo(BodyType.Dynamic), "precondition: parked");
            parkedStart = Along(parked.Hull, facing);
        });

        await HoldAsync(scene.Hull, DirectionFlag.North, TickRate * 3);
        await Server.WaitAssertion(() =>
            Assert.That(Along(parked.Hull, facing) - parkedStart, Is.GreaterThan(0.5f), "a parked hull should be shoved"));
    }

    /// <summary>
    ///     Driven into another driven hull, it shoves it; against one that is up against a wall, it stops. Never
    ///         driving into it either way, though physics would let them pass through each other.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task ShovesADrivenHull(bool theirsAgainstAWall)
    {
        var (ours, facing) = await DrivenAsync();
        var theirs = await SpawnHullAsync(facing * 4f);
        if (theirsAgainstAWall)
            await SpawnAheadAsync(WallPrototype, facing, 5.5f);

        EntityUid otherDriver = default;
        await Server.WaitPost(() =>
            otherDriver = SEntMan.SpawnAtPosition(Human, SComp<TransformComponent>(theirs.DriverSeat).Coordinates));
        await RunTicks(5);
        await SitAsync(theirs.DriverSeat, otherDriver);

        var theirStart = 0f;
        await Server.WaitAssertion(() =>
        {
            Assert.That(SComp<PhysicsComponent>(theirs.Hull).BodyType, Is.EqualTo(BodyType.KinematicController),
                "precondition: the other hull is being driven");
            theirStart = Along(theirs.Hull, facing);
        });

        var overlapped = false;
        await HoldAsync(ours.Hull, DirectionFlag.North, TickRate * 4, () =>
        {
            overlapped |= HardBox(ours.Hull).Intersects(HardBox(theirs.Hull).Enlarged(-0.05f));
            return false;
        });

        await Server.WaitAssertion(() =>
        {
            Assert.That(overlapped, Is.False, "a driven hull never drives into another");
            if (theirsAgainstAWall)
                Assert.That(HullVelocity(ours.Hull).Length(), Is.LessThan(0.5f), "against one that can't give way, it stops");
            else
                Assert.That(Along(theirs.Hull, facing) - theirStart, Is.GreaterThan(1f), "it shoves the other hull ahead");
        });
    }

    /// <summary>
    ///     Casings and dropped items lie under a hull's notice: it drives over them at full speed, leaving them where
    ///         they lie. They are kept colliding as they would be just after they land: an item at rest stops colliding
    ///         at all (<see cref="CollisionWakeComponent"/>), which would make this pass whatever the hull does.
    /// </summary>
    [Test]
    public async Task DrivesOverItems()
    {
        var (scene, facing) = await DrivenAsync();
        var hull = scene.Hull;
        var casing = await SpawnAheadAsync(Casing, facing, 3f);
        var item = await SpawnAheadAsync(DroppedItem, facing, 4f);
        var starts = new Dictionary<EntityUid, Vector2>();
        await Server.WaitPost(() =>
        {
            foreach (var uid in new[] { casing, item })
            {
                SEntMan.RemoveComponent<CollisionWakeComponent>(uid);
                SEntMan.System<Robust.Shared.Physics.Systems.SharedPhysicsSystem>().SetCanCollide(uid, true);
                starts[uid] = HullPosition(uid);
            }
        });

        // Up to speed first, then over them.
        await HoldAsync(hull, DirectionFlag.North, TickRate / 2);
        var slowest = float.MaxValue;
        await HoldAsync(hull, DirectionFlag.North, TickRate * 2, () =>
        {
            slowest = MathF.Min(slowest, HullVelocity(hull).Length());
            return false;
        });

        await Server.WaitAssertion(() =>
        {
            Assert.That(Along(hull, facing), Is.GreaterThan(5f), "the hull drives on past them");
            Assert.That(slowest, Is.GreaterThan(TopSpeed(hull) * 0.95f), "it doesn't slow for them");
            foreach (var (uid, start) in starts)
                Assert.That((HullPosition(uid) - start).Length(), Is.LessThan(0.05f), $"{SEntMan.ToPrettyString(uid)} isn't moved");
        });
    }

    /// <summary>
    ///     A gun's casings flung out around and under a hull, as a gun ejects them, move neither a parked hull nor one
    ///         being driven but standing still. Control: a hull that still collides with items is shoved by them.
    /// </summary>
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public async Task EjectedCasingsDontMoveAHull(bool driven, bool collidesWithItems)
    {
        var prototype = collidesWithItems ? ItemColliderHullPrototype : HullPrototype;
        HullScene scene;
        if (driven)
            (scene, _) = await DrivenAsync(prototype);
        else
            scene = await SpawnHullAsync(Vector2.Zero, prototype);

        await RunTicks(TickRate / 2);
        var hull = scene.Hull;
        var start = Vector2.Zero;
        await Server.WaitPost(() =>
        {
            start = HullPosition(hull);
            var throwingSystem = SEntMan.System<ThrowingSystem>();
            var coordinates = SComp<TransformComponent>(hull).Coordinates;
            // As SharedGunSystem.EjectCartridge flings them: a nudge and a spin, a burst's worth.
            for (var i = 0; i < 20; i++)
            {
                var casingUid = SEntMan.SpawnAtPosition(Casing, coordinates);
                throwingSystem.TryThrow(casingUid, Angle.FromDegrees(i * 37).ToVec() / 100, 5f);
            }
        });

        var moved = 0f;
        var fastest = 0f;
        for (var tick = 0; tick < TickRate * 2; tick++)
        {
            await RunTicks(1);
            await Server.WaitPost(() =>
            {
                moved = MathF.Max(moved, (HullPosition(hull) - start).Length());
                fastest = MathF.Max(fastest, HullVelocity(hull).Length());
            });
        }

        if (collidesWithItems)
        {
                        // Parked, a heavy Dynamic hull shrugs them off to a few millimetres; driven, a kinematic hull can't push
            //      a loose body at all, so the casings shove it as walls would.
            TestContext.Out.WriteLine($"control, driven {driven}: moved {moved} m, fastest {fastest} m/s");
            Assert.That(moved, Is.GreaterThan(0.001f), "control: a hull that collides with items is shoved by its casings");
        }
        else
        {
            Assert.That(fastest, Is.Zero, "casings should never set the hull moving");
            Assert.That(moved, Is.LessThan(0.001f), "nor move it");
        }
    }
}
