#nullable enable
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._KS14.ZLevel;
using Robust.Server.GameObjects;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.ZLevel;

/// <summary>
///     The z-level below is sent from wherever a player sees, not only from their body: an entity their session is
///         view-subscribed to (a camera, a vehicle's outside viewer) or their eye's target, on a z-level, gets the
///         level below as a mob standing there would, at its own PVS scale.
/// </summary>
/// <remarks>
///     The player's body is on a map of its own, off any stack, so nothing below could reach them through it. PVS is
///         turned on: pooled pairs send everything to everyone otherwise.
/// </remarks>
[TestOf(typeof(KsZLevelPvsSystem))]
public sealed class KsZLevelViewPointPvsTest : KsZLevelTestBase
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    /// <summary>Long enough for the z-level PVS update, on its own interval, and the sweep after it.</summary>
    private const int SettleTicks = 40;

    private sealed record Scene(TestStack Stack, EntityUid Body, float PvsRange);

    private async Task<Scene> SetUpAsync()
    {
        await OverrideCVar(Side.Server, CVars.NetPVS, true);
        var stack = await CreateStack();
        Assert.That(ServerSession, Is.Not.Null, "this test needs a connected session for anything to replicate to");

        EntityUid body = default;
        await Server.WaitPost(() =>
        {
            SEntMan.System<SharedMapSystem>().CreateMap(out var elsewhereMapId);
            body = SEntMan.SpawnEntity(null, new MapCoordinates(Vector2.Zero, elsewhereMapId));
            SEntMan.EnsureComponent<EyeComponent>(body);
            Server.PlayerMan.SetAttachedEntity(ServerSession!, body);
        });
        await Pair.RunTicksSync(SettleTicks);

        var pvsRange = Server.ResolveDependency<IConfigurationManager>().GetCVar(CVars.NetMaxUpdateRange);
        return new Scene(stack, body, pvsRange);
    }

    /// <summary>A view entity on the upper z-level, over its floor, that the player's session sees from.</summary>
    private async Task<EntityUid> SubscribeViewAsync(Scene scene)
    {
        EntityUid viewUid = default;
        await Server.WaitPost(() =>
        {
            viewUid = SEntMan.SpawnEntity(null, scene.Stack.SupportedCoords);
            SEntMan.EnsureComponent<EyeComponent>(viewUid);
            SEntMan.System<ViewSubscriberSystem>().AddViewSubscriber(viewUid, ServerSession!);
        });
        await Pair.RunTicksSync(SettleTicks);
        return viewUid;
    }

    /// <summary>Something on the lower z-level, <paramref name="distance"/> east of the spot under the view.</summary>
    private async Task<NetEntity> SpawnBelowAsync(Scene scene, float distance)
    {
        var netEntity = NetEntity.Invalid;
        await Server.WaitPost(() =>
            netEntity = SEntMan.GetNetEntity(SEntMan.SpawnEntity(null,
                new MapCoordinates(new Vector2(0.5f + distance, 0.5f), scene.Stack.LowerMapId))));
        return netEntity;
    }

    private async Task<bool> ClientHasAsync(NetEntity netEntity)
    {
        var has = false;
        await Client.WaitPost(() =>
            has = CEntMan.TryGetEntity(netEntity, out var clientUid)
                  && (CEntMan.GetComponent<MetaDataComponent>(clientUid.Value).Flags & MetaDataFlags.Detached) == 0);
        return has;
    }

    private int LiveSubscribers()
    {
        return SEntMan.AllEntities<KsZLevelViewSubscriberComponent>()
            .Count(subscriber => !SEntMan.IsQueuedForDeletion(subscriber.Owner) && ServerSession!.ViewSubscriptions.Contains(subscriber.Owner));
    }

    [Test]
    public async Task ViewSubscriptionGetsTheLevelBelow()
    {
        var scene = await SetUpAsync();
        var viewUid = await SubscribeViewAsync(scene);

        var under = await SpawnBelowAsync(scene, 0f);
        var far = await SpawnBelowAsync(scene, 10f * scene.PvsRange);
        await Pair.RunTicksSync(SettleTicks);

        Assert.That(await ClientHasAsync(under), Is.True,
            "the level below a view entity the player sees from should be sent to them");
        Assert.That(await ClientHasAsync(far), Is.False, "but only around it");

        // No longer seen from, it sends nothing below.
        await Server.WaitPost(() => SEntMan.System<ViewSubscriberSystem>().RemoveViewSubscriber(viewUid, ServerSession!));
        await Pair.RunTicksSync(SettleTicks);
        await Server.WaitAssertion(() =>
        {
            Assert.That(LiveSubscribers(), Is.Zero, "unsubscribed, its subscribers below should go");
            Assert.That(SEntMan.HasComponent<KsZLevelViewerComponent>(viewUid), Is.False);
        });
        var afterwards = await SpawnBelowAsync(scene, 0f);
        await Pair.RunTicksSync(SettleTicks);
        Assert.That(await ClientHasAsync(afterwards), Is.False, "and nothing new below arrives");
    }

    [Test]
    public async Task TheLevelBelowIsAsWideAsTheView()
    {
        const float wideScale = 4f;
        var scene = await SetUpAsync();
        var viewUid = await SubscribeViewAsync(scene);

        // Out of reach at scale 1, in reach at the wide one.
        var wide = await SpawnBelowAsync(scene, scene.PvsRange * (wideScale + 1f) / 4f);
        await Pair.RunTicksSync(SettleTicks);
        Assert.That(await ClientHasAsync(wide), Is.False, "precondition: at scale 1 this is out of reach below");

        await Server.WaitPost(() => SEntMan.System<SharedEyeSystem>().SetPvsScale(viewUid, wideScale));
        await Pair.RunTicksSync(SettleTicks);
        Assert.That(await ClientHasAsync(wide), Is.True, "the level below should be sent as wide as the view above it");
    }

    /// <summary>
    ///     A player's eye target and a viewer riding it are two view points at one spot: one set of subscribers below
    ///         serves both.
    /// </summary>
    [Test]
    public async Task ViewPointsAtOneSpotShareTheLevelBelow()
    {
        var scene = await SetUpAsync();
        var firstUid = await SubscribeViewAsync(scene);
        await Server.WaitAssertion(() => Assert.That(LiveSubscribers(), Is.EqualTo(1), "precondition: one level below, one subscriber"));

        EntityUid targetUid = default;
        await Server.WaitPost(() =>
        {
            targetUid = SEntMan.SpawnEntity(null, scene.Stack.SupportedCoords);
            SEntMan.System<SharedEyeSystem>().SetTarget(scene.Body, targetUid);
        });
        await Pair.RunTicksSync(SettleTicks);

        await Server.WaitAssertion(() =>
        {
            Assert.That(ServerSession!.ViewSubscriptions, Does.Contain(targetUid), "precondition: the eye target is seen from");
            Assert.That(LiveSubscribers(), Is.EqualTo(1), "two view points at one spot should share one subscriber below");
        });

        // The first one gone, the eye target alone still gets the level below.
        await Server.WaitPost(() => SEntMan.System<ViewSubscriberSystem>().RemoveViewSubscriber(firstUid, ServerSession!));
        await Pair.RunTicksSync(SettleTicks);
        var under = await SpawnBelowAsync(scene, 0f);
        await Pair.RunTicksSync(SettleTicks);
        await Server.WaitAssertion(() => Assert.That(LiveSubscribers(), Is.EqualTo(1)));
        Assert.That(await ClientHasAsync(under), Is.True, "the eye target on its own should get the level below");
    }
}
