#nullable enable
using System;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server.Gravity;
using Content.Shared._KS14.CrewVehicle;
using Content.Shared.Gravity;
using Content.Shared.Maps;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._KS14.CrewVehicle;

/// <summary>
///     Driving off one grid onto another that touches it: the hull keeps its speed across the seam, and the driver's
///         client sees it go smoothly, without a jump.
/// </summary>
[TestOf(typeof(Content.Shared._KS14.CrewVehicle.Drive.KsCrewVehicleDriveSystem))]
public sealed class KsCrewVehicleSeamTest : KsCrewVehicleDriveTestBase
{
    [Test]
    public async Task CrossingAGridSeam()
    {
        var scene = await SpawnHullAsync(new Vector2(FloorRadius - 4, 0f));
        var hull = scene.Hull;
        EntityUid secondGrid = default;
        await Server.WaitPost(() =>
        {
            // Facing east, at the first grid's east edge, with a second grid butted up against it.
            var east = new Vector2(1f, 0f).ToWorldAngle();
            Transform.SetWorldRotation(hull, east);

            var grid = MapSystem.CreateGridEntity(MapData.MapId);
            secondGrid = grid.Owner;
            var tile = new Tile(ProtoMan.Index<ContentTileDefinition>(Plating).TileId);
            for (var x = 0; x <= 12; x++)
            {
                for (var y = -4; y <= 4; y++)
                {
                    MapSystem.SetTile(grid.Owner, grid.Comp, new Vector2i(x, y), tile);
                }
            }

            var firstGridPosition = Transform.GetWorldPosition(MapData.Grid);
            Transform.SetWorldPosition(grid.Owner, firstGridPosition + new Vector2(FloorRadius + 1, 0f));
            var gravityComponent = SEntMan.EnsureComponent<GravityComponent>(grid.Owner);
            SEntMan.System<GravitySystem>().EnableGravity(grid.Owner, gravityComponent);
        });
        await RunTicks(5);
        await PutInsideAsync(scene);
        await SitAsync(scene.DriverSeat);

        // Up to speed on the first grid.
        await HoldAsync(hull, DirectionFlag.North, TickRate);

        var clientHull = ToClient(hull);
        var forward = Vector2.Zero;
        await Server.WaitPost(() => forward = KsCrewVehicleHullFrame.Forward(HullRotation(hull)));
        var slowest = float.MaxValue;
        var crossed = false;
        var clientPrevious = 0f;
        var worstClientStep = 0f;
        await Client.WaitPost(() => clientPrevious = Vector2.Dot(Client.System<SharedTransformSystem>().GetWorldPosition(clientHull), forward));
        var weightlessTicks = 0;

        await SetMovementKey(DirectionFlag.North, Robust.Shared.Input.BoundKeyState.Down);
        for (var tick = 0; tick < TickRate * 2; tick++)
        {
            await RunTicks(1);
            await Server.WaitPost(() =>
            {
                slowest = MathF.Min(slowest, HullVelocity(hull).Length());
                crossed |= SComp<TransformComponent>(hull).GridUid == secondGrid;
                if (SEntMan.System<SharedGravitySystem>().IsWeightless(hull))
                    weightlessTicks++;
            });
            await Client.WaitPost(() =>
            {
                var along = Vector2.Dot(Client.System<SharedTransformSystem>().GetWorldPosition(clientHull), forward);
                // A sample can span two of the client's ticks: what counts is how far off a whole number of ticks' travel.
                var step = along - clientPrevious;
                var expected = TopSpeed(hull) / TickRate;
                worstClientStep = MathF.Max(worstClientStep, MathF.Abs(step - MathF.Round(step / expected) * expected));
                clientPrevious = along;
            });
        }

        await SetMovementKey(DirectionFlag.North, Robust.Shared.Input.BoundKeyState.Up);
        var expectedStep = 0f;
        await Server.WaitPost(() => expectedStep = TopSpeed(hull) / TickRate);
        TestContext.Out.WriteLine($"seam: crossed {crossed}, slowest {slowest} m/s of {expectedStep * TickRate}, weightless ticks {weightlessTicks}, worst client jump {worstClientStep} m off a {expectedStep} m tick");

        Assert.That(crossed, Is.True, "precondition: the hull drove onto the second grid");
        Assert.Multiple(() =>
        {
            Assert.That(weightlessTicks, Is.Zero, "the hull shouldn't go weightless at the seam");
            Assert.That(slowest, Is.GreaterThan(expectedStep * TickRate * 0.9f), "nor slow down crossing it");
            Assert.That(worstClientStep, Is.LessThan(0.03f), "and the driver's client shouldn't see it jump");
        });
    }
}
