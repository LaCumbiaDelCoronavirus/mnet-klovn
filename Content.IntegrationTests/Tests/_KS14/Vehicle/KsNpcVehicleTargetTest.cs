#nullable enable
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.Perception;
using Content.Server.NPC.Systems;
using Content.Shared._KS14.CrewVehicle.Mounts;
using Content.Shared._KS14.NPC;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.Projectiles;
using Content.Shared.Vehicle.Components;
using Content.Shared.Vehicle.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;
using static Content.IntegrationTests.Tests._KS14.Vehicle.KsNpcVehicleTestPrototypes;

namespace Content.IntegrationTests.Tests._KS14.Vehicle;

/// <summary>
///     NPCs see a vehicle as they would its driver: a hostile one in sight is a contact, an empty or friendly one is not,
///         and one emptied out of sight is not hunted. A mounted gun's shot gives away the vehicle it is mounted on.
/// </summary>
[TestOf(typeof(NpcPerceptionSystem))]
public sealed class KsNpcVehicleTargetTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private static readonly ProtoId<DamageTypePrototype> BluntDamage = "Blunt";

    // Off the grid, far past the observer's vision.
    private static readonly Vector2 FarAway = new(0.5f, 25.5f);

    // Only in this test's [TestPrototypes], so an instance field: the linter checks static ones only.
    private readonly EntProtoId _revealingObserver = "KsNpcVehicleTestObserverRevealing";
    private readonly EntProtoId _bullet = "KsNpcVehicleTestBullet";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: KsSquadTestMobLoner
  id: KsNpcVehicleTestObserverRevealing
  components:
  - type: Injurable
    damageContainer: Biological
  - type: NpcPerception
    revealAttackers: true

- type: entity
  id: KsNpcVehicleTestBullet
  components:
  - type: Projectile
    damage:
      types:
        Blunt: 1
";

    private readonly record struct Scene(EntityUid GridUid, EntityUid ObserverUid, EntityUid VehicleUid, EntityUid DriverUid);

    private async Task<Scene> SetUpScene(string driverPrototype, string? observerPrototype = null)
    {
        var map = await Pair.CreateTestMap();
        Scene scene = default;

        await Pair.Server.WaitPost(() =>
        {
            var gridUid = MakeGrid(SEntMan, Pair.Server.ResolveDependency<ITileDefinitionManager>(), map.MapId, map.Grid, new(-5, -5), new(10, 5)).Owner;
            var observerUid = SpawnAt(SEntMan, observerPrototype ?? LonerMob, gridUid, 0, 0);
            SEntMan.System<NPCSystem>().SleepNPC(observerUid);

            var vehicleUid = SpawnAt(SEntMan, FactionVehicle, gridUid, 3, 0);
            // Inside, where nobody outside can see them, as a crew vehicle's driver is.
            var driverUid = SEntMan.SpawnEntity(driverPrototype, new EntityCoordinates(gridUid, FarAway));
            scene = new Scene(gridUid, observerUid, vehicleUid, driverUid);
        });

        await Pair.RunTicksSync(5);
        return scene;
    }

    private void SetDriving(Scene scene, bool driving)
    {
        var vehicleSystem = SEntMan.System<VehicleSystem>();
        var vehicle = (scene.VehicleUid, SEntMan.GetComponent<VehicleComponent>(scene.VehicleUid));
        Assert.That(driving ? vehicleSystem.TrySetOperator(vehicle, scene.DriverUid) : vehicleSystem.TryRemoveOperator(vehicle), Is.True,
            "precondition: the driver gets in or out");
    }

    [Test]
    public async Task TestEmptyVehicleIsNotAContact()
    {
        var scene = await SetUpScene(DriverNanoTrasen);
        var perceptionSystem = SEntMan.System<NpcPerceptionSystem>();

        await Pair.Server.WaitAssertion(() =>
        {
            perceptionSystem.UpdateNow(scene.ObserverUid);
            Assert.That(perceptionSystem.TryGetContact(scene.ObserverUid, scene.VehicleUid, out _), Is.False, "nobody drives it");
            Assert.That(perceptionSystem.TryGetContact(scene.ObserverUid, scene.DriverUid, out _), Is.False,
                "and the driver is out of sight, or the test proves nothing");
        });
    }

    [Test]
    public async Task TestHostileDrivenVehicleIsSeen()
    {
        var scene = await SetUpScene(DriverNanoTrasen);
        var perceptionSystem = SEntMan.System<NpcPerceptionSystem>();

        await Pair.Server.WaitAssertion(() =>
        {
            SetDriving(scene, true);
            perceptionSystem.UpdateNow(scene.ObserverUid);
            Assert.That(perceptionSystem.IsVisible(scene.ObserverUid, scene.VehicleUid), "a hostile drives it, in plain sight");
        });
    }

    [Test]
    public async Task TestFriendlyDrivenVehicleIsNotAContact()
    {
        var scene = await SetUpScene(DriverSyndicate);
        var perceptionSystem = SEntMan.System<NpcPerceptionSystem>();

        await Pair.Server.WaitAssertion(() =>
        {
            SetDriving(scene, true);
            perceptionSystem.UpdateNow(scene.ObserverUid);
            Assert.That(perceptionSystem.TryGetContact(scene.ObserverUid, scene.VehicleUid, out _), Is.False, "a friend drives it");
        });
    }

    /// <summary>
    ///     A hostile vehicle lost from sight is remembered, as any hostile is; emptied while out of sight, it is forgotten,
    ///         as there is nobody in it left to hunt.
    /// </summary>
    [Test]
    public async Task TestLostVehicleForgottenWhenEmptied()
    {
        var scene = await SetUpScene(DriverNanoTrasen);
        var perceptionSystem = SEntMan.System<NpcPerceptionSystem>();

        await Pair.Server.WaitAssertion(() =>
        {
            SetDriving(scene, true);
            perceptionSystem.UpdateNow(scene.ObserverUid);
            Assert.That(perceptionSystem.IsVisible(scene.ObserverUid, scene.VehicleUid), "precondition: seen");

            SEntMan.System<SharedTransformSystem>().SetCoordinates(scene.VehicleUid, new EntityCoordinates(scene.GridUid, -FarAway));
            perceptionSystem.UpdateNow(scene.ObserverUid);
            Assert.That(perceptionSystem.TryGetContact(scene.ObserverUid, scene.VehicleUid, out var contact), "precondition: remembered once gone");
            Assert.That(contact.State, Is.Not.EqualTo(NpcContactState.Visible), "precondition: no longer in sight");

            SetDriving(scene, false);
            perceptionSystem.UpdateNow(scene.ObserverUid);
            Assert.That(perceptionSystem.TryGetContact(scene.ObserverUid, scene.VehicleUid, out _), Is.False,
                "emptied, it should be forgotten wherever it is");
        });
    }

    /// <summary>
    ///     Shot by a gun mounted on a hostile vehicle, the NPC learns of the vehicle, which it can see and shoot back, and
    ///         not of the gun.
    /// </summary>
    [Test]
    public async Task TestMountedGunRevealsItsVehicle()
    {
        var scene = await SetUpScene(DriverNanoTrasen, _revealingObserver);
        var perceptionSystem = SEntMan.System<NpcPerceptionSystem>();

        await Pair.Server.WaitAssertion(() =>
        {
            SetDriving(scene, true);
            // Far off, where only being shot could tell of it.
            SEntMan.System<SharedTransformSystem>().SetCoordinates(scene.VehicleUid, new EntityCoordinates(scene.GridUid, -FarAway));

            var weaponUid = SEntMan.SpawnEntity(MountedWeapon, new EntityCoordinates(scene.GridUid, -FarAway));
#pragma warning disable RA0002
            SEntMan.GetComponent<KsCrewVehicleWeaponComponent>(weaponUid).Vehicle = scene.VehicleUid;
#pragma warning restore RA0002
            var bulletUid = SEntMan.SpawnEntity(_bullet, new EntityCoordinates(scene.GridUid, -FarAway));
            SEntMan.GetComponent<ProjectileComponent>(bulletUid).Shooter = weaponUid;

            var damage = new DamageSpecifier(SProtoMan.Index(BluntDamage), 5);
            SEntMan.System<DamageableSystem>().TryChangeDamage(scene.ObserverUid, damage, origin: bulletUid);

            Assert.That(perceptionSystem.TryGetContact(scene.ObserverUid, scene.VehicleUid, out var contact), "shot by its gun, it should know of the vehicle");
            Assert.That(contact.State, Is.EqualTo(NpcContactState.Visible));
            Assert.That(perceptionSystem.TryGetContact(scene.ObserverUid, weaponUid, out _), Is.False, "the vehicle, not the gun");
        });
    }
}
