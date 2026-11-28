#nullable enable
using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Shared._KS14.CrewVehicle;
using Content.Shared._KS14.CrewVehicle.AmmoPort;
using Content.Shared._KS14.CrewVehicle.Drive;
using Content.Shared._KS14.CrewVehicle.Gunner;
using Content.Shared._KS14.CrewVehicle.Interior;
using Content.Shared._KS14.CrewVehicle.Mounts;
using Content.Shared._KS14.CrewVehicle.Turret;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Destructible;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.CrewVehicle;

/// <summary>
///     The crewed tank as it ships: its guns, its seats and its breech, and what its two shells do. Every number comes
///         from its prototypes.
/// </summary>
[TestOf(typeof(SharedKsCrewVehicleAmmoPortSystem))]
public sealed class KsCrewVehicleTankTest : KsCrewVehicleGunnerTestBase
{
    private static readonly EntProtoId Tank = "KsCrewVehicleTank";
    private static readonly EntProtoId HeShell = "KsTankShellHE";
    private static readonly EntProtoId ApShell = "KsTankShellAP";
    private static readonly EntProtoId RifleRound = "CartridgeLightRifle";
    private static readonly EntProtoId RifleBox = "MagazineBoxLightRifle";

    private EntityUid Port(HullScene scene, string mountId)
    {
        var interiorMap = SComp<TransformComponent>(scene.InteriorGrid).MapUid;
        return SEntMan.AllEntities<KsCrewVehicleAmmoPortComponent>()
            .Single(port => SComp<TransformComponent>(port).MapUid == interiorMap && port.Comp.Mount == mountId).Owner;
    }

    private async Task<EntityUid> HoldAsync(EntProtoId prototype)
    {
        var held = EntityUid.Invalid;
        await Server.WaitPost(() =>
        {
            held = SEntMan.SpawnAtPosition(prototype, SComp<TransformComponent>(SPlayer).Coordinates);
            Assert.That(SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(SPlayer, held), $"precondition: picked up {prototype}");
        });
        await RunTicks(5);
        return held;
    }

    private async Task UseOnAsync(EntityUid port, EntityUid held)
    {
        await Server.WaitPost(() => InteractSys.InteractUsing(SPlayer, held, port, SComp<TransformComponent>(port).Coordinates));
        var delay = SComp<KsCrewVehicleAmmoPortComponent>(port).LoadDelay;
        await RunTicks((int)(delay.TotalSeconds * TickRate) * 2 + 5);
    }

    private int Capacity(EntityUid weapon) => SComp<BallisticAmmoProviderComponent>(weapon).Capacity;

    /// <summary>
    ///     Its driver fires both guns, switching with one action; the machine gun rides the cannon's turret, fixed to it;
    ///         nobody standing inside sees out; it is tracked.
    /// </summary>
    [Test]
    public async Task DriverFiresBothGunsFromASealedCabin()
    {
        var scene = await SpawnHullAsync(new Vector2(0f, 5f), Tank);
        await PutInsideAsync(scene);
        await SitAsync(scene.DriverSeat);

        await Server.WaitAssertion(() =>
        {
            var seat = SComp<KsCrewVehicleGunnerSeatComponent>(scene.DriverSeat);
            Assert.That(seat.Mounts, Has.Count.EqualTo(2), "the driver fires both guns");
            var cannon = Weapon(scene.Hull, seat.Mounts[0]);
            var coax = Weapon(scene.Hull, seat.Mounts[1]);
            Assert.That(SEntMan.HasComponent<KsCrewVehicleTurretComponent>(cannon), "the cannon turns in its turret");
            Assert.That(SEntMan.HasComponent<KsCrewVehicleCoaxialComponent>(coax), "the machine gun is fixed to it");
            Assert.That(SComp<TransformComponent>(coax).ParentUid, Is.EqualTo(cannon), "riding the turret");

            Assert.That(SComp<KsCrewVehicleGunnerComponent>(SPlayer).Weapons, Is.EqualTo(new[] { cannon, coax }));
            Assert.That(seat.SelectActions, Has.Count.EqualTo(1), "one action switches between them");
            Assert.That(SComp<KsCrewVehicleSelectWeaponActionComponent>(seat.SelectActions[0]).Cycle);

            Assert.That(SComp<KsCrewVehicleInteriorComponent>(scene.Hull).OutsideBackdrop, Is.False, "standing inside, nobody sees out");
            Assert.That(SComp<KsCrewVehicleDriveComponent>(scene.Hull).DriveTrain, Is.EqualTo(KsCrewVehicleDriveTrain.Tracked));
        });
    }

    /// <summary>
    ///     The breech takes one tank shell and no more, refuses anything else, and gives back the one in it; the machine
    ///         gun's loader takes a box of rifle rounds.
    /// </summary>
    [Test]
    public async Task BreechTakesOneShellAtATime()
    {
        var scene = await SpawnHullAsync(new Vector2(0f, 5f), Tank);
        await PutInsideAsync(scene);

        EntityUid breech = default, loader = default, cannon = default, coax = default;
        await Server.WaitPost(() =>
        {
            var seat = SComp<KsCrewVehicleGunnerSeatComponent>(scene.DriverSeat);
            cannon = Weapon(scene.Hull, seat.Mounts[0]);
            coax = Weapon(scene.Hull, seat.Mounts[1]);
            breech = Port(scene, seat.Mounts[0]);
            loader = Port(scene, seat.Mounts[1]);
            Assert.That(RoundsLeft(cannon), Is.Zero, "precondition: the cannon starts empty");
        });

        var he = await HoldAsync(HeShell);
        await UseOnAsync(breech, he);
        await Server.WaitAssertion(() => Assert.That(RoundsLeft(cannon), Is.EqualTo(Capacity(cannon)), "a shell fills the breech"));

        var ap = await HoldAsync(ApShell);
        await UseOnAsync(breech, ap);
        await Server.WaitAssertion(() =>
        {
            Assert.That(RoundsLeft(cannon), Is.EqualTo(Capacity(cannon)), "a full breech takes no second shell");
            Assert.That(SEntMan.System<SharedHandsSystem>().IsHolding(SPlayer, ap), "which stays in hand");

            Assert.That(SEntMan.System<SharedKsCrewVehicleAmmoPortSystem>().TryUnload(SEntity<KsCrewVehicleAmmoPortComponent>(breech), SPlayer),
                "the shell in it can be taken out");
            Assert.That(RoundsLeft(cannon), Is.Zero);
            Assert.That(SEntMan.System<SharedHandsSystem>().IsHolding(SPlayer, he), "into the other hand");
            SEntMan.DeleteEntity(he);
        });
        await RunTicks(2);

        var rifleRound = await HoldAsync(RifleRound);
        await UseOnAsync(breech, rifleRound);
        await Server.WaitAssertion(() => Assert.That(RoundsLeft(cannon), Is.Zero, "a rifle round doesn't fit the breech"));

        await Server.WaitPost(() =>
        {
            SEntMan.DeleteEntity(rifleRound);
            SEntMan.DeleteEntity(ap);
            SEntMan.System<SharedGunSystem>().SetBallisticUnspawned(SEntity<BallisticAmmoProviderComponent>(coax), 0);
        });
        var box = await HoldAsync(RifleBox);
        await UseOnAsync(loader, box);
        await Server.WaitAssertion(() => Assert.That(RoundsLeft(coax), Is.GreaterThan(0), "the machine gun's loader takes a box of rifle rounds"));
    }

    /// <summary>
    ///     An HE shell bursts on the hull it hits; an AP shell hits it far harder, but one alone doesn't destroy it. (Where
    ///         an AP shell goes after it has punched through is the penetration framework's.)
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task ShellsHitTheHull(bool armourPiercing)
    {
        EntityUid hull = default;
        EntityUid round = default;
        await Server.WaitPost(() =>
        {
            Transform.SetCoordinates(SPlayer, OriginCoordinates.Offset(new Vector2(-10f, -10f)));
            hull = SEntMan.SpawnAtPosition(Tank, OriginCoordinates.Offset(new Vector2(0f, 4f)));
        });
        await RunTicks(5);

        float destroyedAt = 0f;
        float directHit = 0f;
        await Server.WaitPost(() =>
        {
            var shellPrototype = ProtoMan.Index(armourPiercing ? ApShell : HeShell);
            Assert.That(shellPrototype.TryComp<CartridgeAmmoComponent>(out var cartridgeComponent, SEntMan.ComponentFactory), "precondition: a shell");
            var start = Transform.GetWorldPosition(hull) + new Vector2(5f, 0f);
            round = SEntMan.SpawnEntity(cartridgeComponent!.Prototype, new MapCoordinates(start, MapId));
            SEntMan.System<SharedGunSystem>().ShootProjectile(round, new Vector2(-1f, 0f), Vector2.Zero, gunUid: null, speed: 20f);
            destroyedAt = SEntMan.System<SharedDestructibleSystem>().DestroyedAt(hull).Float();
            directHit = SComp<ProjectileComponent>(round).Damage.GetTotal().Float();
        });
        await RunTicks(TickRate);

        await Server.WaitAssertion(() =>
        {
            if (!armourPiercing)
                Assert.That(SEntMan.Deleted(round) || SEntMan.IsQueuedForDeletion(round), "the HE shell bursts on the hull");
            Assert.That(SEntMan.Deleted(hull), Is.False, "one shell doesn't destroy a tank");
            var damage = SEntMan.System<DamageableSystem>().GetTotalDamage(SEntity<DamageableComponent>(hull).AsNullable()).Float();
            if (armourPiercing)
                Assert.That(damage, Is.GreaterThanOrEqualTo(destroyedAt * 0.25f), $"an AP shell hits hard: {damage} of {destroyedAt}");
            else
                Assert.That(damage, Is.GreaterThan(directHit), $"an HE shell's burst damages it more than its hit alone: {damage}, the hit {directHit}");
        });
    }

    /// <summary>
    ///     Each hatch inside leads out beside the crew compartment, on the side of the hull it is in: its doorway is past
    ///         the hull's side that the hatch's place in the cabin maps to, and along the compartment, not behind it in the
    ///         engine bay.
    /// </summary>
    [Test]
    public async Task HatchesLeadOutBesideTheCompartment()
    {
        var scene = await SpawnHullAsync(new Vector2(0f, 5f), Tank);

        await Server.WaitAssertion(() =>
        {
            var interiorComponent = SComp<KsCrewVehicleInteriorComponent>(scene.Hull);
            var compartment = interiorComponent.PassThroughHullBounds!.Value;
            var hullBox = SComp<Robust.Shared.Physics.FixturesComponent>(scene.Hull).Fixtures["fix1"].Shape
                .ComputeAABB(Robust.Shared.Physics.Transform.Empty, 0);
            var cabin = SComp<Robust.Shared.Map.Components.MapGridComponent>(scene.InteriorGrid).LocalAABB;
            var cabinRotation = KsCrewVehicleHullFrame.GetCabinRotation(interiorComponent.CabinFront);

            var exits = 0;
            foreach (var exit in SEntMan.AllEntities<KsCrewVehicleExitComponent>())
            {
                var exitTransform = SComp<TransformComponent>(exit);
                if (exitTransform.ParentUid != scene.InteriorGrid)
                    continue;

                exits++;
                var doorway = interiorComponent.Doorways[exit.Comp.Doorway].Offset;
                var hatch = KsCrewVehiclePassThroughFrame.InteriorToHull(compartment, cabin, exitTransform.LocalPosition,
                    cabinRotation: cabinRotation);

                Assert.That(doorway.Y, Is.InRange(compartment.Bottom, compartment.Top),
                    $"{exit.Comp.Doorway} leads out alongside the compartment, not behind it");
                Assert.That(Math.Abs(doorway.X), Is.GreaterThan(hullBox.Right), $"{exit.Comp.Doorway} leads out past the hull's side");
                Assert.That(Math.Sign(doorway.X), Is.EqualTo(Math.Sign(hatch.X)),
                    $"{exit.Comp.Doorway} leads out of the side its hatch is on, at {hatch} in the hull");
            }

            Assert.That(exits, Is.GreaterThan(0), "precondition: the tank has a hatch");
        });
    }
}
