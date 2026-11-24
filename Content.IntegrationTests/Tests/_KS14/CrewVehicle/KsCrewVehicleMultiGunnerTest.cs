#nullable enable
using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Client.Actions;
using Content.Shared._KS14.CrewVehicle.Gunner;
using Content.Shared._KS14.CrewVehicle.Mounts;
using Content.Shared._KS14.CrewVehicle.Turret;
using Content.Shared.Actions.Components;
using Content.Shared.Alert;
using Content.Shared.Alert.Components;
using Content.Shared.Movement.Components;
using Content.Shared.Weapons.Ranged.Events;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.CrewVehicle;

/// <summary>
///     The multi-gunner test vehicle: a driver and two gunners, one with a turret, one with a turret and a fixed gun
///         with a fire arc. Every number comes from the test prototypes.
/// </summary>
[TestOf(typeof(KsCrewVehicleTurretSystem))]
public sealed class KsCrewVehicleMultiGunnerTest : KsCrewVehicleGunnerTestBase
{
    private readonly ProtoId<AlertPrototype> _ammoAlert = "KsCrewVehicleAmmo";

    private sealed record MultiScene(HullScene Scene, EntityUid Left, EntityUid Right, EntityUid Rear, EntityUid SeatA, EntityUid SeatB);

    private async Task<MultiScene> SpawnMultiAsync()
    {
        var scene = await SpawnHullAsync(Vector2.Zero, _multiGunnerHull);
        MultiScene multi = default!;
        await Server.WaitPost(() => multi = new MultiScene(scene,
            Weapon(scene.Hull, LeftMount),
            Weapon(scene.Hull, RightMount),
            Weapon(scene.Hull, RearMount),
            GunnerSeat(scene, LeftMount),
            GunnerSeat(scene, RightMount)));
        return multi;
    }

    private async Task SeatPlayerAsync(MultiScene multi, EntityUid seat)
    {
        await PutInsideAsync(multi.Scene);
        await SitAsync(seat);
        await SetCombatMode(true);
        await RunTicks(5);
    }

    /// <summary>
    ///     A point straight ahead of <paramref name="weapon"/>, as it points now, inside its reach.
    /// </summary>
    private Vector2 AheadOf(EntityUid weapon)
    {
        var (position, rotation) = Transform.GetWorldPositionRotation(weapon);
        return position + rotation.ToWorldVec() * SComp<KsCrewVehicleWeaponComponent>(weapon).MaxAimRange * 0.5f;
    }

    /// <summary>
    ///     Whether <paramref name="round"/> lies on the line out of <paramref name="weapon"/>'s muzzle toward
    ///         <paramref name="target"/>, past the muzzle.
    /// </summary>
    private bool CameFrom(EntityUid round, Vector2 muzzle, Vector2 target)
    {
        var aim = Vector2.Normalize(target - muzzle);
        var flown = WorldPosition(round) - muzzle;
        // Within a hundredth of a radian of the line, however far it has flown.
        var distance = flown.Length();
        return Vector2.Dot(flown, aim) > 0f && MathF.Abs(aim.X * flown.Y - aim.Y * flown.X) < 0.01f * distance + 0.01f;
    }

    private EntityUid SelectAction(EntityUid seat, string mountId)
    {
        return SComp<KsCrewVehicleGunnerSeatComponent>(seat).SelectActions
            .Single(action => SComp<KsCrewVehicleSelectWeaponActionComponent>(action).MountId == mountId);
    }

    /// <summary>
    ///     The player picks <paramref name="mountId"/>'s weapon through their client, with its action.
    /// </summary>
    private async Task SelectAsync(EntityUid seat, string mountId)
    {
        var actionNetEntity = NetEntity.Invalid;
        await Server.WaitPost(() => actionNetEntity = SEntMan.GetNetEntity(SelectAction(seat, mountId)));
        await Client.WaitPost(() =>
        {
            var actionUid = ToClient(actionNetEntity);
            CEntMan.System<ActionsSystem>().TriggerAction((actionUid, CEntMan.GetComponent<ActionComponent>(actionUid)));
        });
        await RunTicks(5);
    }

    private EntityUid? Selected(EntityUid gunner) => SComp<KsCrewVehicleGunnerComponent>(gunner).Selected;

    /// <summary>
    ///     Two gunners firing in the same tick each fire their own weapon, from its own muzzle. A gunner naming the other's
    ///         weapon fires nothing.
    /// </summary>
    [Test]
    public async Task TwoGunnersFireTogether()
    {
        var multi = await SpawnMultiAsync();
        await SeatPlayerAsync(multi, multi.SeatA);
        var (mobB, sessionB) = await AddPlayerInsideAsync(multi.Scene);
        await SitAsync(multi.SeatB, mobB);

        var leftMuzzle = Vector2.Zero;
        var rightMuzzle = Vector2.Zero;
        var leftTarget = Vector2.Zero;
        var rightTarget = Vector2.Zero;
        await Server.WaitPost(() =>
        {
            leftMuzzle = MuzzlePosition(multi.Left);
            rightMuzzle = MuzzlePosition(multi.Right);
            leftTarget = AheadOf(multi.Left);
            rightTarget = AheadOf(multi.Right);

            ReceiveFrom(ServerSession, ShotAt(multi.Left, OutsideAt(leftTarget)));
            ReceiveFrom(sessionB, ShotAt(multi.Right, OutsideAt(rightTarget)));
        });
        await RunTicks(2);

        await Server.WaitAssertion(() =>
        {
            var leftRounds = Rounds(multi.Left);
            var rightRounds = Rounds(multi.Right);
            Assert.That(leftRounds, Has.Count.EqualTo(1), "gunner A should fire the left turret");
            Assert.That(rightRounds, Has.Count.EqualTo(1), "gunner B should fire the right turret, the same tick");
            Assert.That(CameFrom(leftRounds[0].Owner, leftMuzzle, leftTarget), "A's round comes out of the left turret");
            Assert.That(CameFrom(rightRounds[0].Owner, rightMuzzle, rightTarget), "B's round comes out of the right turret");
        });

        // A names B's weapon.
        await Server.WaitPost(() => ReceiveFrom(ServerSession, ShotAt(multi.Right, OutsideAt(rightTarget))));
        await RunTicks(5);
        await Server.WaitAssertion(() =>
            Assert.That(Rounds(multi.Right), Has.Count.EqualTo(1), "gunner A shouldn't fire gunner B's weapon by naming it"));
    }

    /// <summary>
    ///     A seat with two weapons hands out one action each, and the action picks the weapon its next shot comes from; the
    ///         ammo alert follows the pick, and each action counts its own weapon's rounds. A seat with one weapon has no
    ///         actions. Standing up takes the alert away.
    /// </summary>
    [Test]
    public async Task SelectingAWeapon()
    {
        var multi = await SpawnMultiAsync();
        await SeatPlayerAsync(multi, multi.SeatB);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SComp<KsCrewVehicleGunnerSeatComponent>(multi.SeatA).SelectActions, Is.Empty, "one weapon, no actions");
            Assert.That(SComp<KsCrewVehicleGunnerSeatComponent>(multi.SeatB).SelectActions, Has.Count.EqualTo(2));
            Assert.That(Selected(SPlayer), Is.EqualTo(multi.Right), "the first mount is selected on sitting down");
            Assert.That(SEntMan.System<AlertsSystem>().IsShowingAlert(SPlayer, _ammoAlert), "a gunner is shown their rounds");
        });

        // One round off the right turret, so the two weapons' counts differ.
        var rightTarget = Vector2.Zero;
        await Server.WaitPost(() => rightTarget = AheadOf(multi.Right));
        await RequestShotAsync(multi.Right, OutsideAt(rightTarget));
        await AssertClientCountsAsync(multi);

        await SelectAsync(multi.SeatB, RearMount);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Selected(SPlayer), Is.EqualTo(multi.Rear), "the action selects its weapon");
            Assert.That(SComp<ActionComponent>(SelectAction(multi.SeatB, RearMount)).Toggled, Is.True);
            Assert.That(SComp<ActionComponent>(SelectAction(multi.SeatB, RightMount)).Toggled, Is.False);
        });
        await AssertClientCountsAsync(multi);

        var rearMuzzle = Vector2.Zero;
        var rearTarget = Vector2.Zero;
        await Server.WaitPost(() =>
        {
            rearMuzzle = MuzzlePosition(multi.Rear);
            rearTarget = AheadOf(multi.Rear);
        });
        await RequestShotAsync(multi.Rear, OutsideAt(rearTarget));
        await Server.WaitAssertion(() =>
        {
            var rearRounds = Rounds(multi.Rear);
            Assert.That(rearRounds, Has.Count.EqualTo(1), "the next shot comes from the selected weapon");
            Assert.That(CameFrom(rearRounds[0].Owner, rearMuzzle, rearTarget));
        });

        await StandAsync();
        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.System<AlertsSystem>().IsShowingAlert(SPlayer, _ammoAlert), Is.False, "standing up takes the alert away"));
    }

    /// <summary>
    ///     On the client: the alert counts the selected weapon's rounds, and each action its own weapon's.
    /// </summary>
    private async Task AssertClientCountsAsync(MultiScene multi)
    {
        var actionNetEntities = Array.Empty<(NetEntity Action, NetEntity Weapon)>();
        var selectedNetEntity = NetEntity.Invalid;
        await Server.WaitPost(() =>
        {
            selectedNetEntity = SEntMan.GetNetEntity(Selected(SPlayer)!.Value);
            actionNetEntities = new[] { (RightMount, multi.Right), (RearMount, multi.Rear) }
                .Select(pair => (SEntMan.GetNetEntity(SelectAction(multi.SeatB, pair.Item1)), SEntMan.GetNetEntity(pair.Item2)))
                .ToArray();
        });

        await Client.WaitAssertion(() =>
        {
            var alertPrototype = CEntMan.System<AlertsSystem>().TryGet(_ammoAlert, out var alert) ? alert : null;
            Assert.That(alertPrototype, Is.Not.Null);
            Assert.That(CEntMan.System<AlertsSystem>().IsShowingAlert(CPlayer, _ammoAlert), "the client shows the ammo alert");

            var alertEv = new GetGenericAlertCounterAmountEvent(alertPrototype!);
            CEntMan.EventBus.RaiseLocalEvent(CPlayer, ref alertEv);
            Assert.That(alertEv.Amount, Is.EqualTo(ClientRounds(ToClient(selectedNetEntity))), "the alert counts the selected weapon's rounds");

            foreach (var (actionNetEntity, weaponNetEntity) in actionNetEntities)
            {
                var actionEv = new GetGenericAlertCounterAmountEvent(alertPrototype!);
                CEntMan.EventBus.RaiseLocalEvent(ToClient(actionNetEntity), ref actionEv);
                Assert.That(actionEv.Amount, Is.EqualTo(ClientRounds(ToClient(weaponNetEntity))), "an action counts its own weapon's rounds");
                Assert.That(CEntMan.GetComponent<ActionComponent>(ToClient(actionNetEntity)).EntityIcon, Is.EqualTo(ToClient(actionNetEntity)),
                    "the action is its own icon, which the count is drawn on");
            }
        });

        await Server.WaitAssertion(() =>
            Assert.That(RoundsLeft(multi.Right), Is.Not.EqualTo(RoundsLeft(multi.Rear)), "precondition: the two weapons' counts differ"));
    }

    private int ClientRounds(EntityUid clientWeapon)
    {
        var ev = new GetAmmoCountEvent();
        CEntMan.EventBus.RaiseLocalEvent(clientWeapon, ref ev);
        return ev.Count;
    }

    /// <summary>
    ///     An aimed turret turns at its traverse rate and refuses to fire until it points at the target, then fires. An
    ///         aim for a turret the sender has not selected changes nothing.
    /// </summary>
    [Test]
    public async Task TurretTurnsThenFires()
    {
        var multi = await SpawnMultiAsync();
        await SeatPlayerAsync(multi, multi.SeatA);

        // Off to the side of where it points, well past its fire tolerance.
        var target = Vector2.Zero;
        var rate = 0.0;
        await Server.WaitPost(() =>
        {
            var (position, rotation) = Transform.GetWorldPositionRotation(multi.Left);
            target = position + (rotation + Angle.FromDegrees(90)).ToWorldVec() * 2f;
            rate = SComp<KsCrewVehicleTurretComponent>(multi.Left).TraverseRate.Theta;
        });

        // Aimed at the other gunner's turret: ignored.
        await AimAsync(multi.Right, OutsideAt(target));
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<KsCrewVehicleTurretAimComponent>(multi.Right), Is.False,
                "an aim for a turret the sender has not selected should be ignored");
            Assert.That(SComp<TransformComponent>(multi.Right).LocalRotation.Theta, Is.EqualTo(0).Within(0.0001));
        });

        await AimAsync(multi.Left, OutsideAt(target));
        var rounds = 0;
        var start = 0.0;
        await Server.WaitPost(() =>
        {
            Assert.That(SEntMan.HasComponent<KsCrewVehicleTurretAimComponent>(multi.Left), "the selected turret takes the aim");
            start = SComp<TransformComponent>(multi.Left).LocalRotation.Theta;
            rounds = RoundsLeft(multi.Left);
        });

        const int ticks = 3;
        await RunTicks(ticks);
        await Server.WaitAssertion(() =>
        {
            var turned = Math.Abs(SComp<TransformComponent>(multi.Left).LocalRotation.Theta - start);
            var expected = rate * ticks / TickRate;
            Assert.That(turned, Is.EqualTo(expected).Within(rate / TickRate * 1.01), "it turns at its traverse rate");
        });

        // Not there yet: refused.
        var targetCoordinates = OutsideAt(target);
        await Server.WaitPost(() => ReceiveFrom(ServerSession, ShotAt(multi.Left, targetCoordinates)));
        await RunTicks(1);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Rounds(multi.Left), Is.Empty, "a turret not yet pointing at its target shouldn't fire");
            Assert.That(RoundsLeft(multi.Left), Is.EqualTo(rounds));
        });

        var aligned = false;
        for (var tick = 0; tick < TickRate * 5 && !aligned; tick++)
        {
            await RunTicks(1);
            await Server.WaitPost(() => aligned = !SEntMan.HasComponent<KsCrewVehicleTurretTraversingComponent>(multi.Left));
        }

        Assert.That(aligned, "the turret should finish turning");

        // The client, which predicted the turn, ends where the server does, and its marker went with the server's.
        await RunTicks(5);
        var serverRotation = 0.0;
        var leftNetEntity = NetEntity.Invalid;
        await Server.WaitPost(() =>
        {
            serverRotation = SComp<TransformComponent>(multi.Left).LocalRotation.Theta;
            leftNetEntity = SEntMan.GetNetEntity(multi.Left);
        });
        await Client.WaitAssertion(() =>
        {
            var clientLeft = ToClient(leftNetEntity);
            Assert.That(CEntMan.HasComponent<KsCrewVehicleTurretTraversingComponent>(clientLeft), Is.False,
                "the client's turret should stop turning when the server's does");
            Assert.That(CEntMan.GetComponent<TransformComponent>(clientLeft).LocalRotation.Theta, Is.EqualTo(serverRotation).Within(0.001));
        });
        await Server.WaitPost(() => ReceiveFrom(ServerSession, ShotAt(multi.Left, targetCoordinates)));
        await RunTicks(1);
        await Server.WaitAssertion(() =>
            Assert.That(Rounds(multi.Left), Has.Count.EqualTo(1), "pointing at its target, it fires"));
    }

    /// <summary>
    ///     Sitting down through the client, which predicts it, hands out each of the seat's actions once.
    /// </summary>
    [Test]
    public async Task PredictedSittingGrantsEachActionOnce()
    {
        var multi = await SpawnMultiAsync();
        await PutInsideAsync(multi.Scene);

        var seatNetEntity = NetEntity.Invalid;
        var seatCoordinates = NetCoordinates.Invalid;
        await Server.WaitPost(() =>
        {
            seatNetEntity = SEntMan.GetNetEntity(multi.SeatB);
            seatCoordinates = SEntMan.GetNetCoordinates(SComp<TransformComponent>(multi.SeatB).Coordinates);
        });
        await PressKey(Robust.Shared.Input.EngineKeyFunctions.Use, coordinates: seatCoordinates, cursorEntity: seatNetEntity);
        await RunTicks(10);

        var expected = 0;
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<KsCrewVehicleGunnerComponent>(SPlayer), "precondition: the player sat down as gunner");
            expected = SComp<KsCrewVehicleGunnerSeatComponent>(multi.SeatB).SelectActions.Count;
            Assert.That(SelectActionCount(SEntMan, SPlayer), Is.EqualTo(expected), "the server gave each action once");
        });
        await Client.WaitAssertion(() =>
            Assert.That(SelectActionCount(CEntMan, CPlayer), Is.EqualTo(expected), "the client has each action once"));
    }

    private static int SelectActionCount(IEntityManager entityManager, EntityUid performer)
    {
        return entityManager.GetComponent<ActionsComponent>(performer).Actions
            .Count(entityManager.HasComponent<KsCrewVehicleSelectWeaponActionComponent>);
    }

    private async Task AimAsync(EntityUid weapon, NetCoordinates target)
    {
        var weaponNetEntity = SEntMan.GetNetEntity(weapon);
        await Client.WaitPost(() => CEntMan.RaisePredictiveEvent(new KsCrewVehicleAimEvent
        {
            Weapon = weaponNetEntity,
            Target = target,
        }));
        await RunTicks(5);
    }

    /// <summary>
    ///     The fixed gun fires only into its arc, and an aim outside it is kept at the arc's edge.
    /// </summary>
    [Test]
    public async Task FixedGunFiresOnlyIntoItsArc()
    {
        var multi = await SpawnMultiAsync();
        await SeatPlayerAsync(multi, multi.SeatB);
        await SelectAsync(multi.SeatB, RearMount);

        var outside = Vector2.Zero;
        var inside = Vector2.Zero;
        await Server.WaitPost(() =>
        {
            var (position, rotation) = Transform.GetWorldPositionRotation(multi.Rear);
            var halfArc = SComp<KsCrewVehicleWeaponComponent>(multi.Rear).FireArc!.Value.Theta / 2;
            outside = position + (rotation + new Angle(halfArc * 1.5)).ToWorldVec() * 2f;
            inside = position + (rotation + new Angle(halfArc * 0.5)).ToWorldVec() * 2f;

            var weapon = SEntity<KsCrewVehicleWeaponComponent>(multi.Rear);
            var turretSystem = SEntMan.System<KsCrewVehicleTurretSystem>();
            Assert.That(turretSystem.TryAim(weapon, Transform.ToCoordinates(MapData.Grid.Owner, new MapCoordinates(outside, MapId))));
            var rest = SEntMan.System<SharedKsCrewVehicleMountSystem>().GetRestRotation(weapon);
            var aimed = SComp<KsCrewVehicleTurretAimComponent>(multi.Rear).TargetAngle;
            Assert.That(Math.Abs(Angle.ShortestDistance(rest, aimed).Theta), Is.EqualTo(halfArc).Within(0.001),
                "an aim outside the arc is kept at its edge");
            SEntMan.RemoveComponent<KsCrewVehicleTurretAimComponent>(multi.Rear);
            SEntMan.RemoveComponent<KsCrewVehicleTurretTraversingComponent>(multi.Rear);
        });

        await RequestShotAsync(multi.Rear, OutsideAt(outside));
        await Server.WaitAssertion(() => Assert.That(Rounds(multi.Rear), Is.Empty, "outside its arc it shouldn't fire"));

        await RequestShotAsync(multi.Rear, OutsideAt(inside));
        await Server.WaitAssertion(() => Assert.That(Rounds(multi.Rear), Has.Count.EqualTo(1), "inside its arc it fires"));
    }

    public enum Hostile
    {
        NaN,
        Infinity,
        AnotherMap,
    }

    /// <summary>
    ///     An aim at a position that is not one, or at another map (the interior's), changes nothing.
    /// </summary>
    [TestCase(Hostile.NaN)]
    [TestCase(Hostile.Infinity)]
    [TestCase(Hostile.AnotherMap)]
    public async Task HostileAimChangesNothing(Hostile hostile)
    {
        var multi = await SpawnMultiAsync();
        await SeatPlayerAsync(multi, multi.SeatA);

        var target = NetCoordinates.Invalid;
        await Server.WaitPost(() =>
        {
            var gridNetEntity = SEntMan.GetNetEntity(MapData.Grid.Owner);
            target = hostile switch
            {
                Hostile.NaN => new NetCoordinates(gridNetEntity, new Vector2(float.NaN, float.NaN)),
                Hostile.Infinity => new NetCoordinates(gridNetEntity, new Vector2(float.PositiveInfinity, 0f)),
                _ => new NetCoordinates(SEntMan.GetNetEntity(multi.Scene.InteriorGrid), new Vector2(1.5f, 4.5f)),
            };
        });

        await AimAsync(multi.Left, target);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<KsCrewVehicleTurretAimComponent>(multi.Left), Is.False, $"{hostile}: no aim taken");
            Assert.That(SComp<TransformComponent>(multi.Left).LocalRotation.Theta, Is.EqualTo(0).Within(0.0001), $"{hostile}: not turned");
        });
    }

    /// <summary>
    ///     A driver drives while both gunners fire, all in the same ticks.
    /// </summary>
    [Test]
    public async Task DriverAndTwoGunnersActTogether()
    {
        var multi = await SpawnMultiAsync();
        await PutInsideAsync(multi.Scene);
        await SitAsync(multi.Scene.DriverSeat);
        var (mobA, sessionA) = await AddPlayerInsideAsync(multi.Scene);
        await SitAsync(multi.SeatA, mobA);
        var (mobB, sessionB) = await AddPlayerInsideAsync(multi.Scene);
        await SitAsync(multi.SeatB, mobB);

        var start = Vector2.Zero;
        var facingKey = DirectionFlag.South;
        await Server.WaitPost(() =>
        {
            start = HullPosition(multi.Scene.Hull);
            facingKey = DirectionFor(Content.Shared._KS14.CrewVehicle.KsCrewVehicleHullFrame.Forward(HullRotation(multi.Scene.Hull)));
        });

        await SetMovementKey(facingKey, Robust.Shared.Input.BoundKeyState.Down);
        await RunTicks(TickRate / 2);
        await Server.WaitPost(() =>
        {
            ReceiveFrom(sessionA, ShotAt(multi.Left, OutsideAt(AheadOf(multi.Left))));
            ReceiveFrom(sessionB, ShotAt(multi.Right, OutsideAt(AheadOf(multi.Right))));
        });
        await RunTicks(2);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Rounds(multi.Left), Has.Count.EqualTo(1), "gunner A fires while the hull is driven");
            Assert.That(Rounds(multi.Right), Has.Count.EqualTo(1), "gunner B fires while the hull is driven");
        });
        await RunTicks(TickRate / 2);
        await SetMovementKey(facingKey, Robust.Shared.Input.BoundKeyState.Up);

        await Server.WaitAssertion(() =>
            Assert.That((HullPosition(multi.Scene.Hull) - start).Length(), Is.GreaterThan(TopSpeed(multi.Scene.Hull) * 0.25f),
                "the driver drives the hull while they fire"));
    }
}
