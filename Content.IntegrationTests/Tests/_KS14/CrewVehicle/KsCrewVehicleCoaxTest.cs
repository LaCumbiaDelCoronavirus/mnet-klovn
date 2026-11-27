#nullable enable
using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Client.Actions;
using Content.Server._KS14.CrewVehicle.Mounts;
using Content.Shared._KS14.CrewVehicle.Gunner;
using Content.Shared._KS14.CrewVehicle.Mounts;
using Content.Shared._KS14.CrewVehicle.Turret;
using Content.Shared.Actions.Components;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.CrewVehicle;

/// <summary>
///     Weapons mounted on other weapons: a coaxial gun fixed to its turret, which aiming it turns and which fires once the
///         turret has brought it round, and a gun turning on its own on top of a turret, for a gunner of its own. Every
///         number comes from the test prototypes.
/// </summary>
[TestOf(typeof(KsCrewVehicleTurretSystem))]
public sealed class KsCrewVehicleCoaxTest : KsCrewVehicleGunnerTestBase
{
    private readonly EntProtoId _coaxHull = "KsCrewVehicleCoaxTestHull";
    private readonly EntProtoId _badCoaxHull = "KsCrewVehicleCoaxTestHullBad";
    private readonly EntProtoId _independentCoaxHull = "KsCrewVehicleCoaxTestHullIndependent";
    private readonly EntProtoId _orphanHull = "KsCrewVehicleCoaxTestHullOrphan";

    private const string TurretMount = "turret";
    private const string CoaxMount = "coax";
    private const string MgMount = "mg";

    // Interior: Maps/Test/_KS14/CrewVehicle/coax.yml is multi_gunner.yml with gunner seat A on `turret` and `coax`, one
    //      action cycling them, and gunner seat B on `mg`.
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: KsCrewVehicleGunnerTestGun
  id: KsCrewVehicleCoaxTestCoax
  categories: [ HideSpawnMenu ]
  components:
  - type: KsCrewVehicleWeapon
    muzzleOffset: 0,-0.3
    maxAimRange: 5
  - type: KsCrewVehicleCoaxial
    fireTolerance: 5

- type: entity
  parent: KsCrewVehicleGunnerTestTurret
  id: KsCrewVehicleCoaxTestIndependent
  categories: [ HideSpawnMenu ]
  components:
  - type: KsCrewVehicleWeapon
    muzzleOffset: 0,-0.3
    maxAimRange: 5

- type: entity
  parent: KsCrewVehicleDriveTestHull
  id: KsCrewVehicleCoaxTestHull
  components:
  - type: KsCrewVehicleInterior
    interiorPath: /Maps/Test/_KS14/CrewVehicle/coax.yml
    interiorAnchor: 2.5,3
    doorways:
      left:
        offset: 1,0
      right:
        offset: -1,0
  - type: KsCrewVehicleMounts
    mounts:
      turret:
        prototype: KsCrewVehicleGunnerTestTurret
      coax:
        prototype: KsCrewVehicleCoaxTestCoax
        parent: turret
        offset: -0.3,0
      mg:
        prototype: KsCrewVehicleCoaxTestIndependent
        parent: turret
        offset: 0.3,0

# multi_gunner.yml's seat B fires `right` and `rear`; here `right` is fixed to `left`, seat A's turret.
- type: entity
  parent: KsCrewVehicleMultiGunnerTestHull
  id: KsCrewVehicleCoaxTestHullBad
  components:
  - type: KsCrewVehicleMounts
    mounts:
      left:
        prototype: KsCrewVehicleGunnerTestTurret
        offset: 0.3,0
      right:
        prototype: KsCrewVehicleCoaxTestCoax
        parent: left
        offset: -0.3,0
      rear:
        prototype: KsCrewVehicleGunnerTestArcGun
        offset: 0,0.7
        rotation: 180

- type: entity
  parent: KsCrewVehicleMultiGunnerTestHull
  id: KsCrewVehicleCoaxTestHullIndependent
  components:
  - type: KsCrewVehicleMounts
    mounts:
      left:
        prototype: KsCrewVehicleGunnerTestTurret
        offset: 0.3,0
      right:
        prototype: KsCrewVehicleCoaxTestIndependent
        parent: left
        offset: -0.3,0
      rear:
        prototype: KsCrewVehicleGunnerTestArcGun
        offset: 0,0.7
        rotation: 180

- type: entity
  parent: KsCrewVehicleGunnerTestHull
  id: KsCrewVehicleCoaxTestHullOrphan
  components:
  - type: KsCrewVehicleMounts
    mounts:
      main:
        prototype: KsCrewVehicleGunnerTestGun
        parent: nowhere
";

    private sealed record CoaxScene(HullScene Scene, EntityUid Turret, EntityUid Coax, EntityUid Mg, EntityUid SeatA, EntityUid SeatB);

    private async Task<CoaxScene> SpawnCoaxAsync()
    {
        var scene = await SpawnHullAsync(Vector2.Zero, _coaxHull);
        CoaxScene coax = default!;
        await Server.WaitPost(() => coax = new CoaxScene(scene,
            Weapon(scene.Hull, TurretMount),
            Weapon(scene.Hull, CoaxMount),
            Weapon(scene.Hull, MgMount),
            GunnerSeat(scene, TurretMount),
            GunnerSeat(scene, MgMount)));
        return coax;
    }

    private async Task SeatPlayerAsync(CoaxScene coax, EntityUid seat)
    {
        await PutInsideAsync(coax.Scene);
        await SitAsync(seat);
        await SetCombatMode(true);
        await RunTicks(5);
    }

    private EntityUid? Selected(EntityUid gunner) => SComp<KsCrewVehicleGunnerComponent>(gunner).Selected;

    /// <summary>
    ///     The player switches weapon through their client, with the seat's one action.
    /// </summary>
    private async Task CycleAsync(EntityUid seat)
    {
        var actionNetEntity = NetEntity.Invalid;
        await Server.WaitPost(() => actionNetEntity = SEntMan.GetNetEntity(SComp<KsCrewVehicleGunnerSeatComponent>(seat).SelectActions.Single()));
        await Client.WaitPost(() =>
        {
            var actionUid = ToClient(actionNetEntity);
            CEntMan.System<ActionsSystem>().TriggerAction((actionUid, CEntMan.GetComponent<ActionComponent>(actionUid)));
        });
        await RunTicks(5);
    }

    /// <summary>
    ///     A point <paramref name="degrees"/> round from where <paramref name="weapon"/> points, inside its reach.
    /// </summary>
    private Vector2 OffTo(EntityUid weapon, double degrees)
    {
        var (position, rotation) = Transform.GetWorldPositionRotation(weapon);
        return position + (rotation + Angle.FromDegrees(degrees)).ToWorldVec() * SComp<KsCrewVehicleWeaponComponent>(weapon).MaxAimRange * 0.5f;
    }

    private static double Off(Angle a, Angle b) => Math.Abs(Angle.ShortestDistance(a, b).Theta);

    /// <summary>
    ///     A mount on another mount is spawned on that one's weapon, where its offset puts it, and turns with it; deleting
    ///         the turret takes both off the hull.
    /// </summary>
    [Test]
    public async Task ChildMountRidesItsParent()
    {
        var coax = await SpawnCoaxAsync();
        await Server.WaitAssertion(() =>
        {
            var mount = SComp<KsCrewVehicleMountsComponent>(coax.Scene.Hull).Mounts[CoaxMount];
            var coaxTransform = SComp<TransformComponent>(coax.Coax);
            Assert.That(coaxTransform.ParentUid, Is.EqualTo(coax.Turret), "the coaxial gun is the turret's child");
            Assert.That(Vector2.Distance(coaxTransform.LocalPosition, mount.Offset), Is.LessThan(0.001f), "at its offset on the turret");

            var muzzleBefore = MuzzlePosition(coax.Coax);
            Transform.SetLocalRotation(coax.Turret, SComp<TransformComponent>(coax.Turret).LocalRotation + Angle.FromDegrees(90));
            Assert.That(Vector2.Distance(MuzzlePosition(coax.Coax), muzzleBefore), Is.GreaterThan(0.1f), "turning the turret moves its muzzle");
            Assert.That(Off(Transform.GetWorldRotation(coax.Coax), Transform.GetWorldRotation(coax.Turret) + mount.Rotation), Is.LessThan(0.001),
                "and points it the turret's way");

            SEntMan.DeleteEntity(coax.Turret);
            var weapons = SComp<KsCrewVehicleMountsComponent>(coax.Scene.Hull).Weapons;
            Assert.That(weapons, Does.Not.ContainKey(TurretMount));
            Assert.That(weapons, Does.Not.ContainKey(CoaxMount), "the coaxial gun goes with its turret");
            Assert.That(SEntMan.Deleted(coax.Coax));
        });
    }

    /// <summary>
    ///     A mount on a mount the hull doesn't have is a mistake in the prototype: nothing is spawned for it.
    /// </summary>
    [Test]
    public async Task MissingParentSpawnsNothing()
    {
        await Server.WaitAssertion(() =>
        {
            var mountSystem = SEntMan.System<KsCrewVehicleMountSystem>();
            var level = mountSystem.Log.Level;
            mountSystem.Log.Level = Robust.Shared.Log.LogLevel.Fatal;
            EntityUid hull;
            try
            {
                hull = SEntMan.SpawnAtPosition(_orphanHull, OriginCoordinates.Offset(new Vector2(-6f, -6f)));
            }
            finally
            {
                mountSystem.Log.Level = level;
            }

            Assert.That(SComp<KsCrewVehicleMountsComponent>(hull).Weapons, Is.Empty);
            Assert.That(SEntMan.AllEntities<KsCrewVehicleWeaponComponent>().Any(weapon => SComp<TransformComponent>(weapon).ParentUid == hull), Is.False,
                "nothing is spawned on the hull in its place");
        });
    }

    /// <summary>
    ///     A seat set to cycle has one action, which switches to the next weapon in turn. With the coaxial gun selected,
    ///         the aim turns its turret, and an aim naming the coaxial gun itself is dropped.
    /// </summary>
    [Test]
    public async Task SelectingCoaxStillAimsTheTurret()
    {
        var coax = await SpawnCoaxAsync();
        await SeatPlayerAsync(coax, coax.SeatA);

        await Server.WaitAssertion(() =>
        {
            var actions = SComp<KsCrewVehicleGunnerSeatComponent>(coax.SeatA).SelectActions;
            Assert.That(actions, Has.Count.EqualTo(1), "one action cycles the seat's weapons");
            Assert.That(SComp<KsCrewVehicleSelectWeaponActionComponent>(actions[0]).Cycle);
            Assert.That(Selected(SPlayer), Is.EqualTo(coax.Turret), "the first mount is selected on sitting down");
        });

        await CycleAsync(coax.SeatA);
        await Server.WaitAssertion(() => Assert.That(Selected(SPlayer), Is.EqualTo(coax.Coax), "the action switches to the next"));
        await CycleAsync(coax.SeatA);
        await Server.WaitAssertion(() => Assert.That(Selected(SPlayer), Is.EqualTo(coax.Turret), "and round to the first"));
        await CycleAsync(coax.SeatA);
        await Server.WaitAssertion(() => Assert.That(Selected(SPlayer), Is.EqualTo(coax.Coax)));

        // The coaxial gun named: dropped, nothing turns.
        var target = Vector2.Zero;
        var coaxNetEntity = NetEntity.Invalid;
        var turretNetEntity = NetEntity.Invalid;
        await Server.WaitPost(() =>
        {
            target = OffTo(coax.Turret, 90);
            coaxNetEntity = SEntMan.GetNetEntity(coax.Coax);
            turretNetEntity = SEntMan.GetNetEntity(coax.Turret);
        });
        var targetCoordinates = OutsideAt(target);
        await Client.WaitPost(() => CEntMan.RaisePredictiveEvent(new KsCrewVehicleAimEvent { Weapon = coaxNetEntity, Target = targetCoordinates }));
        await RunTicks(5);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<KsCrewVehicleTurretAimComponent>(coax.Coax), Is.False, "the coaxial gun has no traverse of its own");
            Assert.That(SEntMan.HasComponent<KsCrewVehicleTurretAimComponent>(coax.Turret), Is.False, "an aim naming it turns nothing");
        });

        // The turret named: taken.
        await Client.WaitPost(() => CEntMan.RaisePredictiveEvent(new KsCrewVehicleAimEvent { Weapon = turretNetEntity, Target = targetCoordinates }));
        await RunTicks(5);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.TryGetComponent<KsCrewVehicleTurretAimComponent>(coax.Turret, out var aimComponent),
                "with the coaxial gun selected, the aim turns its turret");
            var expected = (target - WorldPosition(coax.Turret)).ToWorldAngle() - HullRotation(coax.Scene.Hull);
            Assert.That(Off(aimComponent!.TargetAngle, expected), Is.LessThan(0.01), "toward the point aimed at");
        });
    }

    /// <summary>
    ///     The coaxial gun holds its fire while its turret points elsewhere, and fires, from its own muzzle, once the
    ///         turret points within the gun's tolerance of the target: at a target this close, the gun beside the turret's
    ///         axis never points straight at it itself.
    /// </summary>
    [Test]
    public async Task CoaxHoldsFireUntilTheTurretIsOnTarget()
    {
        var coax = await SpawnCoaxAsync();
        await SeatPlayerAsync(coax, coax.SeatA);
        await CycleAsync(coax.SeatA);

        var target = Vector2.Zero;
        var tolerance = 0.0;
        var rounds = 0;
        await Server.WaitPost(() =>
        {
            Assert.That(Selected(SPlayer), Is.EqualTo(coax.Coax), "precondition: the coaxial gun is selected");
            target = OffTo(coax.Coax, 90);
            tolerance = SComp<KsCrewVehicleCoaxialComponent>(coax.Coax).FireTolerance.Theta;
            rounds = RoundsLeft(coax.Coax);

            SEntMan.System<KsCrewVehicleTurretSystem>().TryAim(SEntity<KsCrewVehicleWeaponComponent>(coax.Turret),
                Transform.ToCoordinates(MapData.Grid.Owner, new MapCoordinates(target, MapId)));
            ReceiveFrom(ServerSession, ShotAt(coax.Coax, OutsideAt(target)));
        });
        Assert.That(tolerance, Is.GreaterThan(0), "precondition: the test coaxial gun has a tolerance");
        await RunTicks(2);
        await Server.WaitAssertion(() => Assert.That(RoundsLeft(coax.Coax), Is.EqualTo(rounds), "it doesn't fire while its turret points away"));

        var fired = false;
        var offAtShot = 0.0;
        for (var tick = 0; tick < TickRate * 10 && !fired; tick++)
        {
            await Server.WaitPost(() =>
            {
                offAtShot = Off(Transform.GetWorldRotation(coax.Turret), (target - WorldPosition(coax.Turret)).ToWorldAngle());
                ReceiveFrom(ServerSession, ShotAt(coax.Coax, OutsideAt(target)));
            });
            await RunTicks(1);
            await Server.WaitPost(() => fired = RoundsLeft(coax.Coax) < rounds);
        }

        Assert.That(fired, "it fires once the turret has turned it onto the target");
        Assert.That(offAtShot, Is.LessThanOrEqualTo(tolerance + 0.01), "within its tolerance, and not before");
        await Server.WaitAssertion(() => Assert.That(Rounds(coax.Coax), Is.Not.Empty, "the round is the coaxial gun's"));
    }

    /// <summary>
    ///     A gun turning on its own on top of a turret aims relative to that turret and turns with it; its own gunner
    ///         fires it in the same tick as the turret's gunner fires the turret, each from its own muzzle.
    /// </summary>
    [Test]
    public async Task IndependentGunOnTurretAimsOnItsOwn()
    {
        var coax = await SpawnCoaxAsync();
        await SeatPlayerAsync(coax, coax.SeatA);
        var (mobB, sessionB) = await AddPlayerInsideAsync(coax.Scene);
        await SitAsync(coax.SeatB, mobB);

        // The turret turned first, so an aim measured from the hull would be off by as much.
        var target = Vector2.Zero;
        await Server.WaitPost(() =>
        {
            Transform.SetLocalRotation(coax.Turret, Angle.FromDegrees(60));
            target = OffTo(coax.Mg, -45);
            ReceiveFrom(sessionB, new KsCrewVehicleAimEvent { Weapon = SEntMan.GetNetEntity(coax.Mg), Target = OutsideAt(target) });
        });
        await RunTicks(1);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.TryGetComponent<KsCrewVehicleTurretAimComponent>(coax.Mg, out var aimComponent), "its gunner's aim is taken");
            var expected = (target - WorldPosition(coax.Mg)).ToWorldAngle() - Transform.GetWorldRotation(coax.Turret);
            Assert.That(Off(aimComponent!.TargetAngle, expected), Is.LessThan(0.01), "relative to the turret it rides");
        });

        var arrived = false;
        for (var tick = 0; tick < TickRate * 5 && !arrived; tick++)
        {
            await RunTicks(1);
            await Server.WaitPost(() => arrived = !SEntMan.HasComponent<KsCrewVehicleTurretTraversingComponent>(coax.Mg));
        }

        Assert.That(arrived, "precondition: it turns onto its aim");

        // Both gunners in one tick.
        var turretTarget = Vector2.Zero;
        await Server.WaitPost(() =>
        {
            turretTarget = OffTo(coax.Turret, 0);
            ReceiveFrom(ServerSession, ShotAt(coax.Turret, OutsideAt(turretTarget)));
            ReceiveFrom(sessionB, ShotAt(coax.Mg, OutsideAt(target)));
        });
        await RunTicks(2);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Rounds(coax.Turret), Has.Count.EqualTo(1), "the turret's gunner fires the turret");
            Assert.That(Rounds(coax.Mg), Has.Count.EqualTo(1), "and the other gunner the gun on top of it, the same tick");

            var mgWorld = Transform.GetWorldRotation(coax.Mg);
            Transform.SetLocalRotation(coax.Turret, SComp<TransformComponent>(coax.Turret).LocalRotation + Angle.FromDegrees(30));
            Assert.That(Off(Transform.GetWorldRotation(coax.Mg), mgWorld + Angle.FromDegrees(30)), Is.LessThan(0.001),
                "turning the turret carries the gun round with it");
        });
    }

    /// <summary>
    ///     A coaxial gun's seat must also fire its turret; a gun that turns on its own may have a seat to itself.
    /// </summary>
    [Test]
    public async Task FixedCoaxNeedsItsTurretsSeat()
    {
        await Server.WaitAssertion(() =>
        {
            var errors = RefusedInterior(_badCoaxHull);
            Assert.That(errors.Any(error => error.Contains("coaxial mount 'right'") && error.Contains("'left'")),
                $"the error names the coaxial mount and its turret: {string.Join("; ", errors)}");
        });

        // Loads: SpawnHullAsync asserts it.
        await SpawnHullAsync(new Vector2(6f, 6f), _independentCoaxHull);
    }
}
