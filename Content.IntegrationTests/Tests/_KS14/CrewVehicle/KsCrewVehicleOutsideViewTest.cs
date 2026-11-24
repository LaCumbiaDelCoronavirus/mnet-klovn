#nullable enable
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Robust.Server.GameObjects;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.CrewVehicle;

/// <summary>
///     Someone inside a crew vehicle stands on the interior map while the vehicle is on another. They are sent the
///         outside through a server-side viewer entity: its own <see cref="EyeComponent"/>, parented to the vehicle,
///         with the session view-subscribed to it. A view seat also points their eye at the vehicle with
///         <see cref="SharedEyeSystem.SetTarget"/>, which subscribes the session to the vehicle itself and drops that
///         subscription again when the target is cleared.
/// </summary>
/// <remarks>
///     Pooled pairs run with net.pvs off, which sends everything to everyone, so every test turns it on, and each
///         checks for entities spawned after the state under test began: leaving PVS only detaches an entity on the
///         client.
/// </remarks>
[TestFixture]
[TestOf(typeof(SharedEyeSystem))]
public sealed class KsCrewVehicleOutsideViewTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestOutsideViewMob
  name: test occupant
  components:
  - type: Eye

- type: entity
  id: KsTestOutsideViewVehicle
  name: test vehicle

- type: entity
  id: KsTestOutsideViewMarker
  name: test marker
";

    private readonly EntProtoId _mob = "KsTestOutsideViewMob";
    private readonly EntProtoId _vehicle = "KsTestOutsideViewVehicle";
    private readonly EntProtoId _marker = "KsTestOutsideViewMarker";

    /// <summary>
    ///     A viewer's PVS scale multiplies the size of the box around it. Four is comfortably past what it takes to
    ///         reach <see cref="WideDistance"/> at the default range, and well short of <see cref="FarDistance"/>.
    /// </summary>
    private const float WideScale = 4f;

    /// <summary>Twice the PVS range: out of reach at scale 1, in reach at <see cref="WideScale"/>.</summary>
    private const float WideDistance = 2f;

    /// <summary>Ten times the PVS range: out of reach at either scale.</summary>
    private const float FarDistance = 10f;

    /// <summary>Long enough for the PVS sweep and the state carrying its result to reach the client.</summary>
    private const int SettleTicks = 30;

    private sealed record Scene(EntityUid Mob, EntityUid Vehicle, EntityUid Viewer, MapId OutsideMapId, float PvsRange);

    /// <summary>
    ///     Puts the player's mob on one map and a vehicle on another, subscribes the session to a viewer entity
    ///         parented to the vehicle, and only then points the mob's eye at the vehicle: the client drops a target it
    ///         hasn't been sent, so the vehicle must reach it first.
    /// </summary>
    private async Task<Scene> SetUpAsync()
    {
        await OverrideCVar(Side.Server, CVars.NetPVS, true);

        var insideMap = await Pair.CreateTestMap();
        var outsideMap = await Pair.CreateTestMap();
        var session = ServerSession;
        Assert.That(session, Is.Not.Null, "this test needs a connected session for anything to replicate to");

        EntityUid mob = default, vehicle = default, viewer = default;
        await Server.WaitPost(() =>
        {
            mob = SSpawnAtPosition(_mob, insideMap.GridCoords);
            Server.PlayerMan.SetAttachedEntity(session!, mob);

            vehicle = SSpawnAtPosition(_vehicle, outsideMap.GridCoords);
            viewer = SEntMan.SpawnAttachedTo(null, new EntityCoordinates(vehicle, Vector2.Zero));
            SEntMan.EnsureComponent<EyeComponent>(viewer);
            SEntMan.System<ViewSubscriberSystem>().AddViewSubscriber(viewer, session!);
        });
        await Pair.RunTicksSync(SettleTicks);

        await Server.WaitPost(() => SEntMan.System<SharedEyeSystem>().SetTarget(mob, vehicle));
        await Pair.RunTicksSync(SettleTicks);

        await Client.WaitAssertion(() =>
            Assert.That(CEntMan.GetComponent<EyeComponent>(ToClientUid(mob)).Target, Is.EqualTo(ToClientUid(vehicle)),
                "precondition: the client kept the eye target on the other map"));

        var pvsRange = Server.ResolveDependency<IConfigurationManager>().GetCVar(CVars.NetMaxUpdateRange);
        return new Scene(mob, vehicle, viewer, outsideMap.MapId, pvsRange);
    }

    private async Task<NetEntity> SpawnMarkerAsync(Scene scene, float distance)
    {
        var netEntity = NetEntity.Invalid;
        await Server.WaitPost(() =>
        {
            var vehiclePosition = SEntMan.System<SharedTransformSystem>().GetWorldPosition(scene.Vehicle);
            var markerUid = SEntMan.SpawnEntity(_marker,
                new MapCoordinates(vehiclePosition + new Vector2(distance, 0f), scene.OutsideMapId));
            netEntity = SEntMan.GetNetEntity(markerUid);
        });
        return netEntity;
    }

    /// <summary>Whether the client holds the entity and it is in view, not left detached by an earlier send.</summary>
    private async Task<bool> ClientHasAsync(NetEntity netEntity)
    {
        var has = false;
        await Client.WaitPost(() =>
            has = CEntMan.TryGetEntity(netEntity, out var clientUid)
                  && (CEntMan.GetComponent<MetaDataComponent>(clientUid.Value).Flags & MetaDataFlags.Detached) == 0);
        return has;
    }

    [Test]
    public async Task NearTheVehicleIsSentAndFarIsNot()
    {
        var scene = await SetUpAsync();

        var near = await SpawnMarkerAsync(scene, 2f);
        var far = await SpawnMarkerAsync(scene, FarDistance * scene.PvsRange);
        await Pair.RunTicksSync(SettleTicks);

        Assert.That(await ClientHasAsync(near), Is.True,
            "an entity spawned next to the vehicle on the other map should reach the occupant's client");
        Assert.That(await ClientHasAsync(far), Is.False,
            "an entity far from both the occupant and the vehicle should not reach the client");
    }

    /// <summary>
    ///     The vehicle itself is a viewer at scale 1 while it is the eye target, so only the viewer entity's own scale
    ///         can widen what arrives.
    /// </summary>
    [Test]
    public async Task ViewerScaleWidensTheOutside()
    {
        var scene = await SetUpAsync();

        var wide = await SpawnMarkerAsync(scene, WideDistance * scene.PvsRange);
        await Pair.RunTicksSync(SettleTicks);
        Assert.That(await ClientHasAsync(wide), Is.False,
            "precondition: at scale 1 an entity twice the PVS range from the vehicle should not arrive");

        await Server.WaitPost(() => SEntMan.System<SharedEyeSystem>().SetPvsScale(scene.Viewer, WideScale));
        await Pair.RunTicksSync(SettleTicks);
        Assert.That(await ClientHasAsync(wide), Is.True,
            "the viewer entity's PVS scale should widen what is sent around the vehicle");

        var far = await SpawnMarkerAsync(scene, FarDistance * scene.PvsRange);
        await Pair.RunTicksSync(SettleTicks);
        Assert.That(await ClientHasAsync(far), Is.False,
            "an entity beyond even the scaled range should still not arrive");
    }

    /// <summary>
    ///     Standing up from a view seat clears the eye target, which removes the session's subscription to the
    ///         vehicle. The viewer entity is a different entity, so the outside keeps coming.
    /// </summary>
    [Test]
    public async Task ClearingTheEyeTargetKeepsTheViewer()
    {
        var scene = await SetUpAsync();

        await Server.WaitPost(() => SEntMan.System<SharedEyeSystem>().SetTarget(scene.Mob, null));
        await Pair.RunTicksSync(SettleTicks);

        await Server.WaitAssertion(() =>
        {
            var subscriptions = ServerSession!.ViewSubscriptions;
            Assert.That(subscriptions, Does.Not.Contain(scene.Vehicle),
                "precondition: clearing the eye target drops the subscription to the vehicle");
            Assert.That(subscriptions, Does.Contain(scene.Viewer),
                "clearing the eye target should leave the subscription to the viewer entity alone");
        });
        await Client.WaitAssertion(() =>
            Assert.That(CEntMan.GetComponent<EyeComponent>(ToClientUid(scene.Mob)).Target, Is.Null,
                "precondition: the client's eye is back on the occupant"));

        var near = await SpawnMarkerAsync(scene, 2f);
        var far = await SpawnMarkerAsync(scene, FarDistance * scene.PvsRange);
        await Pair.RunTicksSync(SettleTicks);

        Assert.That(await ClientHasAsync(near), Is.True,
            "with the eye target cleared, entities near the vehicle should still reach the client");
        Assert.That(await ClientHasAsync(far), Is.False,
            "an entity far from both the occupant and the vehicle should not reach the client");
    }
}
