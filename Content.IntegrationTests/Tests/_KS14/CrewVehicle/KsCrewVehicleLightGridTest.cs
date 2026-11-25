#nullable enable
using System.Numerics;
using System.Threading.Tasks;
using Content.Server.Gravity;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.CrewVehicle;
using Content.Shared.Gravity;
using Content.Shared.Maps;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._KS14.CrewVehicle;

/// <summary>
///     A hull on a light, loose grid, as a shuttle is: parked on it, driven, turned and braked on it, or driven onto it
///         across a seam, it leaves the grid where it was. The hull stands on a grid; it never pushes it.
/// </summary>
[TestOf(typeof(Content.Shared._KS14.CrewVehicle.Push.KsCrewVehiclePushController))]
public sealed class KsCrewVehicleLightGridTest : KsCrewVehicleDriveTestBase
{
    private const int LightGridRadius = 4;

    /// <summary>How far a grid may move, in metres, and turn, in degrees, and still count as where it was.</summary>
    private const float StillDistance = 0.001f;
    private const double StillDegrees = 0.001;

    private async Task<EntityUid> SpawnLightGridAsync(Vector2 worldPosition)
    {
        EntityUid gridUid = default;
        await Server.WaitPost(() =>
        {
            var grid = MapSystem.CreateGridEntity(MapData.MapId);
            gridUid = grid.Owner;
            var tile = new Tile(ProtoMan.Index<ContentTileDefinition>(Plating).TileId);
            for (var x = -LightGridRadius; x <= LightGridRadius; x++)
            {
                for (var y = -LightGridRadius; y <= LightGridRadius; y++)
                {
                    MapSystem.SetTile(grid.Owner, grid.Comp, new Vector2i(x, y), tile);
                }
            }

            Transform.SetWorldPosition(grid.Owner, worldPosition);
            var gravityComponent = SEntMan.EnsureComponent<GravityComponent>(grid.Owner);
            SEntMan.System<GravitySystem>().EnableGravity(grid.Owner, gravityComponent);
        });
        await RunTicks(5);
        return gridUid;
    }

    private (Vector2 Position, Angle Rotation) Pose(EntityUid grid) =>
        (Transform.GetWorldPosition(grid), Transform.GetWorldRotation(grid));

    [TestCase("parked hull", false)]
    [TestCase("crate", false)]
    [TestCase("walking mob", false)]
    [TestCase("driven hull", false)]
    [TestCase("hull driven onto it", false)]
    // Control: the air the open test grid trades with the map does move it, so this would see a grid that moved.
    [TestCase("crate", true)]
    public async Task LightGridStaysPut(string what, bool spacingThrust)
    {
        if (spacingThrust)
            await Server.WaitPost(() => Server.CfgMan.SetCVar(KsCCVars.AtmosSpacingThrust, true));

        // Far from the floor grid; or, for driving onto it, butted against its east edge.
        var floorEdge = FloorRadius + 1;
        var gridPosition = what == "hull driven onto it"
            ? new Vector2(floorEdge + LightGridRadius, 0f)
            : new Vector2(FloorRadius * 5f, 0f);
        Vector2 gridPositionOffset = default;
        await Server.WaitPost(() => gridPositionOffset = Transform.GetWorldPosition(MapData.Grid.Owner));
        var grid = await SpawnLightGridAsync(gridPositionOffset + gridPosition);
        var onGrid = new EntityCoordinates(grid, new Vector2(0.5f, 0.5f));
        (Vector2 Position, Angle Rotation) start = default;

        switch (what)
        {
            case "parked hull":
            case "driven hull":
            {
                var scene = await SpawnHullAsync(Vector2.Zero);
                await Server.WaitPost(() => Transform.SetCoordinates(scene.Hull, onGrid.Offset(new Vector2(0f, -2f))));
                await RunTicks(2);
                await Server.WaitPost(() => start = Pose(grid));
                if (what == "parked hull")
                {
                    await RunTicks(TickRate * 3);
                    break;
                }

                await PutInsideAsync(scene);
                await SitAsync(scene.DriverSeat);
                await HoldAsync(scene.Hull, DirectionFlag.North, TickRate);
                await HoldAsync(scene.Hull, DirectionFlag.East, TickRate);
                await HoldAsync(scene.Hull, DirectionFlag.South, TickRate / 2);
                await SettleAsync(scene.Hull);
                break;
            }
            case "hull driven onto it":
            {
                var scene = await SpawnHullAsync(new Vector2(FloorRadius - 3, 0f));
                await Server.WaitPost(() => Transform.SetWorldRotation(scene.Hull, new Vector2(1f, 0f).ToWorldAngle()));
                await PutInsideAsync(scene);
                await SitAsync(scene.DriverSeat);
                await Server.WaitPost(() => start = Pose(grid));
                var crossed = false;
                await HoldAsync(scene.Hull, DirectionFlag.North, TickRate * 3, () =>
                {
                    crossed |= SComp<TransformComponent>(scene.Hull).GridUid == grid;
                    return crossed && Vector2.Dot(Transform.GetWorldPosition(scene.Hull) - Transform.GetWorldPosition(grid), new Vector2(1f, 0f)) > 0f;
                });
                await SettleAsync(scene.Hull);
                await Server.WaitAssertion(() => Assert.That(crossed, Is.True, "precondition: the hull drove onto the light grid"));
                break;
            }
            case "crate":
                await Server.WaitPost(() =>
                {
                    SEntMan.SpawnAtPosition(PushCratePrototype, onGrid);
                    start = Pose(grid);
                });
                await RunTicks(TickRate * 3);
                break;
            case "walking mob":
                await Server.WaitPost(() => Transform.SetCoordinates(SPlayer, onGrid));
                await RunTicks(2);
                await Server.WaitPost(() => start = Pose(grid));
                await Move(DirectionFlag.North, 1f);
                await Move(DirectionFlag.East, 1f);
                await Move(DirectionFlag.South, 1f);
                await RunTicks(TickRate);
                break;
        }

        var moved = 0f;
        var turned = 0.0;
        await Server.WaitPost(() =>
        {
            var (position, rotation) = Pose(grid);
            moved = (position - start.Position).Length();
            turned = System.Math.Abs(Angle.ShortestDistance(start.Rotation, rotation).Degrees);
        });
        TestContext.Out.WriteLine($"{what}, spacing thrust {spacingThrust}: grid moved {moved} m, turned {turned}°");

        if (spacingThrust)
        {
            Assert.That(moved, Is.GreaterThan(StillDistance), "control: the traded air moves the grid, which this sees");
            return;
        }

        Assert.Multiple(() =>
        {
            Assert.That(moved, Is.LessThan(StillDistance), $"a {what} shouldn't move the grid it is on");
            Assert.That(turned, Is.LessThan(StillDegrees), "nor turn it");
        });
    }
}
