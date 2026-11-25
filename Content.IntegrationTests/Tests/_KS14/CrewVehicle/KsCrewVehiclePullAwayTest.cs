#nullable enable
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Content.Shared._KS14.CrewVehicle;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._KS14.CrewVehicle;

/// <summary>
///     Pulling away from rest: the driver's client predicts the hull along the very path the server drives it.
/// </summary>
[TestOf(typeof(Content.Shared._KS14.CrewVehicle.Drive.KsCrewVehicleDriveSystem))]
public sealed class KsCrewVehiclePullAwayTest : KsCrewVehicleDriveTestBase
{
    /// <summary>The most ticks the client's samples may lag the server's by.</summary>
    private const int MaxLag = 3;

    [Test]
    public async Task PullingAwayIsPredictedRight()
    {
        var scene = await SpawnHullAsync(Vector2.Zero);
        await PutInsideAsync(scene);
        await SitAsync(scene.DriverSeat);
        // Seated and idle for a while first, as a driver is before pulling away.
        await RunTicks(TickRate);

        var hull = scene.Hull;
        var clientHull = ToClient(hull);
        var clientGrid = ToClient(MapData.Grid.Owner);
        var forward = Vector2.Zero;
        await Server.WaitPost(() => forward = KsCrewVehicleHullFrame.Forward(HullRotation(hull)));

        // On the floor grid whichever grid each side has the hull on, and whichever way the grid has drifted.
        var server = new Dictionary<GameTick, float>();
        var client = new Dictionary<GameTick, float>();
        var parents = new HashSet<EntityUid>();

        await SetMovementKey(DirectionFlag.North, Robust.Shared.Input.BoundKeyState.Down);
        for (var tick = 0; tick < TickRate; tick++)
        {
            await RunTicks(1);
            await Server.WaitPost(() =>
            {
                var onGrid = Vector2.Transform(Transform.GetWorldPosition(hull), Transform.GetInvWorldMatrix(MapData.Grid.Owner));
                server[STiming.CurTick] = Vector2.Dot(onGrid, forward);
            });
            await Client.WaitPost(() =>
            {
                var transformSystem = Client.System<SharedTransformSystem>();
                var onGrid = Vector2.Transform(transformSystem.GetWorldPosition(clientHull), transformSystem.GetInvWorldMatrix(clientGrid));
                client.TryAdd(CTiming.CurTick, Vector2.Dot(onGrid, forward));
                parents.Add(CEntMan.GetComponent<TransformComponent>(clientHull).ParentUid);
            });
        }

        await SetMovementKey(DirectionFlag.North, Robust.Shared.Input.BoundKeyState.Up);

        // The client's ticks are numbered a fixed few apart from the server's: line them up at the best lag.
        var worst = float.MaxValue;
        var bestLag = 0;
        for (var lag = -MaxLag; lag <= MaxLag; lag++)
        {
            var worstAtLag = 0f;
            var compared = 0;
            foreach (var (tick, clientAlong) in client)
            {
                if (!server.TryGetValue(new GameTick((uint)((int)tick.Value + lag)), out var serverAlong))
                    continue;

                worstAtLag = System.MathF.Max(worstAtLag, System.MathF.Abs(clientAlong - serverAlong));
                compared++;
            }

            if (compared > TickRate / 2 && worstAtLag < worst)
            {
                worst = worstAtLag;
                bestLag = lag;
            }
        }

        TestContext.Out.WriteLine($"pull away: worst {worst} m off the server's path at a lag of {bestLag} ticks; client parents {string.Join(", ", parents)}");
        Assert.Multiple(() =>
        {
            Assert.That(parents, Has.Count.EqualTo(1), "the driver's client should keep the hull on one grid");
            Assert.That(worst, Is.LessThan(0.01f), "the driver's client should predict the hull along the server's path");
        });
    }
}
