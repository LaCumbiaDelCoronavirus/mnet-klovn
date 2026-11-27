#nullable enable
using System;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server.Gravity;
using Content.Shared._KS14.CrewVehicle.Drive;
using Content.Shared.Gravity;
using Content.Shared.Maps;
using Content.Shared.Movement.Components;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Map.Components;

namespace Content.IntegrationTests.Tests._KS14.CrewVehicle;

/// <summary>
///     A hull isn't held to the floor: driven off the edge it carries on over space, with no traction, and drifts with
///         the speed it had. The floor is the test grid's tiles, -<see cref="KsCrewVehicleDriveTestBase.FloorRadius"/> to
///         +<see cref="KsCrewVehicleDriveTestBase.FloorRadius"/>, so its east edge is at grid x = FloorRadius + 1.
/// </summary>
[TestOf(typeof(KsCrewVehicleDriveSystem))]
public sealed class KsCrewVehicleDriftTest : KsCrewVehicleDriveTestBase
{
    /// <summary>The floor's east edge, in grid coordinates.</summary>
    private const float EastEdge = FloorRadius + 1;

    private static readonly Angle East = new Vector2(1f, 0f).ToWorldAngle();

    private static Vector2 AtGridX(float gridX) => new(gridX - 0.5f, 0f);

    private async Task<HullScene> DrivenEastAsync(float gridX)
    {
        var scene = await SpawnHullAsync(AtGridX(gridX));
        await Server.WaitPost(() => Transform.SetWorldRotation(scene.Hull, East));
        await RunTicks(2);
        await PutInsideAsync(scene);
        await SitAsync(scene.DriverSeat);
        return scene;
    }

    private bool OnAGrid(EntityUid hull) => SComp<TransformComponent>(hull).GridUid != null;

    private float OffGridFriction(EntityUid hull) =>
        SComp<MovementSpeedModifierComponent>(hull).OffGridFriction ?? 0f;

    /// <summary>
    ///     Drives east off the edge, holding forward the whole way, and returns the speed on leaving. From then on, every
    ///         tick the speed is at most the last tick's and at least what the off-grid friction leaves of it: nothing
    ///         accelerates the hull, and nothing but friction slows it, so it drifts.
    /// </summary>
    [Test]
    public async Task DrivesOffTheEdgeAndDrifts()
    {
        var scene = await DrivenEastAsync(EastEdge - 5f);
        var hull = scene.Hull;
        var friction = 0f;
        var dt = 0f;
        var topSpeed = 0f;
        await Server.WaitPost(() =>
        {
            friction = OffGridFriction(hull);
            dt = 1f / TickRate;
            topSpeed = TopSpeed(hull);
        });

        await SetMovementKey(DirectionFlag.North, Robust.Shared.Input.BoundKeyState.Down);
        var left = false;
        for (var tick = 0; tick < TickRate * 6 && !left; tick++)
        {
            await RunTicks(1);
            await Server.WaitPost(() => left = !OnAGrid(hull));
        }

        var entrySpeed = 0f;
        var entryDirection = Vector2.Zero;
        await Server.WaitPost(() =>
        {
            entrySpeed = HullVelocity(hull).Length();
            entryDirection = Vector2.Normalize(HullVelocity(hull));
        });
        Assert.That(left, Is.True, "precondition: driven off the edge");
        Assert.That(entrySpeed, Is.GreaterThan(topSpeed * 0.9f), "precondition: it left at speed");

        var previous = entrySpeed;
        var worstDirection = 0f;
        for (var tick = 0; tick < TickRate; tick++)
        {
            await RunTicks(1);
            await Server.WaitAssertion(() =>
            {
                var velocity = HullVelocity(hull);
                var speed = velocity.Length();
                Assert.That(speed, Is.LessThanOrEqualTo(previous + 0.001f), "forward held, nothing accelerates it over space");
                Assert.That(speed, Is.GreaterThanOrEqualTo(previous * (1f - dt * friction) - 0.01f), "only friction slows it");
                worstDirection = MathF.Max(worstDirection, MathF.Acos(Math.Clamp(Vector2.Dot(Vector2.Normalize(velocity), entryDirection), -1f, 1f)));
                previous = speed;
            });
        }

        await SetMovementKey(DirectionFlag.North, Robust.Shared.Input.BoundKeyState.Up);
        Assert.That(previous, Is.GreaterThan(entrySpeed * 0.5f), "after a second it is still going");
        Assert.That(MathHelper.RadiansToDegrees(worstDirection), Is.LessThan(1f), "straight on");
    }

    /// <summary>
    ///     Over space the steering keys do nothing. Control: on the floor the same key turns the hull.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task NoSteeringOffGrid(bool offGrid)
    {
        var scene = await DrivenEastAsync(offGrid ? EastEdge + 4f : 0f);
        var hull = scene.Hull;
        await Server.WaitAssertion(() =>
            Assert.That(OnAGrid(hull), Is.Not.EqualTo(offGrid), "precondition: where the test wants it"));

        // Moving, so a turn and a velocity direction could show.
        await Server.WaitPost(() => SEntMan.System<Robust.Shared.Physics.Systems.SharedPhysicsSystem>()
            .SetLinearVelocity(hull, new Vector2(1f, 0f)));
        var before = Angle.Zero;
        await Server.WaitPost(() => before = HullRotation(hull));
        await HoldAsync(hull, DirectionFlag.East, TickRate);

        await Server.WaitAssertion(() =>
        {
            var turned = Math.Abs(Angle.ShortestDistance(before, HullRotation(hull)).Degrees);
            if (offGrid)
            {
                Assert.That(turned, Is.LessThan(0.5f), "nothing to steer on over space");
                Assert.That(HullVelocity(hull).Y, Is.EqualTo(0f).Within(0.01f), "and the drift goes straight on");
            }
            else
            {
                Assert.That(turned, Is.GreaterThan(10f), "control: on the floor the same key turns it");
            }
        });
    }

    /// <summary>
    ///     On the driver's own client the hull goes over the edge without a step back or a jump.
    /// </summary>
    [Test]
    public async Task DriftOnTheDriversClient()
    {
        var scene = await DrivenEastAsync(EastEdge - 5f);
        var clientHull = ToClient(scene.Hull);
        float ClientAlong() => CEntMan.GetComponent<TransformComponent>(clientHull).LocalPosition.X;

        await SetMovementKey(DirectionFlag.North, Robust.Shared.Input.BoundKeyState.Down);
        var previous = float.MinValue;
        var worstStepBack = 0f;
        for (var tick = 0; tick < TickRate * 4; tick++)
        {
            await RunTicks(1);
            await Client.WaitPost(() =>
            {
                var along = ClientAlong();
                if (previous > float.MinValue)
                    worstStepBack = MathF.Max(worstStepBack, previous - along);
                previous = along;
            });
        }

        await SetMovementKey(DirectionFlag.North, Robust.Shared.Input.BoundKeyState.Up);
        await RunTicks(TickRate / 2);
        await Server.WaitAssertion(() =>
            Assert.That(OnAGrid(scene.Hull), Is.False, "precondition: it went over the edge"));
        Assert.That(worstStepBack, Is.LessThan(0.001f), "the driver's client never sees it step back");

        var serverPosition = Vector2.Zero;
        await Server.WaitPost(() => serverPosition = Transform.GetWorldPosition(scene.Hull));
        var clientPosition = Vector2.Zero;
        await Client.WaitPost(() => clientPosition = Client.System<SharedTransformSystem>().GetWorldPosition(clientHull));
        Assert.That((serverPosition - clientPosition).Length(), Is.LessThan(0.2f), "and it agrees with the server");
    }

    /// <summary>
    ///     A second grid beyond the edge, with no gravity component: the drifting hull reaches it, reparents and, with
    ///         forward held, drives again.
    /// </summary>
    [Test]
    public async Task RegainsTractionOnAnotherGrid()
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

            Transform.SetWorldPosition(grid.Owner, Transform.GetWorldPosition(MapData.Grid.Owner) + new Vector2(EastEdge + 5f, 0f));
        });

        var scene = await DrivenEastAsync(EastEdge - 5f);
        var hull = scene.Hull;
        await SetMovementKey(DirectionFlag.North, Robust.Shared.Input.BoundKeyState.Down);
        var wasOffGrid = false;
        var landed = false;
        var speedOnLanding = 0f;
        for (var tick = 0; tick < TickRate * 8 && !landed; tick++)
        {
            await RunTicks(1);
            await Server.WaitPost(() =>
            {
                wasOffGrid |= !OnAGrid(hull);
                if (wasOffGrid && SComp<TransformComponent>(hull).GridUid == secondGrid)
                {
                    landed = true;
                    speedOnLanding = HullVelocity(hull).Length();
                }
            });
        }

        Assert.That(wasOffGrid, Is.True, "precondition: it drifted over the gap");
        Assert.That(landed, Is.True, "and reached the other grid");

        // Slower from the gap's drag, it speeds up again now there is something to grip.
        var fastest = speedOnLanding;
        for (var tick = 0; tick < TickRate * 2; tick++)
        {
            await RunTicks(1);
            await Server.WaitPost(() => fastest = MathF.Max(fastest, HullVelocity(hull).Length()));
        }

        await SetMovementKey(DirectionFlag.North, Robust.Shared.Input.BoundKeyState.Up);
        Assert.That(fastest, Is.GreaterThan(speedOnLanding + 0.05f), "traction is back");
    }

    /// <summary>
    ///     A hull has traction on any grid, with or without gravity: this one has no gravity component at all, and
    ///         over space the same hull is weightless.
    /// </summary>
    [Test]
    public async Task TractionOnAGridWithoutGravity()
    {
        var scene = await SpawnHullAsync(Vector2.Zero);
        await Server.WaitPost(() =>
        {
            SEntMan.RemoveComponent<GravityComponent>(MapData.Grid.Owner);
            SEntMan.RemoveComponent<GravityComponent>(MapData.MapUid);
            SEntMan.System<SharedGravitySystem>().RefreshWeightless((scene.Hull, null));
        });
        await RunTicks(2);
        var gravitySystem = Server.System<SharedGravitySystem>();
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<GravityComponent>(MapData.Grid.Owner), Is.False, "precondition: no gravity here");
            Assert.That(gravitySystem.IsWeightless(scene.Hull), Is.False, "a hull grips any grid's floor");
            Transform.SetWorldPosition(scene.Hull, Transform.GetWorldPosition(MapData.Grid.Owner) + new Vector2(EastEdge + 3f, 0f));
        });
        await RunTicks(3);
        await Server.WaitAssertion(() =>
        {
            Assert.That(OnAGrid(scene.Hull), Is.False, "precondition: off the grid");
            Assert.That(gravitySystem.IsWeightless(scene.Hull), Is.True, "and off it, it is weightless");
        });
    }

    private static readonly Robust.Shared.Prototypes.ProtoId<ContentTileDefinition> LatticeTile = "Lattice";

    /// <summary>
    ///     Lattice is a tile of the same grid but open to space: a hull over it has no traction either, and drifts.
    /// </summary>
    [Test]
    public async Task LatticeHasNoTraction()
    {
        var scene = await DrivenEastAsync(EastEdge - 5f);
        var hull = scene.Hull;
        await Server.WaitPost(() =>
        {
            var tile = new Tile(ProtoMan.Index(LatticeTile).TileId);
            var tiles = new System.Collections.Generic.List<(Vector2i, Tile)>();
            for (var x = FloorRadius + 1; x <= FloorRadius + 12; x++)
            {
                for (var y = -4; y <= 4; y++)
                {
                    tiles.Add((new Vector2i(x, y), tile));
                }
            }

            MapSystem.SetTiles(MapData.Grid.Owner, MapData.Grid.Comp, tiles);
        });
        await RunTicks(2);

        var gravitySystem = Server.System<SharedGravitySystem>();
        await SetMovementKey(DirectionFlag.North, Robust.Shared.Input.BoundKeyState.Down);
        var weightless = false;
        for (var tick = 0; tick < TickRate * 6 && !weightless; tick++)
        {
            await RunTicks(1);
            await Server.WaitPost(() => weightless = gravitySystem.IsWeightless(hull));
        }

        Assert.That(weightless, Is.True, "precondition: over the lattice it has no traction");
        var previous = 0f;
        await Server.WaitPost(() => previous = HullVelocity(hull).Length());
        for (var tick = 0; tick < TickRate; tick++)
        {
            await RunTicks(1);
            await Server.WaitAssertion(() =>
            {
                Assert.That(OnAGrid(hull), Is.True, "precondition: still on the grid, over its lattice");
                var speed = HullVelocity(hull).Length();
                Assert.That(speed, Is.LessThanOrEqualTo(previous + 0.001f), "forward held, nothing accelerates it over lattice");
                previous = speed;
            });
        }

        await SetMovementKey(DirectionFlag.North, Robust.Shared.Input.BoundKeyState.Up);
    }

    /// <summary>
    ///     A parked hull shoved off the edge by a driven one drifts on over space instead of stopping as on a floor.
    /// </summary>
    [Test]
    public async Task PushedParkedHullDriftsOff()
    {
        var parked = await SpawnHullAsync(AtGridX(EastEdge - 1.5f));
        var scene = await DrivenEastAsync(EastEdge - 5f);
        await SetMovementKey(DirectionFlag.North, Robust.Shared.Input.BoundKeyState.Down);
        var off = false;
        for (var tick = 0; tick < TickRate * 8 && !off; tick++)
        {
            await RunTicks(1);
            await Server.WaitPost(() => off = !OnAGrid(parked.Hull));
        }

        await SetMovementKey(DirectionFlag.North, Robust.Shared.Input.BoundKeyState.Up);
        Assert.That(off, Is.True, "precondition: it was shoved off the edge");

        var speedOnLeaving = 0f;
        var positionOnLeaving = Vector2.Zero;
        await Server.WaitPost(() =>
        {
            speedOnLeaving = HullVelocity(parked.Hull).Length();
            positionOnLeaving = Transform.GetWorldPosition(parked.Hull);
        });
        await RunTicks(TickRate * 2);
        await Server.WaitAssertion(() =>
        {
            Assert.That(HullVelocity(parked.Hull).Length(), Is.GreaterThan(speedOnLeaving * 0.8f), "it carries on");
            Assert.That((Transform.GetWorldPosition(parked.Hull) - positionOnLeaving).Length(), Is.GreaterThan(speedOnLeaving), "a good way");
        });
    }

    /// <summary>
    ///     The driver getting out mid-drift doesn't stop the hull: it is a loose body then, with the speed it had.
    /// </summary>
    [Test]
    public async Task DriverLeavesWhileDrifting()
    {
        var scene = await DrivenEastAsync(EastEdge - 5f);
        var hull = scene.Hull;
        await SetMovementKey(DirectionFlag.North, Robust.Shared.Input.BoundKeyState.Down);
        var left = false;
        for (var tick = 0; tick < TickRate * 6 && !left; tick++)
        {
            await RunTicks(1);
            await Server.WaitPost(() => left = !OnAGrid(hull));
        }

        await SetMovementKey(DirectionFlag.North, Robust.Shared.Input.BoundKeyState.Up);
        Assert.That(left, Is.True, "precondition: over space");

        var before = 0f;
        await Server.WaitPost(() => before = HullVelocity(hull).Length());
        await StandAsync();
        var after = 0f;
        await Server.WaitPost(() => after = HullVelocity(hull).Length());
        Assert.That(after, Is.GreaterThan(before * 0.9f), "unbuckling over space doesn't brake it");

        await RunTicks(TickRate * 2);
        await Server.WaitAssertion(() =>
            Assert.That(HullVelocity(hull).Length(), Is.GreaterThan(before * 0.3f), "and it keeps drifting"));
    }
}
