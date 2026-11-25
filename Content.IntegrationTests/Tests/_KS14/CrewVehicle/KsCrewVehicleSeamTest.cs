#nullable enable
using System;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server.Gravity;
using Content.Shared.Gravity;
using Content.Shared.Maps;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;

namespace Content.IntegrationTests.Tests._KS14.CrewVehicle;

/// <summary>
///     Driving off one grid onto another that touches it: the hull keeps its speed across the seam, and the driver's
///         client sees it go as smoothly as it sees a player walking across.
/// </summary>
/// <remarks>
///     On the tick a predicted body changes grid, the client shows it off by half a tick's travel, then right again:
///         a player walking across does the same, and so does an upstream strap vehicle, so it isn't the hull's. That
///         walk, measured here, is the hull's tolerance.
/// </remarks>
[TestOf(typeof(Content.Shared._KS14.CrewVehicle.Drive.KsCrewVehicleDriveSystem))]
public sealed class KsCrewVehicleSeamTest : KsCrewVehicleDriveTestBase
{
    /// <summary>How many steps back from the seam a mover is put, and how many of them pass before it is watched.</summary>
    private const int StepsBack = 15;
    private const int SettleTicks = 5;

    private sealed record Crossing(bool Crossed, float Slowest, int WeightlessTicks, float ReparentFraction, float WorstElsewhere, float TickStep);

    /// <summary>A second grid butted against the first's east edge.</summary>
    private async Task<EntityUid> SpawnSecondGridAsync()
    {
        EntityUid secondGrid = default;
        await Server.WaitPost(() =>
        {
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

            Transform.SetWorldPosition(grid.Owner, Transform.GetWorldPosition(MapData.Grid) + new Vector2(FloorRadius + 1, 0f));
            var gravityComponent = SEntMan.EnsureComponent<GravityComponent>(grid.Owner);
            SEntMan.System<GravitySystem>().EnableGravity(grid.Owner, gravityComponent);
        });
        await RunTicks(5);
        return secondGrid;
    }

    /// <summary>
    ///     Holds <paramref name="key"/> a second to get up to speed, then two more across the seam, watching the server
    ///         and the client's predicted position of <paramref name="mover"/> each tick.
    /// </summary>
    private async Task<Crossing> CrossAsync(EntityUid mover, DirectionFlag key, EntityUid secondGrid)
    {
        // The key kept held throughout: let go and pressed again, the mover would slow and pick up again.
        await SetMovementKey(key, Robust.Shared.Input.BoundKeyState.Down);
        await RunTicks(TickRate);

        var clientMover = ToClient(mover);
        var tickStep = 0f;
        await Server.WaitPost(() =>
        {
            // Put where its steps straddle the seam evenly, half a step each side, some way back: how far off the client
            //      shows a body changing grid depends on where in a step it crosses, so the walk and the drive are
            //      compared crossing alike.
            tickStep = SComp<PhysicsComponent>(mover).LinearVelocity.Length() / TickRate;
            var seamX = Transform.GetWorldPosition(secondGrid).X;
            var position = Transform.GetWorldPosition(mover);
            Transform.SetWorldPosition(mover, new Vector2(seamX - tickStep * (StepsBack + 0.5f), position.Y));
        });
        // Past the teleport before watching.
        await RunTicks(SettleTicks);
        var previous = 0f;
        var previousParent = EntityUid.Invalid;
        await Client.WaitPost(() =>
        {
            previous = Client.System<SharedTransformSystem>().GetWorldPosition(clientMover).X;
            previousParent = CEntMan.GetComponent<TransformComponent>(clientMover).ParentUid;
        });

        var slowest = float.MaxValue;
        var crossed = false;
        var weightlessTicks = 0;
        var reparentFraction = 0f;
        var worstElsewhere = 0f;
        for (var tick = 0; tick < TickRate * 2; tick++)
        {
            await RunTicks(1);
            await Server.WaitPost(() =>
            {
                slowest = MathF.Min(slowest, SComp<PhysicsComponent>(mover).LinearVelocity.Length());
                crossed |= SComp<TransformComponent>(mover).GridUid == secondGrid;
                if (SEntMan.System<SharedGravitySystem>().IsWeightless(mover))
                    weightlessTicks++;
            });
            await Client.WaitPost(() =>
            {
                var x = Client.System<SharedTransformSystem>().GetWorldPosition(clientMover).X;
                var parent = CEntMan.GetComponent<TransformComponent>(clientMover).ParentUid;
                // A sample can span two of the client's ticks: what counts is how far off a whole number of ticks' travel.
                var step = x - previous;
                var off = MathF.Abs(step - MathF.Round(step / tickStep) * tickStep);
                if (parent != previousParent)
                    reparentFraction = MathF.Max(reparentFraction, off / tickStep);
                else
                    worstElsewhere = MathF.Max(worstElsewhere, off);

                previous = x;
                previousParent = parent;
            });
        }

        await SetMovementKey(key, Robust.Shared.Input.BoundKeyState.Up);
        return new Crossing(crossed, slowest, weightlessTicks, reparentFraction, worstElsewhere, tickStep);
    }

    [Test]
    public async Task CrossingAGridSeam()
    {
        var secondGrid = await SpawnSecondGridAsync();
        var start = new Vector2(FloorRadius - 4, 0f);
        var scene = await SpawnHullAsync(start + new Vector2(0f, 2f));
        await Server.WaitPost(() => Transform.SetWorldRotation(scene.Hull, new Vector2(1f, 0f).ToWorldAngle()));

        // The control: the player walking across from the same line, moving as the hull does, so that both reach the
        //      seam at the same point in a tick. How far off the client shows it depends on that point.
        await Server.WaitPost(() =>
        {
            var hullSpeed = SComp<Content.Shared.Movement.Components.MovementSpeedModifierComponent>(scene.Hull);
            var speedSystem = SEntMan.System<Content.Shared.Movement.Systems.MovementSpeedModifierSystem>();
            // The player's own modifiers (a species' pace) stay on top of its base speed: divided out, so it moves at the hull's.
            var mobSpeed = SComp<Content.Shared.Movement.Components.MovementSpeedModifierComponent>(SPlayer);
            speedSystem.ChangeBaseSpeed(SPlayer,
                hullSpeed.CurrentWalkSpeed / mobSpeed.WalkSpeedModifier,
                hullSpeed.CurrentSprintSpeed / mobSpeed.SprintSpeedModifier,
                hullSpeed.BaseAcceleration);
            speedSystem.ChangeBaseFriction(SPlayer, hullSpeed.BaseFriction, hullSpeed.FrictionNoInput, hullSpeed.BaseAcceleration);
            Transform.SetCoordinates(SPlayer, OriginCoordinates.Offset(start));
            Transform.AttachToGridOrMap(SPlayer);
        });
        await RunTicks(5);
        var walk = await CrossAsync(SPlayer, DirectionFlag.East, secondGrid);

        // The hull, facing east, driven across a little further along the seam.
        await PutInsideAsync(scene);
        await SitAsync(scene.DriverSeat);
        var drive = await CrossAsync(scene.Hull, DirectionFlag.North, secondGrid);

        TestContext.Out.WriteLine($"seam: walk {walk}; drive {drive}");
        Assert.That(walk.Crossed && drive.Crossed, Is.True, "precondition: both crossed onto the second grid");
        Assert.Multiple(() =>
        {
            Assert.That(drive.WeightlessTicks, Is.Zero, "the hull shouldn't go weightless at the seam");
            Assert.That(drive.Slowest, Is.GreaterThan(drive.TickStep * TickRate * 0.99f), "nor slow down crossing it");
            Assert.That(drive.ReparentFraction, Is.LessThanOrEqualTo(walk.ReparentFraction + 0.01f),
                "the driver's client should see the hull change grid no worse than a player walking across");
            Assert.That(drive.WorstElsewhere / drive.TickStep, Is.LessThanOrEqualTo(walk.WorstElsewhere / walk.TickStep + 0.01f),
                "nor any less smoothly either side of the seam");
        });
    }
}
