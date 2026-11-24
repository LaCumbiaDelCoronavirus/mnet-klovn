#nullable enable
using System.Numerics;
using System.Threading.Tasks;
using Content.Shared._KS14.CrewVehicle.AmmoPort;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Weapons.Ranged.Components;
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

        await UseOnPortAsync(portScene, box);
        await Server.WaitAssertion(() =>
        {
            Assert.That(RoundsLeft(portScene.Weapon), Is.EqualTo(Capacity(portScene.Weapon)), "the weapon is filled");
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
