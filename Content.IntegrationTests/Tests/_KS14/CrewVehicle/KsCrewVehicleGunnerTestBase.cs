#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server._KS14.KillCredit;
using Content.Shared._KS14.CrewVehicle.Gunner;
using Content.Shared._KS14.CrewVehicle.Mounts;
using Content.Shared.CombatMode;
using Content.Shared.CombatMode.Pacification;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.CrewVehicle;

/// <summary>
///     What the gunner tests share: the test guns, hulls with them mounted, and firing them through the player's client or
///         as another session.
/// </summary>
public abstract class KsCrewVehicleGunnerTestBase : KsCrewVehicleDriveTestBase
{
    protected readonly EntProtoId _gunnerHull = "KsCrewVehicleGunnerTestHull";
    protected readonly EntProtoId _beamHull = "KsCrewVehicleGunnerTestHullBeam";
    protected readonly EntProtoId _badGunnerHull = "KsCrewVehicleGunnerTestHullBad";

    // Interiors: Maps/Test/_KS14/CrewVehicle/gunner.yml is drive.yml with its driver seat also a gunner seat on `main`;
    //      gunner_bad.yml has a seat on a mount the hull lacks, one that is not a view seat, and two on `main`.
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsCrewVehicleGunnerTestBullet
  categories: [ HideSpawnMenu ]
  components:
  - type: Physics
    bodyType: Dynamic
  - type: Fixtures
    fixtures:
      projectile:
        shape:
          !type:PhysShapeAabb
          bounds: ""-0.1,-0.1,0.1,0.1""
        hard: false
        mask:
        - Impassable
        - BulletImpassable
  - type: Projectile
    damage:
      types:
        Blunt: 7

- type: entity
  id: KsCrewVehicleGunnerTestCartridge
  categories: [ HideSpawnMenu ]
  components:
  - type: CartridgeAmmo
    proto: KsCrewVehicleGunnerTestBullet
    deleteOnSpawn: true
  - type: Item
  - type: Tag
    tags:
    - KsCrewVehicleGunnerTestRound

- type: Tag
  id: KsCrewVehicleGunnerTestRound

# A round of the same kind as the test gun's, which it doesn't take.
- type: entity
  parent: KsCrewVehicleGunnerTestCartridge
  id: KsCrewVehicleGunnerTestWrongCartridge
  categories: [ HideSpawnMenu ]
  components:
  - type: Tag
    tags: []

- type: entity
  id: KsCrewVehicleGunnerTestAmmoBox
  categories: [ HideSpawnMenu ]
  components:
  - type: Item
  - type: BallisticAmmoProvider
    proto: KsCrewVehicleGunnerTestCartridge
    capacity: 8
    whitelist:
      tags:
      - KsCrewVehicleGunnerTestRound

- type: entity
  parent: KsCrewVehicleInteriorBaseAmmoPort
  id: KsCrewVehicleGunnerTestAmmoPort
  components:
  - type: KsCrewVehicleAmmoPort
    mount: main
    loadDelay: 0.5

- type: entity
  id: KsCrewVehicleGunnerTestGun
  categories: [ HideSpawnMenu ]
  components:
  - type: Gun
    fireRate: 4
    minAngle: 0
    maxAngle: 0
    selectedMode: SemiAuto
    availableModes:
    - SemiAuto
    soundGunshot:
      path: /Audio/Weapons/Guns/Gunshots/pistol.ogg
  - type: BallisticAmmoProvider
    proto: KsCrewVehicleGunnerTestCartridge
    capacity: 10
    whitelist:
      tags:
      - KsCrewVehicleGunnerTestRound
  - type: KsCrewVehicleWeapon
    muzzleOffset: 0,-0.6
    maxAimRange: 5

- type: entity
  id: KsCrewVehicleGunnerTestBeam
  parent: BasicHitscan
  categories: [ HideSpawnMenu ]
  components:
  - type: HitscanBasicDamage
    damage:
      types:
        Heat: 7

- type: entity
  id: KsCrewVehicleGunnerTestBeamGun
  categories: [ HideSpawnMenu ]
  components:
  - type: Gun
    fireRate: 4
    minAngle: 0
    maxAngle: 0
    selectedMode: SemiAuto
    availableModes:
    - SemiAuto
    soundGunshot:
      path: /Audio/Weapons/Guns/Gunshots/laser.ogg
  - type: Battery
    maxCharge: 1000
    startingCharge: 1000
  - type: BatteryAmmoProvider
    proto: KsCrewVehicleGunnerTestBeam
    fireCost: 10
  - type: KsCrewVehicleWeapon
    muzzleOffset: 0,-0.6
    maxAimRange: 5

- type: entity
  parent: KsCrewVehicleDriveTestHull
  id: KsCrewVehicleGunnerTestHull
  components:
  - type: KsCrewVehicleInterior
    interiorPath: /Maps/Test/_KS14/CrewVehicle/gunner.yml
    interiorAnchor: 2.5,3
    doorways:
      left:
        offset: 1,0
      right:
        offset: -1,0
  - type: KsCrewVehicleMounts
    mounts:
      main:
        prototype: KsCrewVehicleGunnerTestGun
        offset: 0,-0.6

- type: entity
  parent: KsCrewVehicleGunnerTestHull
  id: KsCrewVehicleGunnerTestHullBeam
  components:
  - type: KsCrewVehicleMounts
    mounts:
      main:
        prototype: KsCrewVehicleGunnerTestBeamGun
        offset: 0,-0.6

- type: entity
  parent: KsCrewVehicleGunnerTestHull
  id: KsCrewVehicleGunnerTestHullBad
  components:
  - type: KsCrewVehicleInterior
    interiorPath: /Maps/Test/_KS14/CrewVehicle/gunner_bad.yml
    interiorAnchor: 2.5,3
    doorways:
      left:
        offset: 1,0
      right:
        offset: -1,0
";

    protected const string MountId = "main";

    /// <summary>A generous wait for a shot request to reach the server and its round to fly.</summary>
    protected int ShotTicks => TickRate;

    protected EntityUid Weapon(EntityUid hull)
    {
        Assert.That(SEntMan.System<SharedKsCrewVehicleMountSystem>().TryGetWeapon(hull, MountId, out var weaponUid), Is.True,
            "precondition: the hull has its weapon");
        return weaponUid!.Value;
    }

    protected int RoundsLeft(EntityUid weapon)
    {
        var ev = new GetAmmoCountEvent();
        SEntMan.EventBus.RaiseLocalEvent(weapon, ref ev);
        return ev.Count;
    }

    protected List<Entity<ProjectileComponent>> Rounds(EntityUid weapon)
    {
        return SEntMan.AllEntities<ProjectileComponent>().Where(round => round.Comp.Weapon == weapon).ToList();
    }

    protected Vector2 WorldPosition(EntityUid uid) => Transform.GetWorldPosition(uid);

    protected bool Damaged(EntityUid uid) =>
        SEntMan.System<DamageableSystem>().GetAllDamage(SEntity<DamageableComponent>(uid).AsNullable()).AnyPositive();

    protected Vector2 MuzzlePosition(EntityUid weapon)
    {
        var muzzle = SEntMan.System<SharedKsCrewVehicleMountSystem>().GetMuzzleCoordinates(SEntity<KsCrewVehicleWeaponComponent>(weapon));
        return Transform.ToMapCoordinates(muzzle).Position;
    }

    /// <summary>
    ///     A point on the outside test grid at <paramref name="worldPosition"/>.
    /// </summary>
    protected NetCoordinates OutsideAt(Vector2 worldPosition)
    {
        return SEntMan.GetNetCoordinates(Transform.ToCoordinates(MapData.Grid.Owner, new MapCoordinates(worldPosition, MapId)));
    }

    protected async Task<(HullScene Scene, EntityUid Weapon)> SeatGunnerAsync(EntProtoId? hull = null, bool passenger = false)
    {
        var scene = await SpawnHullAsync(Vector2.Zero, hull ?? _gunnerHull);
        await PutInsideAsync(scene);
        await SitAsync(passenger ? scene.PassengerSeat : scene.DriverSeat);
        await SetCombatMode(true);
        await RunTicks(5);

        var weapon = EntityUid.Invalid;
        await Server.WaitPost(() => weapon = Weapon(scene.Hull));
        return (scene, weapon);
    }

    /// <summary>
    ///     The player's client asks to fire <paramref name="weapon"/> at <paramref name="target"/>, as its fire input does.
    /// </summary>
    protected async Task RequestShotAsync(EntityUid weapon, NetCoordinates target)
    {
        var weaponNetEntity = SEntMan.GetNetEntity(weapon);
        await Client.WaitPost(() => CEntMan.RaisePredictiveEvent(new RequestShootEvent
        {
            Gun = weaponNetEntity,
            Coordinates = target,
        }));
        await RunTicks(ShotTicks);
    }

    protected readonly EntProtoId _multiGunnerHull = "KsCrewVehicleMultiGunnerTestHull";

    protected const string LeftMount = "left";
    protected const string RightMount = "right";
    protected const string RearMount = "rear";

    // Interior: Maps/Test/_KS14/CrewVehicle/multi_gunner.yml has a driver seat with no weapons, gunner seat A on `left`
    //      and gunner seat B on `right` and `rear`, each gunner seat with its own pvsScale.
    [TestPrototypes]
    private const string MultiGunnerPrototypes = @"
- type: entity
  parent: KsCrewVehicleGunnerTestGun
  id: KsCrewVehicleGunnerTestTurret
  categories: [ HideSpawnMenu ]
  components:
  - type: KsCrewVehicleTurret
    traverseRate: 90
    fireTolerance: 5

- type: entity
  parent: KsCrewVehicleGunnerTestGun
  id: KsCrewVehicleGunnerTestArcGun
  categories: [ HideSpawnMenu ]
  components:
  - type: KsCrewVehicleWeapon
    muzzleOffset: 0,-0.6
    maxAimRange: 5
    fireArc: 60

- type: entity
  parent: KsCrewVehicleDriveTestHull
  id: KsCrewVehicleMultiGunnerTestHull
  components:
  - type: KsCrewVehicleInterior
    interiorPath: /Maps/Test/_KS14/CrewVehicle/multi_gunner.yml
    interiorAnchor: 2.5,3
    doorways:
      left:
        offset: 1,0
      right:
        offset: -1,0
  - type: KsCrewVehicleMounts
    mounts:
      left:
        prototype: KsCrewVehicleGunnerTestTurret
        offset: 0.3,0
      right:
        prototype: KsCrewVehicleGunnerTestTurret
        offset: -0.3,0
      rear:
        prototype: KsCrewVehicleGunnerTestArcGun
        offset: 0,0.7
        rotation: 180
";

    protected EntityUid Weapon(EntityUid hull, string mountId)
    {
        Assert.That(SEntMan.System<SharedKsCrewVehicleMountSystem>().TryGetWeapon(hull, mountId, out var weaponUid), Is.True,
            $"precondition: the hull has its {mountId} weapon");
        return weaponUid!.Value;
    }

    /// <summary>
    ///     The seat on <paramref name="scene"/>'s interior whose gunner seat lists <paramref name="mountId"/> first.
    /// </summary>
    protected EntityUid GunnerSeat(HullScene scene, string mountId)
    {
        var interiorMap = SComp<TransformComponent>(scene.InteriorGrid).MapUid;
        return SEntMan.AllEntities<KsCrewVehicleGunnerSeatComponent>()
            .Single(seat => SComp<TransformComponent>(seat).MapUid == interiorMap && seat.Comp.Mounts[0] == mountId).Owner;
    }

    /// <summary>
    ///     Another player, standing inside: a mob of their own, attached to a session of their own, in combat mode.
    /// </summary>
    protected async Task<(EntityUid Mob, ICommonSession Session)> AddPlayerInsideAsync(HullScene scene)
    {
        var session = await Server.AddDummySession();
        var mob = EntityUid.Invalid;
        await Server.WaitPost(() =>
        {
            mob = SEntMan.SpawnEntity(Human, SComp<TransformComponent>(scene.DriverSeat).Coordinates);
            Server.PlayerMan.SetAttachedEntity(session, mob);
            SEntMan.System<SharedCombatModeSystem>().SetInCombatMode(mob, true);
        });
        await RunTicks(5);
        return (mob, session);
    }

    /// <summary>
    ///     Hands <paramref name="message"/> to the server's handlers as though <paramref name="session"/>'s client sent it.
    ///         Call it on the server thread.
    /// </summary>
    protected void ReceiveFrom<T>(ICommonSession session, T message) where T : notnull
    {
        // The engine's wrapper for a message with its sender is internal.
        var wrapperType = typeof(EntitySessionEventArgs).Assembly
            .GetType("Robust.Shared.GameObjects.EntitySessionMessage`1")!
            .MakeGenericType(typeof(T));
        var wrapper = Activator.CreateInstance(wrapperType, new EntitySessionEventArgs(session), message)!;
        SEntMan.EventBus.RaiseEvent(EventSource.Network, wrapper);
    }

    protected RequestShootEvent ShotAt(EntityUid weapon, NetCoordinates target) => new()
    {
        Gun = SEntMan.GetNetEntity(weapon),
        Coordinates = target,
    };
}

/// <summary>
///     Records, as each hit lands, what dealt it and which player is paid for it.
/// </summary>
[RegisterComponent]
public sealed partial class KsCrewVehicleGunnerTestHitRecorderComponent : Component
{
    public readonly List<(EntityUid? Origin, NetUserId? Recipient)> Hits = new();
}

/// <summary>
///     Records where each shot of a gun was aimed, as it was fired.
/// </summary>
[RegisterComponent]
public sealed partial class KsCrewVehicleGunnerTestShotRecorderComponent : Component
{
    public readonly List<Vector2> AimedAt = new();
}

public sealed partial class KsCrewVehicleGunnerTestHitRecorderSystem : EntitySystem
{
    [SubscribeLocalEvent]
    private void OnGunShot(Entity<KsCrewVehicleGunnerTestShotRecorderComponent> entity, ref GunShotEvent args)
    {
        if (_netManager.IsServer)
            entity.Comp.AimedAt.Add(EntityManager.System<SharedTransformSystem>().ToMapCoordinates(args.ToCoordinates).Position);
    }

    [Dependency] private INetManager _netManager = default!;

    [SubscribeLocalEvent]
    private void OnDamageDealt(Entity<KsCrewVehicleGunnerTestHitRecorderComponent> entity, ref DamageDealtEvent args)
    {
        // The kill credit is the server's.
        if (!_netManager.IsServer)
            return;

        entity.Comp.Hits.Add((args.Origin, EntityManager.System<KsKillCreditSystem>().GetHitRecipient(args.Origin)));
    }
}
