#nullable enable
using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server._KS14.CrewVehicle.Interior;
using Content.Shared._KS14.CrewVehicle;
using Content.Shared._KS14.CrewVehicle.Drive;
using Content.Shared._KS14.CrewVehicle.Gunner;
using Content.Shared._KS14.CrewVehicle.Interior;
using Content.Shared._KS14.CrewVehicle.Mounts;
using Content.Shared.Interaction;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.CrewVehicle;

/// <summary>
///     The humvee as it ships, end to end through the player's own client: in by a door, into the driver's seat, a drive
///         down the road, and a round from the roof gun. Every number comes from its prototypes.
/// </summary>
[TestOf(typeof(KsCrewVehicleInteriorSystem))]
public sealed class KsCrewVehicleHumveeTest : KsCrewVehicleGunnerTestBase
{
    private static readonly EntProtoId Humvee = "KsCrewVehicleHumvee";

    [Test]
    public async Task EnterDriveAndFire()
    {
        EntityUid hull = default;
        await Server.WaitPost(() => hull = SEntMan.SpawnAtPosition(Humvee, OriginCoordinates.Offset(new Vector2(0f, 4f))));
        await RunTicks(5);

        // In by the first of its doors, as a player gets in: using the vehicle while standing at it.
        var enterTicks = 0;
        await Server.WaitPost(() =>
        {
            var vehicle = SEntity<KsCrewVehicleInteriorComponent>(hull);
            var doorway = vehicle.Comp.Doorways.Values.First();
            var standAt = SEntMan.System<KsCrewVehicleInteriorSystem>().GetDoorwayPosition(vehicle, doorway);
            Transform.SetMapCoordinates(SPlayer, standAt);
            SEntMan.System<SharedInteractionSystem>().InteractionActivate(SPlayer, hull,
                checkCanInteract: false, checkUseDelay: false, checkAccess: false, complexInteractions: true);
            enterTicks = (int)Math.Ceiling(vehicle.Comp.EnterDelay.TotalSeconds * TickRate) + 5;
        });
        await RunTicks(enterTicks);

        EntityUid driverSeat = default;
        await Server.WaitAssertion(() =>
        {
            var interiorMap = SComp<KsCrewVehicleInteriorComponent>(hull).InteriorMap;
            Assert.That(interiorMap, Is.Not.Null, "getting in loads the interior");
            Assert.That(SComp<TransformComponent>(SPlayer).MapUid, Is.EqualTo(interiorMap), "the player is inside");
            Assert.That(SComp<KsCrewVehicleOccupantComponent>(SPlayer).Vehicle, Is.EqualTo(hull));

            driverSeat = SEntMan.AllEntities<KsCrewVehicleDriverSeatComponent>()
                .Single(seat => SComp<TransformComponent>(seat).MapUid == interiorMap).Owner;
        });

        await SitAsync(driverSeat);
        await StartEngineAsync(hull);

        // Up is forward, whichever way it faces.
        var start = Vector2.Zero;
        await Server.WaitPost(() => start = HullPosition(hull));
        await HoldAsync(hull, DirectionFlag.North, TickRate);
        await SettleAsync(hull);

        await Server.WaitAssertion(() =>
        {
            var driven = HullPosition(hull) - start;
            Assert.That(Vector2.Dot(driven, KsCrewVehicleHullFrame.Forward(HullRotation(hull))), Is.GreaterThan(TopSpeed(hull) * 0.25f),
                "a second at the wheel should drive it a good way forward");
        });

        // The roof gun, at a point ahead, where its turret already points.
        await SetCombatMode(true);
        EntityUid weapon = default;
        var muzzle = Vector2.Zero;
        var aim = Vector2.Zero;
        var rounds = 0;
        var range = 0f;
        await Server.WaitPost(() =>
        {
            var mountId = SComp<KsCrewVehicleGunnerSeatComponent>(driverSeat).Mounts[0];
            weapon = Weapon(hull, mountId);
            muzzle = MuzzlePosition(weapon);
            aim = KsCrewVehicleHullFrame.Forward(HullRotation(hull));
            rounds = RoundsLeft(weapon);
            range = SComp<KsCrewVehicleWeaponComponent>(weapon).MaxAimRange;
            Assert.That(SComp<KsCrewVehicleGunnerComponent>(SPlayer).Selected, Is.EqualTo(weapon),
                "precondition: the driver's seat gives them the gun");
        });

        await RequestShotAsync(weapon, OutsideAt(muzzle + aim * range * 0.5f));

        await Server.WaitAssertion(() =>
        {
            var fired = Rounds(weapon);
            Assert.That(fired, Is.Not.Empty, "the driver's shot should fire a round");
            Assert.That(RoundsLeft(weapon), Is.LessThan(rounds), "a round is used");

            var round = fired[0].Owner;
            Assert.That(SComp<TransformComponent>(round).MapUid, Is.EqualTo(SComp<TransformComponent>(hull).MapUid),
                "the round flies outside");
            Assert.That(Vector2.Dot(WorldPosition(round) - muzzle, aim), Is.GreaterThan(0f),
                "the round has left the muzzle, the way it was aimed");
        });
    }
}
