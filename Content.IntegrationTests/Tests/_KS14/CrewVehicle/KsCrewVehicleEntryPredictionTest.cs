#nullable enable
using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.CrewVehicle.Entry;
using Content.Server._KS14.CrewVehicle.Interior;
using Content.Shared._KS14.CrewVehicle.Interior;
using Content.Shared.Buckle;
using Content.Shared.DoAfter;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Verbs;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;
using ClientPopupSystem = Content.Client.Popups.PopupSystem;
using ClientVerbSystem = Content.Client.Verbs.VerbSystem;

namespace Content.IntegrationTests.Tests._KS14.CrewVehicle;

/// <summary>
///     Trying to get into or out of a crew vehicle is predicted: the player's client starts the do-after, or shows
///         why not, on its own, and the server's copy is not shown again. The move itself is the server's.
/// </summary>
[TestFixture]
[TestOf(typeof(KsCrewVehicleEntrySystem))]
[TestOf(typeof(KsCrewVehicleExitSystem))]
public sealed class KsCrewVehicleEntryPredictionTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private readonly EntProtoId _hull = "KsCrewVehiclePredictionTestHull";
    private readonly EntProtoId _mob = "KsCrewVehiclePredictionTestMob";
    private readonly EntProtoId _seat = "KsCrewVehiclePredictionTestSeat";
    private readonly EntProtoId _blocker = "KsCrewVehiclePredictionTestBlocker";

    // The interior (Maps/Test/_KS14/CrewVehicle/basic.yml) has its "left" door at (0.5, 3.5); the hull's left
    //      doorway, local +X, is where that door leads out.
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsCrewVehiclePredictionTestHull
  components:
  - type: Physics
    bodyType: KinematicController
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeCircle
          radius: 0.2
        hard: false
  - type: KsCrewVehicleInterior
    interiorPath: /Maps/Test/_KS14/CrewVehicle/basic.yml
    interiorAnchor: 2.5,3
    enterDelay: 2
    exitDelay: 2
    maxOccupants: 1
    doorways:
      left:
        offset: 1,0
      right:
        offset: -1,0

- type: entity
  id: KsCrewVehiclePredictionTestMob
  components:
  - type: Eye
  - type: DoAfter
  - type: Buckle
  - type: Physics
    bodyType: KinematicController
  - type: MobState

- type: entity
  id: KsCrewVehiclePredictionTestSeat
  components:
  - type: Strap

- type: entity
  id: KsCrewVehiclePredictionTestBlocker
  components:
  - type: Transform
    anchored: true
  - type: Physics
    bodyType: Static
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeAabb
          bounds: -0.5,-0.5,0.5,0.5
        layer:
        - WallLayer
";

    private static readonly LocId EnterVerb = "ks-crew-vehicle-verb-enter";
    private static readonly LocId ExitVerb = "ks-crew-vehicle-verb-exit";

    /// <summary>
    ///     Inside, beside the left door.
    /// </summary>
    private static readonly Vector2 BesideTheLeftDoor = new(1.5f, 3.5f);

    private MapId _mapId;
    private EntityUid _hullUid;

    /// <summary>
    ///     A hull standing on floor at (0.5, 0.5), unturned: its left doorway is at (1.5, 0.5).
    /// </summary>
    private async Task SpawnHullAsync()
    {
        var testMap = await Pair.CreateTestMap();
        _mapId = testMap.MapId;
        await Server.WaitPost(() =>
        {
            var mapSystem = SEntMan.System<SharedMapSystem>();
            for (var x = -4; x <= 4; x++)
            {
                for (var y = -4; y <= 4; y++)
                {
                    mapSystem.SetTile(testMap.Grid, new Vector2i(x, y), testMap.Tile.Tile);
                }
            }

            _hullUid = SEntMan.SpawnAtPosition(_hull, new EntityCoordinates(testMap.Grid, new Vector2(0.5f, 0.5f)));
            Assert.That(SEntMan.System<KsCrewVehicleInteriorSystem>().EnsureInterior(SEntity<KsCrewVehicleInteriorComponent>(_hullUid), out _), Is.True);
        });
    }

    private Vector2 LeftDoorway => new(1.5f, 0.5f);

    private MapId InteriorMapId => SComp<MapComponent>(SComp<KsCrewVehicleInteriorComponent>(_hullUid).InteriorMap!.Value).MapId;

    /// <summary>
    ///     Puts the player in a test mob at <paramref name="position"/>.
    /// </summary>
    private async Task<EntityUid> AttachPlayerAsync(MapCoordinates position)
    {
        EntityUid mob = default;
        await Server.WaitPost(() =>
        {
            mob = SEntMan.Spawn(_mob, position);
            Server.PlayerMan.SetAttachedEntity(ServerSession!, mob);
        });
        await RunUntilSynced();
        await Pair.RunTicksSync(5);
        return mob;
    }

    /// <summary>
    ///     Picks the verb on the player's client, as the player would: predicted, and sent to the server.
    /// </summary>
    private async Task UseVerbOnClientAsync(EntityUid serverTarget, LocId verbText, Action? sameStep = null)
    {
        await Client.WaitPost(() =>
        {
            var target = ToClientUid(serverTarget);
            var user = Client.Session!.AttachedEntity!.Value;
            var text = Client.ResolveDependency<ILocalizationManager>().GetString(verbText);
            var verb = CEntMan.System<SharedVerbSystem>().GetLocalVerbs(target, user, typeof(AlternativeVerb))
                .FirstOrDefault(candidate => candidate.Text == text);
            Assert.That(verb, Is.Not.Null, $"the client offers the verb '{text}'");
            CEntMan.System<ClientVerbSystem>().ExecuteVerb(target, verb!);
            sameStep?.Invoke();
        });
    }

    private int CountLabels(string text)
    {
        return CEntMan.System<ClientPopupSystem>().WorldLabels.Count(label => label.Text.StartsWith(text, StringComparison.Ordinal));
    }

    /// <summary>
    ///     The popup was shown at once by the predicting client, and the server's copy did not show it again.
    /// </summary>
    private async Task AssertPredictedOnceAsync(string text, string what)
    {
        await Client.WaitAssertion(() => Assert.That(CountLabels(text), Is.EqualTo(1), $"{what}: shown by the client at once"));
        await Pair.RunTicksSync(10);
        await Client.WaitAssertion(() =>
        {
            Assert.That(CountLabels(text), Is.EqualTo(1), $"{what}: and once only, after the server's copy");
            Assert.That(CEntMan.System<ClientPopupSystem>().WorldLabels.Any(label => label.Text.Contains("x2")), Is.False,
                $"{what}: not repeated");
        });
    }

    private string ClientText(LocId id, params (string, object)[] args)
    {
        return Client.ResolveDependency<ILocalizationManager>().GetString(id, args);
    }

    [Test]
    public async Task TheClientStartsGettingInAtOnce()
    {
        await SpawnHullAsync();
        var mob = await AttachPlayerAsync(new MapCoordinates(LeftDoorway, _mapId));

        await UseVerbOnClientAsync(_hullUid, EnterVerb, sameStep: () =>
            Assert.That(CComp<DoAfterComponent>(ToClientUid(mob)).DoAfters, Is.Not.Empty, "the bar shows in the same step"));

        var delay = SComp<KsCrewVehicleInteriorComponent>(_hullUid).EnterDelay;
        await Pair.RunTicksSync((int)Math.Ceiling(delay.TotalSeconds * Server.Timing.TickRate) + 10);
        await Server.WaitAssertion(() =>
            Assert.That(SComp<TransformComponent>(mob).MapID, Is.EqualTo(InteriorMapId), "and the server moved them in"));
    }

    [Test]
    public async Task AwayFromADoorwayTheRefusalIsPredictedOnce()
    {
        await SpawnHullAsync();
        // In reach of the hull, but of neither doorway (each half a tile across, a tile to either side).
        var mob = await AttachPlayerAsync(new MapCoordinates(new Vector2(0.5f, 1.6f), _mapId));

        await UseVerbOnClientAsync(_hullUid, EnterVerb, sameStep: () =>
            Assert.That(CComp<DoAfterComponent>(ToClientUid(mob)).DoAfters, Is.Empty, "no bar"));
        await AssertPredictedOnceAsync(ClientText("ks-crew-vehicle-enter-use-a-door"), "use a door");
    }

    [Test]
    public async Task AFullVehicleIsRefusedOnTheClientFromTheNetworkedCount()
    {
        await SpawnHullAsync();
        await Server.WaitPost(() => SEntMan.Spawn(_mob, new MapCoordinates(new Vector2(2.5f, 2.5f), InteriorMapId)));
        await Pair.RunTicksSync(5);
        var capacity = SComp<KsCrewVehicleInteriorComponent>(_hullUid).MaxOccupants;
        await Client.WaitAssertion(() =>
            Assert.That(CComp<KsCrewVehicleInteriorComponent>(ToClientUid(_hullUid)).LivingOccupants, Is.EqualTo(capacity),
                "precondition: the client knows it is full"));

        var mob = await AttachPlayerAsync(new MapCoordinates(LeftDoorway, _mapId));
        string expected = default!;
        await UseVerbOnClientAsync(_hullUid, EnterVerb, sameStep: () =>
        {
            Assert.That(CComp<DoAfterComponent>(ToClientUid(mob)).DoAfters, Is.Empty, "no bar");
            expected = ClientText("ks-crew-vehicle-enter-full", ("vehicle", ToClientUid(_hullUid)));
        });
        await AssertPredictedOnceAsync(expected, "full");
    }

    [Test]
    public async Task BuckledYouCannotGetOutAndTheClientSaysSoOnce()
    {
        await SpawnHullAsync();
        var mob = await AttachPlayerAsync(new MapCoordinates(BesideTheLeftDoor, InteriorMapId));
        await Server.WaitPost(() =>
        {
            var seat = SEntMan.Spawn(_seat, new MapCoordinates(BesideTheLeftDoor, InteriorMapId));
            Assert.That(SEntMan.System<SharedBuckleSystem>().TryBuckle(mob, null, seat, popup: false), Is.True, "precondition: buckled");
        });
        await Pair.RunTicksSync(5);

        await UseVerbOnClientAsync(ExitDoor(), ExitVerb, sameStep: () =>
            Assert.That(CComp<DoAfterComponent>(ToClientUid(mob)).DoAfters, Is.Empty, "no bar"));
        await AssertPredictedOnceAsync(ClientText("ks-crew-vehicle-buckled"), "buckled");
    }

    /// <param name="blocked">Something is in the way where they would land, rather than the hull moving.</param>
    [TestCase(true)]
    [TestCase(false)]
    public async Task GettingOutIsRefusedOnTheClientOnce(bool blocked)
    {
        await SpawnHullAsync();
        var mob = await AttachPlayerAsync(new MapCoordinates(BesideTheLeftDoor, InteriorMapId));
        await Server.WaitPost(() =>
        {
            if (blocked)
                SEntMan.Spawn(_blocker, new MapCoordinates(LeftDoorway, _mapId));
            else
                SEntMan.System<SharedPhysicsSystem>().SetLinearVelocity(_hullUid,
                    new Vector2(SComp<KsCrewVehicleInteriorComponent>(_hullUid).MaxExitSpeed * 2f + 1f, 0f));
        });
        await Pair.RunTicksSync(5);

        await UseVerbOnClientAsync(ExitDoor(), ExitVerb, sameStep: () =>
            Assert.That(CComp<DoAfterComponent>(ToClientUid(mob)).DoAfters, Is.Empty, "no bar"));
        await AssertPredictedOnceAsync(ClientText(blocked ? "ks-crew-vehicle-exit-blocked" : "ks-crew-vehicle-exit-moving"),
            blocked ? "blocked" : "moving");
    }

    [Test]
    public async Task RefusedByTheServerAtTheEndTheClientStaysPut()
    {
        await SpawnHullAsync();
        var mob = await AttachPlayerAsync(new MapCoordinates(BesideTheLeftDoor, InteriorMapId));

        await UseVerbOnClientAsync(ExitDoor(), ExitVerb, sameStep: () =>
            Assert.That(CComp<DoAfterComponent>(ToClientUid(mob)).DoAfters, Is.Not.Empty, "the bar shows in the same step"));

        // Something lands in the way while the bar fills: only the server's check at the end sees it.
        await Pair.RunTicksSync(2);
        await Server.WaitPost(() => SEntMan.Spawn(_blocker, new MapCoordinates(LeftDoorway, _mapId)));

        var delay = SComp<KsCrewVehicleInteriorComponent>(_hullUid).ExitDelay;
        var ticks = (int)Math.Ceiling(delay.TotalSeconds * Server.Timing.TickRate) + 10;
        var clientMob = ToClientUid(mob);
        var interiorMapId = InteriorMapId;
        for (var i = 0; i < ticks; i++)
        {
            await Pair.RunTicksSync(1);
            await Client.WaitAssertion(() =>
                Assert.That(CComp<TransformComponent>(clientMob).MapID, Is.EqualTo(interiorMapId), $"tick {i}: the client never moved them"));
        }

        await Server.WaitAssertion(() =>
            Assert.That(SComp<TransformComponent>(mob).MapID, Is.EqualTo(interiorMapId), "the server refused"));
        await Client.WaitAssertion(() =>
            Assert.That(CountLabels(ClientText("ks-crew-vehicle-exit-blocked")), Is.EqualTo(1), "and said why, once"));
    }

    [Test]
    public async Task TheClientKnowsHowManyAreAliveInside()
    {
        await SpawnHullAsync();
        var hull = SEntity<KsCrewVehicleInteriorComponent>(_hullUid);
        EntityUid byTheDoor = default;
        EntityUid spawnedIn = default;
        await Server.WaitPost(() =>
        {
            byTheDoor = SEntMan.Spawn(_mob, new MapCoordinates(LeftDoorway, _mapId));
            Assert.That(SEntMan.System<KsCrewVehicleEntrySystem>().TryStartEnter(hull, byTheDoor), Is.True);
        });
        await Pair.RunTicksSync((int)Math.Ceiling(hull.Comp.EnterDelay.TotalSeconds * Server.Timing.TickRate) + 5);
        await AssertCountAsync(1, "after getting in by the door");

        await Server.WaitPost(() => spawnedIn = SEntMan.Spawn(_mob, new MapCoordinates(new Vector2(2.5f, 2.5f), InteriorMapId)));
        await Pair.RunTicksSync(3);
        await AssertCountAsync(2, "after a mob is spawned inside");

        await Server.WaitPost(() => SEntMan.System<MobStateSystem>().ChangeMobState(spawnedIn, MobState.Dead));
        await Pair.RunTicksSync(3);
        await AssertCountAsync(1, "after one dies");

        await Server.WaitPost(() => SEntMan.System<MobStateSystem>().ChangeMobState(spawnedIn, MobState.Alive));
        await Pair.RunTicksSync(3);
        await AssertCountAsync(2, "after it is revived");

        await Server.WaitPost(() => SEntMan.System<SharedTransformSystem>().SetMapCoordinates(spawnedIn, new MapCoordinates(new Vector2(-3f, -3f), _mapId)));
        await Pair.RunTicksSync(3);
        await AssertCountAsync(1, "after one is teleported out");

        await Server.WaitPost(() =>
        {
            Assert.That(SEntMan.System<KsCrewVehicleExitSystem>().TryStartExit(SEntity<KsCrewVehicleExitComponent>(ExitDoor()), byTheDoor), Is.True);
        });
        await Pair.RunTicksSync((int)Math.Ceiling(hull.Comp.ExitDelay.TotalSeconds * Server.Timing.TickRate) + 5);
        await AssertCountAsync(0, "after the last gets out");
    }

    private async Task AssertCountAsync(int expected, string when)
    {
        await Server.WaitAssertion(() =>
            Assert.That(SComp<KsCrewVehicleInteriorComponent>(_hullUid).LivingOccupants, Is.EqualTo(expected), $"server, {when}"));
        await Client.WaitAssertion(() =>
            Assert.That(CComp<KsCrewVehicleInteriorComponent>(ToClientUid(_hullUid)).LivingOccupants, Is.EqualTo(expected), $"client, {when}"));
    }

    private EntityUid ExitDoor()
    {
        var interiorMap = SComp<KsCrewVehicleInteriorComponent>(_hullUid).InteriorMap!.Value;
        Assert.That(SEntMan.System<KsCrewVehicleInteriorSystem>().TryGetExitDoor(interiorMap, "left", out var door), Is.True);
        return door!.Value;
    }
}
