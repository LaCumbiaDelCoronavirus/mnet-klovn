#nullable enable
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Shared._KS14.CrewVehicle.AmmoPort;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Verbs;
using Content.Shared.Weapons.Ranged.Components;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.CrewVehicle;

/// <summary>
///     A one-round weapon's ammo port, a breech: examining it names the round in it, and that round can be taken back
///         out. Every number comes from the test prototypes.
/// </summary>
[TestOf(typeof(SharedKsCrewVehicleAmmoPortSystem))]
public sealed class KsCrewVehicleBreechTest : KsCrewVehicleGunnerTestBase
{
    private readonly EntProtoId _breechHull = "KsCrewVehicleBreechTestHull";
    private readonly EntProtoId _breechPort = "KsCrewVehicleBreechTestPort";
    private readonly EntProtoId _shell = "KsCrewVehicleBreechTestShell";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: KsCrewVehicleGunnerTestCartridge
  id: KsCrewVehicleBreechTestShell
  name: breech test shell
  categories: [ HideSpawnMenu ]

- type: entity
  id: KsCrewVehicleBreechTestGun
  categories: [ HideSpawnMenu ]
  components:
  - type: Gun
    fireRate: 1
    selectedMode: SemiAuto
    availableModes:
    - SemiAuto
  - type: BallisticAmmoProvider
    capacity: 1
    whitelist:
      tags:
      - KsCrewVehicleGunnerTestRound
  - type: KsCrewVehicleWeapon
    muzzleOffset: 0,-0.6
    maxAimRange: 5

- type: entity
  parent: KsCrewVehicleGunnerTestHull
  id: KsCrewVehicleBreechTestHull
  components:
  - type: KsCrewVehicleMounts
    mounts:
      main:
        prototype: KsCrewVehicleBreechTestGun
        offset: 0,-0.6

- type: entity
  parent: KsCrewVehicleInteriorBaseAmmoPort
  id: KsCrewVehicleBreechTestPort
  components:
  - type: KsCrewVehicleAmmoPort
    mount: main
    loadDelay: 0.5
    canUnload: true
";

    [Test]
    public async Task ChamberedRoundIsNamedAndCanBeTakenOut()
    {
        var scene = await SpawnHullAsync(Vector2.Zero, _breechHull);
        await PutInsideAsync(scene);

        var weapon = EntityUid.Invalid;
        var port = EntityUid.Invalid;
        var shell = EntityUid.Invalid;
        await Server.WaitPost(() =>
        {
            weapon = Weapon(scene.Hull);
            port = SEntMan.SpawnAtPosition(_breechPort, SComp<TransformComponent>(scene.PassengerSeat).Coordinates);
            shell = SEntMan.SpawnAtPosition(_shell, SComp<TransformComponent>(SPlayer).Coordinates);
            Assert.That(SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(SPlayer, shell), "precondition: picked up");
        });
        await RunTicks(5);

        await Server.WaitPost(() => InteractSys.InteractUsing(SPlayer, shell, port, SComp<TransformComponent>(port).Coordinates));
        await RunTicks((int)(SComp<KsCrewVehicleAmmoPortComponent>(port).LoadDelay.TotalSeconds * TickRate) * 2 + 5);

        await Server.WaitAssertion(() =>
        {
            Assert.That(RoundsLeft(weapon), Is.EqualTo(SComp<BallisticAmmoProviderComponent>(weapon).Capacity), "precondition: loaded");

            var expected = Server.ResolveDependency<ILocalizationManager>().GetString("ks-crew-vehicle-ammo-port-chambered",
                ("round", SComp<MetaDataComponent>(shell).EntityName));
            var text = SEntMan.System<ExamineSystemShared>().GetExamineText(port, SPlayer).ToMarkup();
            Assert.That(text, Does.Contain(expected), $"examining the breech names the round in it: {text}");

            var verbs = SEntMan.System<SharedVerbSystem>().GetLocalVerbs(port, SPlayer, typeof(AlternativeVerb));
            var unload = Server.ResolveDependency<ILocalizationManager>().GetString("ks-crew-vehicle-ammo-port-verb-unload");
            Assert.That(verbs.Any(verb => verb.Text == unload && !verb.Disabled), "it can be unloaded");
        });

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<SharedKsCrewVehicleAmmoPortSystem>().TryUnload(SEntity<KsCrewVehicleAmmoPortComponent>(port), SPlayer),
                "the round comes out");
            Assert.That(RoundsLeft(weapon), Is.Zero, "the weapon is empty");
            Assert.That(SEntMan.System<SharedHandsSystem>().IsHolding(SPlayer, shell), "and the same round is in the hand");
        });
    }
}
