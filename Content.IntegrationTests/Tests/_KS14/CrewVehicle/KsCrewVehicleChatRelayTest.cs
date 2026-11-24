#nullable enable
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.CrewVehicle.Chat;
using Content.Server._KS14.CrewVehicle.Interior;
using Content.Server.Chat.Systems;
using Content.Shared._KS14.CrewVehicle.Interior;
using Content.Shared.Chat;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.CrewVehicle;

/// <summary>
///     Speech carries through a crew vehicle's hull, both ways, as far as a voice carries in the world outside.
/// </summary>
/// <remarks>
///     Raises the chat system's recipient event directly, as its own local and whisper paths do, with every listener
///         a dummy session.
/// </remarks>
[TestFixture]
[TestOf(typeof(KsCrewVehicleChatRelaySystem))]
public sealed class KsCrewVehicleChatRelayTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false, Dirty = true };

    private readonly EntProtoId _hull = "KsCrewVehicleChatTestHull";
    private readonly EntProtoId _mob = "KsCrewVehicleChatTestMob";

    // The anchor is the interior point over the hull's centre: someone standing on it is heard from the hull's centre.
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsCrewVehicleChatTestHull
  components:
  - type: KsCrewVehicleInterior
    interiorPath: /Maps/Test/_KS14/CrewVehicle/basic.yml
    interiorAnchor: 2.5,3
    doorways:
      left:
        offset: 1,0
      right:
        offset: -1,0

- type: entity
  id: KsCrewVehicleChatTestMob
  components:
  - type: MobState
";

    private static readonly Vector2 Anchor = new(2.5f, 3f);

    private MapId _outsideMapId;

    private async Task<(EntityUid Hull, MapId InteriorMapId)> SpawnHullAsync(Vector2 position)
    {
        EntityUid hull = default;
        MapId interiorMapId = default;
        await Server.WaitPost(() =>
        {
            hull = SEntMan.Spawn(_hull, new MapCoordinates(position, _outsideMapId));
            Assert.That(SEntMan.System<KsCrewVehicleInteriorSystem>().EnsureInterior(SEntity<KsCrewVehicleInteriorComponent>(hull), out var interiorMap), Is.True);
            interiorMapId = SComp<MapComponent>(interiorMap!.Value).MapId;
        });
        return (hull, interiorMapId);
    }

    private async Task<(EntityUid Mob, ICommonSession Session)> AddListenerAsync(MapCoordinates position)
    {
        var session = await Server.AddDummySession();
        EntityUid mob = default;
        await Server.WaitPost(() =>
        {
            mob = SEntMan.Spawn(_mob, position);
            Server.PlayerMan.SetAttachedEntity(session, mob);
        });
        return (mob, session);
    }

    private Dictionary<ICommonSession, ChatSystem.ICChatRecipientData> Recipients(EntityUid speaker)
    {
        var recipients = new Dictionary<ICommonSession, ChatSystem.ICChatRecipientData>();
        SEntMan.EventBus.RaiseEvent(EventSource.Local, new ExpandICChatRecipientsEvent(speaker, SharedChatSystem.VoiceRange, recipients));
        return recipients;
    }

    [Test]
    public async Task SpeechInsideIsHeardBesideTheVehicle()
    {
        _outsideMapId = (await Pair.CreateTestMap()).MapId;
        var (_, interiorMapId) = await SpawnHullAsync(Vector2.Zero);
        var (_, beside) = await AddListenerAsync(new MapCoordinates(new Vector2(3f, 0f), _outsideMapId));
        var (_, far) = await AddListenerAsync(new MapCoordinates(new Vector2(SharedChatSystem.VoiceRange + 5f, 0f), _outsideMapId));
        EntityUid speaker = default;
        await Server.WaitPost(() => speaker = SEntMan.Spawn(_mob, new MapCoordinates(Anchor, interiorMapId)));

        await Server.WaitAssertion(() =>
        {
            var recipients = Recipients(speaker);
            Assert.That(recipients, Contains.Key(beside), "heard beside the vehicle");
            Assert.That(recipients[beside].Range, Is.EqualTo(3f).Within(0.01f), "as far off as they stand from where the speaker sits over the world");
            Assert.That(recipients, Does.Not.ContainKey(far), "the control: not heard far away");
        });
    }

    [Test]
    public async Task SpeechBesideTheVehicleIsHeardInside()
    {
        _outsideMapId = (await Pair.CreateTestMap()).MapId;
        var (_, interiorMapId) = await SpawnHullAsync(Vector2.Zero);
        var (occupant, occupantSession) = await AddListenerAsync(new MapCoordinates(Anchor, interiorMapId));
        EntityUid nearSpeaker = default;
        EntityUid farSpeaker = default;
        await Server.WaitPost(() =>
        {
            nearSpeaker = SEntMan.Spawn(_mob, new MapCoordinates(new Vector2(3f, 0f), _outsideMapId));
            farSpeaker = SEntMan.Spawn(_mob, new MapCoordinates(new Vector2(SharedChatSystem.VoiceRange + 5f, 0f), _outsideMapId));
        });
        await Pair.RunTicksSync(2);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<KsCrewVehicleOccupantComponent>(occupant), Is.True, "precondition: inside");
            var recipients = Recipients(nearSpeaker);
            Assert.That(recipients, Contains.Key(occupantSession), "someone near the hull is heard inside");
            Assert.That(recipients[occupantSession].Range, Is.EqualTo(3f).Within(0.01f));
            Assert.That(Recipients(farSpeaker), Does.Not.ContainKey(occupantSession), "the control: someone far off is not");
        });
    }

    /// <param name="near">The other vehicle is within a voice's reach.</param>
    [TestCase(true)]
    [TestCase(false)]
    public async Task OneVehicleHearsAnotherOnlyWithinReach(bool near)
    {
        _outsideMapId = (await Pair.CreateTestMap()).MapId;
        var (_, interiorA) = await SpawnHullAsync(Vector2.Zero);
        var (_, interiorB) = await SpawnHullAsync(new Vector2(near ? 5f : SharedChatSystem.VoiceRange + 5f, 0f));
        var (occupantB, sessionB) = await AddListenerAsync(new MapCoordinates(Anchor, interiorB));
        EntityUid speaker = default;
        await Server.WaitPost(() => speaker = SEntMan.Spawn(_mob, new MapCoordinates(Anchor, interiorA)));
        await Pair.RunTicksSync(2);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<KsCrewVehicleOccupantComponent>(occupantB), Is.True, "precondition: inside the other vehicle");
            Assert.That(Recipients(speaker).ContainsKey(sessionB), Is.EqualTo(near));
        });
    }
}
