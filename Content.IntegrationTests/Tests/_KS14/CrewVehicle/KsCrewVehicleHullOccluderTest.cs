#nullable enable
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Client._KS14.CrewVehicle.Render;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.CrewVehicle.Render;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.CrewVehicle;

/// <summary>
///     A crew vehicle's hull occluder exists on the client only, survives server states and the driver's prediction, and
///         is off exactly while this client's eye is inside the hull: in its view seat or on its interior map.
/// </summary>
[TestOf(typeof(KsCrewVehicleHullOccluderSystem))]
public sealed class KsCrewVehicleHullOccluderTest : KsCrewVehicleDriveTestBase
{
    private readonly EntProtoId _occludedHull = "KsCrewVehicleHullOccluderTestHull";
    private readonly EntProtoId _lightInsideHull = "KsCrewVehicleHullOccluderTestHullLightInside";

    // The drive tests' hull is 1 by 1.6; the polygon sits inside it, and its headlight just past the front, at -Y.
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: KsCrewVehicleDriveTestHull
  id: KsCrewVehicleHullOccluderTestHull
  components:
  - type: PointLight
    enabled: false
    offset: 0,-0.9
  - type: KsCrewVehicleHullOccluder
    polygon:
    - -0.45,-0.75
    - 0.45,-0.75
    - 0.45,0.75
    - -0.45,0.75

- type: entity
  parent: KsCrewVehicleHullOccluderTestHull
  id: KsCrewVehicleHullOccluderTestHullLightInside
  components:
  - type: PointLight
    enabled: false
    offset: 0,-0.5
";

    /// <summary>
    ///     The client's occluder on the hull, or null; asserts it is the client-only one.
    /// </summary>
    private async Task<bool?> ClientOccluderEnabledAsync(EntityUid serverHull)
    {
        bool? enabled = null;
        await Client.WaitPost(() =>
        {
            var clientHull = Client.EntMan.GetEntity(Server.EntMan.GetNetEntity(serverHull));
            if (!Client.EntMan.TryGetComponent<OccluderComponent>(clientHull, out var occluder))
                return;

            Assert.That(occluder.NetSyncEnabled, Is.False, "the hull's occluder should be the client's own, not networked");
            enabled = occluder.Enabled;
        });
        return enabled;
    }

    [Test]
    public async Task OffOnlyForTheCrewAndKeptThroughStatesAndDriving()
    {
        var scene = await SpawnHullAsync(new Vector2(3f, 0f), _occludedHull);
        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.HasComponent<OccluderComponent>(scene.Hull), Is.False,
                "the server builds no occluder: it would feed NPC line of sight"));

        await Client.WaitAssertion(() =>
        {
            var clientHull = Client.EntMan.GetEntity(Server.EntMan.GetNetEntity(scene.Hull));
            var occluder = Client.EntMan.GetComponent<OccluderComponent>(clientHull);
            var expected = SEntMan.GetComponent<KsCrewVehicleHullOccluderComponent>(scene.Hull).Polygon;
            Assert.That(occluder.Polygon.ToArray(), Is.EquivalentTo(expected), "the client's occluder takes the hull's polygon");
        });
        Assert.That(await ClientOccluderEnabledAsync(scene.Hull), Is.True, "the player outside is blocked by the hull");

        await PutInsideAsync(scene);
        Assert.That(await ClientOccluderEnabledAsync(scene.Hull), Is.False,
            "standing in the interior, the backdrop's FOV is cast from inside the hull, so its occluder is off");

        // A round trip of a few ticks: each state rolls the driven hull back and replays the driver's prediction.
        await SimulateLatencyAsync(3);
        await SitAsync(scene.DriverSeat);
        Assert.That(await ClientOccluderEnabledAsync(scene.Hull), Is.False, "in the view seat the eye is on the hull: still off");

        var parked = Vector2.Zero;
        await Server.WaitPost(() => parked = HullPosition(scene.Hull));
        await SetMovementKey(DirectionFlag.North, Robust.Shared.Input.BoundKeyState.Down);
        try
        {
            for (var tick = 0; tick < TickRate; tick++)
            {
                await RunTicks(1);
                Assert.That(await ClientOccluderEnabledAsync(scene.Hull), Is.False,
                    $"driven, {tick} ticks in: the occluder should survive server states and prediction, and stay off");
            }
        }
        finally
        {
            await SetMovementKey(DirectionFlag.North, Robust.Shared.Input.BoundKeyState.Up);
        }

        await Server.WaitAssertion(() =>
            Assert.That((HullPosition(scene.Hull) - parked).Length(), Is.GreaterThan(0.25f), "precondition: the hull was driven"));

        await StandAsync();
        await SettleAsync(scene.Hull);
        await Server.WaitPost(() => Transform.SetCoordinates(SPlayer, OriginCoordinates));
        await RunTicks(5);
        Assert.That(await ClientOccluderEnabledAsync(scene.Hull), Is.True, "once out, the hull blocks their sight again");
    }

    [Test]
    public async Task ShadowCastingLightInsideThePolygonIsRefused()
    {
        await Server.WaitAssertion(() =>
            Assert.That(RefusedInterior(_lightInsideHull), Has.Some.Contains("inside its hull occluder"),
                "a headlight inside the hull occluder would go dark for everyone outside"));

        // The control: the same hull with its light past the front loads (SpawnHullAsync asserts it).
        await SpawnHullAsync(new Vector2(3f, 0f), _occludedHull);
    }

    [Test]
    public void PolygonRules()
    {
        Vector2[] square = [new(-1f, -1f), new(1f, -1f), new(1f, 1f), new(-1f, 1f)];
        Assert.That(KsCrewVehicleHullOccluderValidationSystem.GetPolygonError(square), Is.Null);
        Assert.That(KsCrewVehicleHullOccluderValidationSystem.GetPolygonError(square.Reverse().ToArray()), Is.Null, "either winding");
        Assert.That(KsCrewVehicleHullOccluderValidationSystem.GetPolygonError([new(-1f, -1f), new(1f, -1f)]), Is.Not.Null, "too few");

        Vector2[] dented = [new(-1f, -1f), new(1f, -1f), new(1f, 1f), new(0f, 0f), new(-1f, 1f)];
        Assert.That(KsCrewVehicleHullOccluderValidationSystem.GetPolygonError(dented), Is.Not.Null, "not convex");

        Assert.That(KsCrewVehicleHullOccluderValidationSystem.IsInside(square, Vector2.Zero), Is.True);
        Assert.That(KsCrewVehicleHullOccluderValidationSystem.IsInside(square, new Vector2(0f, -1.35f)), Is.False);
    }
}
