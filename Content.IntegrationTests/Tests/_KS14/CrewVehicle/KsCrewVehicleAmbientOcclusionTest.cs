using System.Numerics;
using Content.Client._KS14.Light;
using Content.IntegrationTests.Fixtures;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.CrewVehicle;

/// <summary>
///     A crew vehicle's hull doesn't block sight or light, and still casts its silhouette's ambient occlusion.
/// </summary>
[TestFixture]
[TestOf(typeof(KsAmbientOcclusionCasterSystem))]
public sealed class KsCrewVehicleAmbientOcclusionTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static readonly EntProtoId[] Hulls = ["KsCrewVehicleHumvee", "KsCrewVehicleTank"];

    [Test]
    public async Task HullCastsAmbientOcclusionAndOccludesNothing([ValueSource(nameof(Hulls))] EntProtoId hullPrototype)
    {
        var map = await Pair.CreateTestMap();
        EntityUid hull = default;
        await Server.WaitPost(() => hull = SSpawnAtPosition(hullPrototype, map.GridCoords.Offset(new Vector2(0.5f, 0.5f))));
        await Pair.RunTicksSync(10);

        await Client.WaitAssertion(() =>
        {
            var clientHull = ToClientUid(hull);
            Assert.That(CEntMan.HasComponent<KsAmbientOcclusionCasterComponent>(clientHull), Is.True,
                $"{hullPrototype} should cast ambient occlusion");
            Assert.That(CEntMan.HasComponent<OccluderComponent>(clientHull), Is.False,
                $"{hullPrototype} should not block sight or light");
        });
    }
}
