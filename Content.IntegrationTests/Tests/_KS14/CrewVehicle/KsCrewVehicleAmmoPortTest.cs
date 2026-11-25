#nullable enable
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Shared._KS14.CrewVehicle.AmmoPort;
using Content.Shared._KS14.CrewVehicle.Mounts;
using Content.Shared.Whitelist;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Input;
using Robust.Shared.Localization;
using ClientPopupSystem = Content.Client.Popups.PopupSystem;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.CrewVehicle;

/// <summary>
///     Loading a mounted weapon from inside, through its ammo port. Every number comes from the test prototypes.
/// </summary>
[TestOf(typeof(SharedKsCrewVehicleAmmoPortSystem))]
public sealed class KsCrewVehicleAmmoPortTest : KsCrewVehicleGunnerTestBase
{
    private readonly EntProtoId _ammoPort = "KsCrewVehicleGunnerTestAmmoPort";
    private readonly EntProtoId _cartridge = "KsCrewVehicleGunnerTestCartridge";
    private readonly EntProtoId _wrongCartridge = "KsCrewVehicleGunnerTestWrongCartridge";
    private readonly EntProtoId _ammoBox = "KsCrewVehicleGunnerTestAmmoBox";
    private readonly EntProtoId _otherAmmoBox = "KsCrewVehicleGunnerTestOtherAmmoBox";

    private int CartridgeEntities() => SEntMan.Count<CartridgeAmmoComponent>();

    /// <summary>
    ///     The player's client uses what they hold on the port, as its own click does: predicted.
    /// </summary>
    private async Task ClientUseOnPortAsync(PortScene portScene)
    {
        var portNetEntity = NetEntity.Invalid;
        var portCoordinates = NetCoordinates.Invalid;
        await Server.WaitPost(() =>
        {
            portNetEntity = SEntMan.GetNetEntity(portScene.Port);
            portCoordinates = SEntMan.GetNetCoordinates(SComp<TransformComponent>(portScene.Port).Coordinates);
        });
        await SetKey(EngineKeyFunctions.Use, Robust.Shared.Input.BoundKeyState.Down, portCoordinates, portNetEntity);
        await RunTicks(1);
        await SetKey(EngineKeyFunctions.Use, Robust.Shared.Input.BoundKeyState.Up, portCoordinates, portNetEntity);
    }

    private int ClientRounds(EntityUid serverWeapon)
    {
        var ev = new GetAmmoCountEvent();
        CEntMan.EventBus.RaiseLocalEvent(ToClient(serverWeapon), ref ev);
        return ev.Count;
    }

    /// <summary>
    ///     A client well ahead of the server shows the weapon loaded before the server's state could have told it.
    /// </summary>
    [Test]
    public async Task LoadingIsPredicted()
    {
        var clientConfigurationManager = Client.ResolveDependency<IConfigurationManager>();
        await Client.WaitPost(() => clientConfigurationManager.SetCVar(CVars.NetPredictTickBias, 4));

        var portScene = await SpawnPortAsync(rounds: 2);
        await HoldAsync(_ammoBox);
        await ClientUseOnPortAsync(portScene);

        var delayTicks = (int)(SComp<KsCrewVehicleAmmoPortComponent>(portScene.Port).LoadDelay.TotalSeconds * TickRate);
        var clientLed = false;
        var serverLoaded = false;
        for (var tick = 0; tick < delayTicks * 3 && !serverLoaded; tick++)
        {
            await RunTicks(1);
            var serverCount = 0;
            await Server.WaitPost(() => serverCount = RoundsLeft(portScene.Weapon));
            var clientCount = 0;
            await Client.WaitPost(() => clientCount = ClientRounds(portScene.Weapon));
            clientLed |= clientCount > serverCount;
            serverLoaded = serverCount > 2;
        }

        Assert.That(serverLoaded, "precondition: the server loaded the weapon");
        Assert.That(clientLed, "the client should show the weapon loaded before the server's state reaches it");
    }

    /// <summary>
    ///     A wrong round's refusal is shown by the client itself, once, and the server's copy isn't shown again.
    /// </summary>
    [Test]
    public async Task RefusalIsShownAtOnceAndOnce()
    {
        var portScene = await SpawnPortAsync(rounds: 2);
        await HoldAsync(_wrongCartridge);
        var text = string.Empty;
        await Client.WaitPost(() => text = Client.ResolveDependency<ILocalizationManager>().GetString("ks-crew-vehicle-ammo-port-wrong-ammo"));

        await ClientUseOnPortAsync(portScene);
        await Client.WaitAssertion(() =>
            Assert.That(CEntMan.System<ClientPopupSystem>().WorldLabels.Count(label => label.Text == text), Is.EqualTo(1),
                "the client should show the refusal at once"));

        await RunTicks(20);
        await Client.WaitAssertion(() =>
            Assert.That(CEntMan.System<ClientPopupSystem>().WorldLabels.Count(label => label.Text == text), Is.EqualTo(1),
                "and only once, when the server's arrives"));
    }

    /// <summary>
    ///     The client's own examine text, before the server answers, already has the count.
    /// </summary>
    [Test]
    public async Task ClientExamineHasTheCount()
    {
        var portScene = await SpawnPortAsync(rounds: 3);
        var expected = string.Empty;
        await Server.WaitPost(() => expected = $"{RoundsLeft(portScene.Weapon)}[/color] of {Capacity(portScene.Weapon)}");
        await Client.WaitAssertion(() =>
        {
            var text = CEntMan.System<ExamineSystemShared>().GetExamineText(ToClient(portScene.Port), CPlayer).ToMarkup();
            Assert.That(text, Does.Contain(expected), $"the client's examine text should show the count: {text}");
        });
    }

    /// <summary>
    ///     A box of another round than the weapon's own still loads it, through the server, which makes each round.
    /// </summary>
    [Test]
    public async Task OtherRoundBoxLoadsThroughTheServer()
    {
        var portScene = await SpawnPortAsync(rounds: 2);
        await HoldAsync(_otherAmmoBox);
        await ClientUseOnPortAsync(portScene);
        await RunTicks((int)(SComp<KsCrewVehicleAmmoPortComponent>(portScene.Port).LoadDelay.TotalSeconds * TickRate) * 2 + 10);
        await Server.WaitAssertion(() =>
            Assert.That(RoundsLeft(portScene.Weapon), Is.EqualTo(Capacity(portScene.Weapon)), "the box fills the weapon"));
        await Client.WaitAssertion(() =>
            Assert.That(ClientRounds(portScene.Weapon), Is.EqualTo(Capacity(portScene.Weapon)), "and the client hears of it"));
    }

    /// <summary>
    ///     Every mounted weapon's whitelist takes the round it is filled with, which a box of the same round is moved
    ///         into it as, without the whitelist being asked.
    /// </summary>
    [Test]
    public async Task MountedWeaponsTakeTheirOwnRound()
    {
        await Server.WaitAssertion(() =>
        {
            var factory = SEntMan.ComponentFactory;
            var whitelistSystem = SEntMan.System<EntityWhitelistSystem>();
            var checkedWeapons = 0;
            foreach (var prototype in ProtoMan.EnumeratePrototypes<EntityPrototype>())
            {
                if (prototype.Abstract ||
                    !prototype.TryComp<KsCrewVehicleWeaponComponent>(out _, factory) ||
                    !prototype.TryComp<BallisticAmmoProviderComponent>(out var ballisticComponent, factory) ||
                    ballisticComponent.Proto is not { } roundPrototype)
                    continue;

                checkedWeapons++;
                var roundUid = SEntMan.SpawnEntity(roundPrototype, MapCoordinates.Nullspace);
                Assert.That(whitelistSystem.IsWhitelistPass(ballisticComponent.Whitelist, roundUid),
                    $"{prototype.ID}'s whitelist refuses {roundPrototype}, the round it is filled with");
                SEntMan.DeleteEntity(roundUid);
            }

            Assert.That(checkedWeapons, Is.GreaterThan(0), "precondition: there are mounted weapons to check");
        });
    }

    private sealed record PortScene(HullScene Scene, EntityUid Weapon, EntityUid Port);

    /// <summary>
    ///     The player standing inside beside an ammo port on the hull's weapon, which has <paramref name="rounds"/> left.
    /// </summary>
    private async Task<PortScene> SpawnPortAsync(int rounds)
    {
        var scene = await SpawnHullAsync(Vector2.Zero, _gunnerHull);
        await PutInsideAsync(scene);

        PortScene portScene = default!;
        await Server.WaitPost(() =>
        {
            var weapon = Weapon(scene.Hull);
            SEntMan.System<SharedGunSystem>().SetBallisticUnspawned(SEntity<BallisticAmmoProviderComponent>(weapon), rounds);
            var port = SEntMan.SpawnAtPosition(_ammoPort, SComp<TransformComponent>(scene.PassengerSeat).Coordinates);
            portScene = new PortScene(scene, weapon, port);
        });
        await RunTicks(5);
        return portScene;
    }

    private async Task<EntityUid> HoldAsync(EntProtoId prototype)
    {
        var held = EntityUid.Invalid;
        await Server.WaitPost(() =>
        {
            held = SEntMan.SpawnAtPosition(prototype, SComp<TransformComponent>(SPlayer).Coordinates);
            Assert.That(SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(SPlayer, held), "precondition: picked up");
        });
        await RunTicks(5);
        return held;
    }

    private async Task UseOnPortAsync(PortScene portScene, EntityUid held)
    {
        await Server.WaitPost(() =>
            InteractSys.InteractUsing(SPlayer, held, portScene.Port, SComp<TransformComponent>(portScene.Port).Coordinates));

        // Past the port's load delay, with room to spare.
        var delay = SComp<KsCrewVehicleAmmoPortComponent>(portScene.Port).LoadDelay;
        await RunTicks((int)(delay.TotalSeconds * TickRate) * 2 + 5);
    }

    private int Capacity(EntityUid weapon) => SComp<BallisticAmmoProviderComponent>(weapon).Capacity;

    /// <summary>
    ///     A round the weapon takes goes in after the port's delay; one it doesn't take is refused and stays in hand.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task LoadsACartridgeItTakes(bool fits)
    {
        var portScene = await SpawnPortAsync(rounds: 2);
        var cartridge = await HoldAsync(fits ? _cartridge : _wrongCartridge);
        await UseOnPortAsync(portScene, cartridge);

        await Server.WaitAssertion(() =>
        {
            if (fits)
            {
                Assert.That(RoundsLeft(portScene.Weapon), Is.EqualTo(3), "a round the weapon takes is loaded");
                return;
            }

            Assert.That(RoundsLeft(portScene.Weapon), Is.EqualTo(2), "a round the weapon doesn't take isn't loaded");
            Assert.That(SEntMan.System<SharedHandsSystem>().IsHolding(SPlayer, cartridge), "and nothing is taken from the hand");
        });
    }

    /// <summary>
    ///     A box loads as many of its rounds as fit and keeps the rest.
    /// </summary>
    [Test]
    public async Task LoadsWhatFitsFromABox()
    {
        var portScene = await SpawnPortAsync(rounds: 0);
        var box = await HoldAsync(_ammoBox);
        var room = 0;
        var inBox = 0;
        await Server.WaitPost(() =>
        {
            var weapon = SEntity<BallisticAmmoProviderComponent>(portScene.Weapon);
            room = Capacity(portScene.Weapon) / 2;
            SEntMan.System<SharedGunSystem>().SetBallisticUnspawned(weapon, Capacity(portScene.Weapon) - room);
            inBox = RoundsLeft(box);
        });
        Assert.That(inBox, Is.GreaterThan(room), "precondition: the box holds more than fits");

        var cartridges = 0;
        await Server.WaitPost(() => cartridges = CartridgeEntities());
        await UseOnPortAsync(portScene, box);
        await Server.WaitAssertion(() =>
        {
            Assert.That(RoundsLeft(portScene.Weapon), Is.EqualTo(Capacity(portScene.Weapon)), "the weapon is filled");
            Assert.That(CartridgeEntities(), Is.EqualTo(cartridges), "by moving counts: no round is made");
            Assert.That(RoundsLeft(box), Is.EqualTo(inBox - room), "and the box keeps what didn't fit");
        });
    }

    /// <summary>
    ///     A full weapon takes nothing.
    /// </summary>
    [Test]
    public async Task FullWeaponRefuses()
    {
        var portScene = await SpawnPortAsync(rounds: 0);
        await Server.WaitPost(() =>
            SEntMan.System<SharedGunSystem>().SetBallisticUnspawned(SEntity<BallisticAmmoProviderComponent>(portScene.Weapon), Capacity(portScene.Weapon)));
        var box = await HoldAsync(_ammoBox);
        var inBox = 0;
        await Server.WaitPost(() => inBox = RoundsLeft(box));

        await UseOnPortAsync(portScene, box);
        await Server.WaitAssertion(() =>
        {
            Assert.That(RoundsLeft(portScene.Weapon), Is.EqualTo(Capacity(portScene.Weapon)));
            Assert.That(RoundsLeft(box), Is.EqualTo(inBox), "a full weapon takes nothing from the box");
        });
    }

    /// <summary>
    ///     Examining the port tells how many rounds its weapon has, of how many.
    /// </summary>
    [Test]
    public async Task ExamineShowsTheCount()
    {
        var portScene = await SpawnPortAsync(rounds: 3);
        await Server.WaitAssertion(() =>
        {
            var text = SEntMan.System<ExamineSystemShared>().GetExamineText(portScene.Port, SPlayer).ToMarkup();
            Assert.That(text, Does.Contain($"{RoundsLeft(portScene.Weapon)}[/color] of {Capacity(portScene.Weapon)}"),
                $"the examine text should show the count: {text}");
        });
    }
}
