#nullable enable
using System.Linq;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._KS14.Damage.Examine;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.Dataset;
using Content.Shared.Examine;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Damage;

/// <summary>
///     A damaged thing's damage line is worked out on the examiner's own client, the same line as the server's, so the
///         tooltip has it at once. Control: upstream's server-only <c>ExaminableDamage</c> line is missing from the
///         client's own text, and arrives only with the server's reply.
/// </summary>
[TestOf(typeof(KsExaminableDamageSystem))]
public sealed class KsExaminableDamageTest : InteractionTest
{
    private readonly EntProtoId _shared = "KsExaminableDamageTestShared";
    private readonly EntProtoId _serverOnly = "KsExaminableDamageTestServerOnly";
    private static readonly ProtoId<LocalizedDatasetPrototype> Messages = "ShieldMessages";
    private static readonly ProtoId<DamageTypePrototype> Blunt = "Blunt";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsExaminableDamageTestShared
  components:
  - type: Damageable
  - type: Injurable
    damageContainer: Inorganic
  - type: Destructible
    thresholds:
    - trigger:
        !type:DamageTrigger
        damage: 100
      behaviors:
      - !type:DoActsBehavior
        acts: [ ""Destruction"" ]
  - type: KsExaminableDamage
    messages: ShieldMessages

- type: entity
  id: KsExaminableDamageTestServerOnly
  components:
  - type: Damageable
  - type: Injurable
    damageContainer: Inorganic
  - type: Destructible
    thresholds:
    - trigger:
        !type:DamageTrigger
        damage: 100
      behaviors:
      - !type:DoActsBehavior
        acts: [ ""Destruction"" ]
  - type: ExaminableDamage
    messages: ShieldMessages
";

    [TestCase(true)]
    [TestCase(false)]
    public async Task ClientShowsTheDamageLineAtOnce(bool shared)
    {
        await SpawnTarget(shared ? _shared : _serverOnly);
        await Server.WaitPost(() =>
            SEntMan.System<DamageableSystem>().TryChangeDamage(SEntMan.GetEntity(Target!.Value),
                new DamageSpecifier(ProtoMan.Index(Blunt), 60)));
        await RunTicks(5);

        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.System<DamageableSystem>().GetTotalDamage(SEntMan.GetEntity(Target!.Value)).Float(), Is.GreaterThan(0f),
                "precondition: damaged"));

        var serverText = "";
        await Server.WaitPost(() =>
            serverText = SEntMan.System<ExamineSystemShared>().GetExamineText(SEntMan.GetEntity(Target!.Value), SPlayer).ToMarkup());
        var clientText = "";
        await Client.WaitPost(() => clientText = ExamineSys.GetExamineText(CEntMan.GetEntity(Target!.Value), CPlayer).ToMarkup());

        // The damage line, whichever level: the dataset's messages as this side words them.
        var loc = Server.ResolveDependency<Robust.Shared.Localization.ILocalizationManager>();
        var lines = ProtoMan.Index(Messages).Values.Select(id => loc.GetString(id)).ToList();
        var serverLine = lines.FirstOrDefault(line => serverText.Contains(line));
        Assert.That(serverLine, Is.Not.Null.And.Not.EqualTo(lines[0]), "precondition: the server's text says it is damaged");

        if (shared)
            Assert.That(clientText, Does.Contain(serverLine), "the client's own text should have the server's damage line");
        else
            Assert.That(lines.Any(line => clientText.Contains(line)), Is.False, "control: a server-only line isn't in the client's own text");
    }
}
