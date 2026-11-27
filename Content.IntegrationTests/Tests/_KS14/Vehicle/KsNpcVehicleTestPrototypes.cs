#nullable enable
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Vehicle;

/// <summary>
///     Vehicles and drivers for the tests of NPCs and vehicles: a loose body three tiles long, like a crew vehicle's hull,
///         and mobs with nothing but a side and a life.
/// </summary>
public static class KsNpcVehicleTestPrototypes
{
    // Only in these [TestPrototypes], so instance fields wherever they are used: the linter checks static ones only.
    public const string FactionVehicle = "KsNpcVehicleTestVehicle";
    public const string VehicleOwnSide = "KsNpcVehicleTestVehicleOwnSide";
    public const string VehiclePlain = "KsNpcVehicleTestVehiclePlain";
    public const string Crate = "KsNpcVehicleTestCrate";
    public const string AnchoredBlock = "KsNpcVehicleTestAnchoredBlock";
    public const string DriverNanoTrasen = "KsNpcVehicleTestDriverNanoTrasen";
    public const string DriverSyndicate = "KsNpcVehicleTestDriverSyndicate";
    public const string DriverFactionless = "KsNpcVehicleTestDriverFactionless";
    public const string MountedWeapon = "KsNpcVehicleTestMountedWeapon";

    [TestPrototypes]
    public const string Prototypes = @"
- type: entity
  abstract: true
  id: KsNpcVehicleTestBody
  components:
  - type: Physics
    bodyType: Dynamic
    fixedRotation: true
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeAabb
          bounds: '-0.6,-1.2,0.6,1.2'
        density: 200
        mask:
        - MobMask
        layer:
        - MidImpassable
        - BulletImpassable
  - type: Damageable
  - type: Destructible
    thresholds:
    - trigger:
        !type:DamageTrigger
        damage: 500

- type: entity
  abstract: true
  parent: KsNpcVehicleTestBody
  id: KsNpcVehicleTestVehicleBase
  components:
  - type: InputMover
  - type: MovementSpeedModifier
  - type: Vehicle
    requiresHands: false
    transferDamage: false

- type: entity
  parent: KsNpcVehicleTestVehicleBase
  id: KsNpcVehicleTestVehicle
  components:
  - type: KsVehicleOperatorFactions

- type: entity
  parent: KsNpcVehicleTestVehicleBase
  id: KsNpcVehicleTestVehicleOwnSide
  components:
  - type: KsVehicleOperatorFactions
    ownFactions:
    - NanoTrasen

- type: entity
  parent: KsNpcVehicleTestVehicleBase
  id: KsNpcVehicleTestVehiclePlain

- type: entity
  parent: KsNpcVehicleTestBody
  id: KsNpcVehicleTestCrate

- type: entity
  id: KsNpcVehicleTestAnchoredBlock
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
          bounds: '-0.6,-1.2,0.6,1.2'
        density: 200
        mask:
        - MobMask
        layer:
        - MidImpassable
        - BulletImpassable

- type: entity
  id: KsNpcVehicleTestDriverFactionless
  components:
  - type: Eye # the vehicle takes over the driver's eye
  - type: MobState
  - type: MobThresholds
    thresholds:
      0: Alive
      100: Critical
      200: Dead
  - type: Damageable
    damageContainer: Biological

- type: entity
  parent: KsNpcVehicleTestDriverFactionless
  id: KsNpcVehicleTestDriverNanoTrasen
  components:
  - type: NpcFactionMember
    factions:
    - NanoTrasen

- type: entity
  parent: KsNpcVehicleTestDriverFactionless
  id: KsNpcVehicleTestDriverSyndicate
  components:
  - type: NpcFactionMember
    factions:
    - Syndicate

- type: entity
  id: KsNpcVehicleTestMountedWeapon
  components:
  - type: KsCrewVehicleWeapon
";
}
