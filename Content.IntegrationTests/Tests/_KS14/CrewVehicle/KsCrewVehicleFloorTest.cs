#nullable enable
using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server.Gravity;
using Content.Shared._KS14.CrewVehicle;
using Content.Shared._KS14.CrewVehicle.Floor;
using Content.Shared.Gravity;
using Content.Shared.Maps;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;

namespace Content.IntegrationTests.Tests._KS14.CrewVehicle;

/// <summary>
///     A hull that isn't opted in stops at the edge of the floor as at a wall: driven, turned, pushed or coasting, it
///         never puts any of itself over space. Each test's control is the opted-in hull, which goes on over.
/// </summary>
/// <remarks>
///     The floor is the test grid's tiles, -<see cref="KsCrewVehicleDriveTestBase.FloorRadius"/> to
///         +<see cref="KsCrewVehicleDriveTestBase.FloorRadius"/>, so its east edge is at grid x = FloorRadius + 1. Hulls
///         spawn at offsets from the origin tile's centre, grid (0.5, 0.5).
/// </remarks>
[TestOf(typeof(KsCrewVehicleFloorSystem))]
[TestOf(typeof(KsCrewVehicleFloorController))]
public sealed class KsCrewVehicleFloorTest : KsCrewVehicleDriveTestBase
{
    /// <summary>The floor's east edge, in grid coordinates.</summary>
    private const float EastEdge = FloorRadius + 1;

    /// <summary>How far past an edge a hull's body may seem to be: the floor check's points sit just inside its rim.</summary>
    private const float Slack = 0.03f;

    private static readonly Angle East = new Vector2(1f, 0f).ToWorldAngle();

    /// <summary>The offset from the origin that puts a hull's centre at grid x <paramref name="gridX"/>.</summary>
    private static Vector2 AtGridX(float gridX) => new(gridX - 0.5f, 0f);

    /// <summary>The hull's solid body, in grid coordinates.</summary>
    private Box2 GridBox(EntityUid uid) => HardBox(uid).Translated(-Transform.GetWorldPosition(MapData.Grid.Owner));

    private async Task<HullScene> DrivenEastAsync(float gridX, Robust.Shared.Prototypes.EntProtoId? prototype = null)
    {
        var scene = await SpawnHullAsync(AtGridX(gridX), prototype);
        await Server.WaitPost(() => Transform.SetWorldRotation(scene.Hull, East));
        await RunTicks(2);
        await PutInsideAsync(scene);
        await SitAsync(scene.DriverSeat);
        return scene;
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task StopsAtTheEdge(bool driveOffGrid)
    {
        var scene = await DrivenEastAsync(EastEdge - 5f, driveOffGrid ? OffGridHullPrototype : HullPrototype);
        var hull = scene.Hull;
        var furthest = float.MinValue;
        var lastSecond = new float[TickRate];
        var tick = 0;
        await HoldAsync(hull, DirectionFlag.North, TickRate * 4, () =>
        {
            var right = SEntMan.Deleted(hull) ? furthest : GridBox(hull).Right;
            furthest = MathF.Max(furthest, right);
            lastSecond[tick++ % TickRate] = HullPosition(hull).X;
            return false;
        });

        await Server.WaitAssertion(() =>
        {
            if (driveOffGrid)
            {
                Assert.That(furthest, Is.GreaterThan(EastEdge + 0.5f), "control: an opted-in hull drives on off the edge");
                return;
            }

            Assert.That(furthest, Is.LessThanOrEqualTo(EastEdge + Slack), "the hull never goes over the edge");
            Assert.That(GridBox(hull).Right, Is.GreaterThan(EastEdge - TopSpeed(hull) / TickRate - Slack),
                "it stops at the edge, within a step of it");
            Assert.That(MathF.Abs(lastSecond.Max() - lastSecond.Min()), Is.LessThan(0.001f), "and holds still there");
        });
    }

    /// <summary>
    ///     The driver's client predicts the stop: its hull never goes over, nor steps back from it.
    /// </summary>
    [Test]
    public async Task StopsAtTheEdgeOnTheDriversClient()
    {
        var scene = await DrivenEastAsync(EastEdge - 5f);
        var clientHull = ToClient(scene.Hull);
        var clientGrid = ToClient(MapData.Grid.Owner);
        var physicsSystem = Client.System<Robust.Shared.Physics.Systems.SharedPhysicsSystem>();
        var transformSystem = Client.System<SharedTransformSystem>();
        Box2 ClientBox() => physicsSystem.GetHardAABB(clientHull).Translated(-transformSystem.GetWorldPosition(clientGrid));

        await SetMovementKey(DirectionFlag.North, Robust.Shared.Input.BoundKeyState.Down);
        var furthest = float.MinValue;
        var previous = float.MinValue;
        var worstStepBack = 0f;
        for (var tick = 0; tick < TickRate * 4; tick++)
        {
            await RunTicks(1);
            await Client.WaitPost(() =>
            {
                var right = ClientBox().Right;
                furthest = MathF.Max(furthest, right);
                if (previous > float.MinValue)
                    worstStepBack = MathF.Max(worstStepBack, previous - right);
                previous = right;
            });
        }

        await SetMovementKey(DirectionFlag.North, Robust.Shared.Input.BoundKeyState.Up);
        Assert.Multiple(() =>
        {
            Assert.That(furthest, Is.GreaterThan(EastEdge - 0.5f), "precondition: it reached the edge");
            Assert.That(furthest, Is.LessThanOrEqualTo(EastEdge + Slack), "the driver's client never shows it over the edge");
            Assert.That(worstStepBack, Is.LessThan(0.001f), "nor stepping back from it");
        });
    }

    /// <summary>
    ///     Nose to the edge, a tracked hull can't turn on the spot: either way, a corner would swing out over space.
    /// </summary>
    [TestCase(0.05f)]
    [TestCase(0.01f)]
    [TestCase(2.2f)]
    public async Task TurningAtTheEdgeIsRefused(float noseGap)
    {
        // Its body is 1.6 long, its nose this far inside the edge. At the edge it may turn only until a corner reaches the
        //      edge, a few degrees, as against a wall; two tiles in it turns freely.
        var atTheEdge = noseGap < 1f;
        var scene = await DrivenEastAsync(EastEdge - 0.8f - noseGap);
        var hull = scene.Hull;
        var furthest = float.MinValue;
        await HoldAsync(hull, DirectionFlag.East, TickRate, () =>
        {
            furthest = MathF.Max(furthest, GridBox(hull).Right);
            return false;
        });

        await Server.WaitAssertion(() =>
        {
            var turned = Math.Abs(Angle.ShortestDistance(East, HullRotation(hull)).Degrees);
            if (!atTheEdge)
            {
                Assert.That(turned, Is.GreaterThan(45), "control: away from the edge it turns");
                return;
            }

            Assert.That(turned, Is.LessThan(10), "at the edge it can't turn round");
            Assert.That(furthest, Is.LessThanOrEqualTo(EastEdge + Slack), "and no corner goes over");
        });
    }

    /// <summary>
    ///     A parked hull pushed to the edge stops there, and so does the hull pushing it.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task PushedHullStopsAtTheEdge(bool driveOffGrid)
    {
        var parked = await SpawnHullAsync(AtGridX(EastEdge - 1.5f), driveOffGrid ? OffGridHullPrototype : HullPrototype);
        await Server.WaitPost(() => Transform.SetWorldRotation(parked.Hull, East));
        var scene = await DrivenEastAsync(EastEdge - 5f);
        var furthest = float.MinValue;
        var overlapped = false;
        // Pushing a hull its own weight is slow.
        await HoldAsync(scene.Hull, DirectionFlag.North, TickRate * 8, () =>
        {
            furthest = MathF.Max(furthest, GridBox(parked.Hull).Right);
            overlapped |= HardBox(scene.Hull).Intersects(HardBox(parked.Hull).Enlarged(-0.05f));
            return false;
        });

        await Server.WaitAssertion(() =>
        {
            Assert.That(overlapped, Is.False, "the pushing hull never drives into the pushed one");
            if (driveOffGrid)
            {
                Assert.That(furthest, Is.GreaterThan(EastEdge + 0.5f), "control: an opted-in hull is pushed off");
                return;
            }

            Assert.That(furthest, Is.GreaterThan(EastEdge - 0.5f), "precondition: it was pushed to the edge");
            Assert.That(furthest, Is.LessThanOrEqualTo(EastEdge + Slack), "the pushed hull stops at the edge");
            Assert.That(HullVelocity(scene.Hull).Length(), Is.LessThan(0.1f), "and the pushing one stops against it");
        });
    }

    /// <summary>
    ///     A parked hull rolling toward the edge, nobody at the wheel, stops at it. Kept rolling each tick, as by a slope
    ///         or a shove: on its own it would come to rest long before.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task CoastingHullStopsAtTheEdge(bool driveOffGrid)
    {
        var scene = await SpawnHullAsync(AtGridX(EastEdge - 2f), driveOffGrid ? OffGridHullPrototype : HullPrototype);
        await Server.WaitPost(() => Transform.SetWorldRotation(scene.Hull, East));

        var physicsSystem = Server.System<Robust.Shared.Physics.Systems.SharedPhysicsSystem>();
        var furthest = float.MinValue;
        for (var tick = 0; tick < TickRate * 2; tick++)
        {
            await Server.WaitPost(() => physicsSystem.SetLinearVelocity(scene.Hull, new Vector2(2f, 0f)));
            await RunTicks(1);
            await Server.WaitPost(() => furthest = MathF.Max(furthest, GridBox(scene.Hull).Right));
        }

        await Server.WaitAssertion(() =>
        {
            Assert.That(SComp<PhysicsComponent>(scene.Hull).BodyType, Is.EqualTo(Robust.Shared.Physics.BodyType.Dynamic), "precondition: parked");
            if (driveOffGrid)
            {
                Assert.That(furthest, Is.GreaterThan(EastEdge + Slack), "control: an opted-in hull rolls on over");
                return;
            }

            Assert.That(furthest, Is.LessThanOrEqualTo(EastEdge + Slack), "a rolling hull stops at the edge");
        });
    }

    /// <summary>
    ///     A hull left hanging over the edge, its centre still on the floor, can back onto it but not drive further off.
    /// </summary>
    [Test]
    public async Task HalfOffHullCanOnlyBackOn()
    {
        var scene = await DrivenEastAsync(EastEdge - 0.4f);
        var hull = scene.Hull;
        var start = 0f;
        await Server.WaitPost(() => start = HullPosition(hull).X);
        await HoldAsync(hull, DirectionFlag.North, TickRate);
        var afterForward = 0f;
        await Server.WaitPost(() => afterForward = HullPosition(hull).X);
        await HoldAsync(hull, DirectionFlag.South, TickRate);
        await Server.WaitAssertion(() =>
        {
            Assert.That(afterForward - start, Is.LessThan(0.001f), "it can't drive further off");
            Assert.That(HullPosition(hull).X, Is.LessThan(afterForward - 0.1f), "but it backs on");
        });
    }

    /// <summary>
    ///     A hull out in space can't be driven or turned at all.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task OffGridHullCantDrive(bool driveOffGrid)
    {
        var scene = await DrivenEastAsync(EastEdge + 4f, driveOffGrid ? OffGridHullPrototype : HullPrototype);
        var hull = scene.Hull;
        var start = Vector2.Zero;
        await Server.WaitPost(() => start = Transform.GetWorldPosition(hull));
        await Server.WaitAssertion(() =>
            Assert.That(SComp<TransformComponent>(hull).GridUid, Is.Null, "precondition: off any grid"));
        await HoldAsync(hull, DirectionFlag.North, TickRate);
        await HoldAsync(hull, DirectionFlag.East, TickRate / 2);

        await Server.WaitAssertion(() =>
        {
            var moved = (Transform.GetWorldPosition(hull) - start).Length();
            var turned = Math.Abs(Angle.ShortestDistance(East, HullRotation(hull)).Degrees);
            if (driveOffGrid)
            {
                Assert.That(moved + turned, Is.GreaterThan(0.05f), "control: an opted-in hull drives or turns in space");
                return;
            }

            Assert.That(moved, Is.LessThan(0.001f), "off the floor it can't be driven");
            Assert.That(turned, Is.LessThan(0.01), "nor turned");
        });
    }

    /// <summary>
    ///     A one-tile gap to another grid stops the hull at the near edge: it can't bridge it.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task StopsAtAOneTileGap(bool driveOffGrid)
    {
        EntityUid secondGrid = default;
        await Server.WaitPost(() =>
        {
            var grid = MapSystem.CreateGridEntity(MapData.MapId);
            secondGrid = grid.Owner;
            var tile = new Tile(ProtoMan.Index<ContentTileDefinition>(Plating).TileId);
            for (var x = 0; x <= 8; x++)
            {
                for (var y = -4; y <= 4; y++)
                {
                    MapSystem.SetTile(grid.Owner, grid.Comp, new Vector2i(x, y), tile);
                }
            }

            Transform.SetWorldPosition(grid.Owner, Transform.GetWorldPosition(MapData.Grid.Owner) + new Vector2(EastEdge + 1f, 0f));
            var gravityComponent = SEntMan.EnsureComponent<GravityComponent>(grid.Owner);
            SEntMan.System<GravitySystem>().EnableGravity(grid.Owner, gravityComponent);
        });

        var scene = await DrivenEastAsync(EastEdge - 5f, driveOffGrid ? OffGridHullPrototype : HullPrototype);
        var hull = scene.Hull;
        var furthest = float.MinValue;
        var crossed = false;
        await HoldAsync(hull, DirectionFlag.North, TickRate * 4, () =>
        {
            furthest = MathF.Max(furthest, GridBox(hull).Right);
            crossed |= SComp<TransformComponent>(hull).GridUid == secondGrid;
            return crossed;
        });

        await Server.WaitAssertion(() =>
        {
            if (driveOffGrid)
            {
                Assert.That(crossed, Is.True, "control: an opted-in hull crosses the gap");
                return;
            }

            Assert.That(crossed, Is.False, "the hull doesn't cross the gap");
            Assert.That(furthest, Is.LessThanOrEqualTo(EastEdge + Slack), "it stops at the near edge");
        });
    }
}
