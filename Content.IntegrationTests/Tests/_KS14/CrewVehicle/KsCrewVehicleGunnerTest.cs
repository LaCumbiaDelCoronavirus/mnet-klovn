#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server._KS14.KillCredit;
using Content.Shared._KS14.CrewVehicle.Drive;
using Content.Shared._KS14.CrewVehicle.Gunner;
using Content.Shared._KS14.CrewVehicle.Mounts;
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
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.CrewVehicle;

/// <summary>
///     Firing a crew vehicle's mounted weapon from a gunner seat inside, with the player's own shoot request through their
///         client. Every number comes from the test prototypes.
/// </summary>
[TestOf(typeof(KsCrewVehicleGunnerSystem))]
public sealed class KsCrewVehicleGunnerTest : KsCrewVehicleGunnerTestBase
{
    public enum Seat
    {
        Gunner,
        GunnerOutOfCombatMode,
        Passenger,
    }

    /// <summary>
    ///     The seated gunner's shot leaves the weapon's muzzle on the outside map toward where they aimed, fired as the
    ///         weapon. Not out of combat mode, and not from a seat that is not a gunner seat.
    /// </summary>
    [TestCase(Seat.Gunner)]
    [TestCase(Seat.GunnerOutOfCombatMode)]
    [TestCase(Seat.Passenger)]
    public async Task GunnerFiresFromTheMuzzle(Seat seat)
    {
        var (scene, weapon) = await SeatGunnerAsync(passenger: seat == Seat.Passenger);
        if (seat == Seat.GunnerOutOfCombatMode)
            await SetCombatMode(false);

        var muzzle = Vector2.Zero;
        var target = Vector2.Zero;
        var rounds = 0;
        var range = 0f;
        await Server.WaitPost(() =>
        {
            muzzle = MuzzlePosition(weapon);
            range = SComp<KsCrewVehicleWeaponComponent>(weapon).MaxAimRange;
            target = muzzle + new Vector2(range * 0.5f, -range * 0.5f);
            rounds = RoundsLeft(weapon);
        });

        await RequestShotAsync(weapon, OutsideAt(target));

        await Server.WaitAssertion(() =>
        {
            var fired = Rounds(weapon);
            if (seat != Seat.Gunner)
            {
                Assert.That(fired, Is.Empty, $"{seat}: nothing should be fired");
                Assert.That(RoundsLeft(weapon), Is.EqualTo(rounds), $"{seat}: no round should be used");
                return;
            }

            Assert.That(fired, Has.Count.EqualTo(1), "the gunner's request should fire one round");
            var round = fired[0];
            Assert.That(round.Comp.Shooter, Is.EqualTo(weapon), "the weapon fires its own rounds");
            Assert.That(SComp<TransformComponent>(round.Owner).MapUid, Is.EqualTo(SComp<TransformComponent>(scene.Hull).MapUid),
                "the round flies on the outside map");
            Assert.That(RoundsLeft(weapon), Is.EqualTo(rounds - 1));

            // Still on the line from the muzzle to the target, past the muzzle: it left from there.
            var aim = Vector2.Normalize(target - muzzle);
            var flown = WorldPosition(round.Owner) - muzzle;
            Assert.That(Vector2.Dot(flown, aim), Is.GreaterThan(0f), "the round flies from the muzzle toward the target");
            Assert.That(MathF.Abs(aim.X * flown.Y - aim.Y * flown.X), Is.LessThan(0.05f), "on the line from the muzzle");
            var velocity = SComp<Robust.Shared.Physics.Components.PhysicsComponent>(round.Owner).LinearVelocity;
            Assert.That(Vector2.Dot(Vector2.Normalize(velocity), aim), Is.GreaterThan(0.999f));
        });
    }

    public enum Shot
    {
        Round,
        Beam,
    }

    /// <summary>
    ///     A shot aimed back across the hull leaves it untouched and hits a second hull in the way.
    /// </summary>
    [TestCase(Shot.Round)]
    [TestCase(Shot.Beam)]
    public async Task ShotsPassThroughTheirOwnHull(Shot shot)
    {
        var (scene, weapon) = await SeatGunnerAsync(shot == Shot.Beam ? _beamHull : _gunnerHull);

        // The muzzle is in front (south, at rotation 0); behind the hull, another.
        var otherHull = EntityUid.Invalid;
        var target = Vector2.Zero;
        await Server.WaitPost(() =>
        {
            var hullPosition = WorldPosition(scene.Hull);
            otherHull = SEntMan.SpawnAtPosition(HullPrototype, OriginCoordinates.Offset(new Vector2(0f, 3f)));
            target = hullPosition + new Vector2(0f, 4f);
        });
        await RunTicks(5);

        await RequestShotAsync(weapon, OutsideAt(target));

        await Server.WaitAssertion(() =>
        {
            Assert.That(Damaged(scene.Hull), Is.False,
                "a weapon's shot shouldn't hit the hull it is mounted on");
            Assert.That(Damaged(otherHull), Is.True,
                "the shot should go on through to the hull in the way");
        });
    }

    public enum Hostile
    {
        NaN,
        Infinity,
        AnotherMap,
    }

    /// <summary>
    ///     A shot request aimed at a position that is not one, or at another map (the interior's), fires nothing.
    /// </summary>
    [TestCase(Hostile.NaN)]
    [TestCase(Hostile.Infinity)]
    [TestCase(Hostile.AnotherMap)]
    public async Task HostileCoordinatesFireNothing(Hostile hostile)
    {
        var (scene, weapon) = await SeatGunnerAsync();
        var rounds = 0;
        var target = NetCoordinates.Invalid;
        await Server.WaitPost(() =>
        {
            rounds = RoundsLeft(weapon);
            var gridNetEntity = SEntMan.GetNetEntity(MapData.Grid.Owner);
            target = hostile switch
            {
                Hostile.NaN => new NetCoordinates(gridNetEntity, new Vector2(float.NaN, float.NaN)),
                Hostile.Infinity => new NetCoordinates(gridNetEntity, new Vector2(float.PositiveInfinity, 0f)),
                _ => new NetCoordinates(SEntMan.GetNetEntity(scene.InteriorGrid), new Vector2(1.5f, 1.5f)),
            };
        });

        await RequestShotAsync(weapon, target);

        await Server.WaitAssertion(() =>
        {
            Assert.That(Rounds(weapon), Is.Empty, $"{hostile}: nothing should be fired");
            Assert.That(RoundsLeft(weapon), Is.EqualTo(rounds), $"{hostile}: no round should be used");
        });
    }

    /// <summary>
    ///     A point beyond the weapon's reach is aimed at along the same bearing, at its reach, and the shot fires; one
    ///         within reach is aimed at as it is.
    /// </summary>
    [TestCase(4f)]
    [TestCase(0.5f)]
    public async Task AimBeyondReachIsBroughtIn(float rangeMultiple)
    {
        var (_, weapon) = await SeatGunnerAsync();
        var muzzle = Vector2.Zero;
        var target = Vector2.Zero;
        var range = 0f;
        await Server.WaitPost(() =>
        {
            muzzle = MuzzlePosition(weapon);
            range = SComp<KsCrewVehicleWeaponComponent>(weapon).MaxAimRange;
            target = muzzle + Vector2.Normalize(new Vector2(1f, -2f)) * range * rangeMultiple;
            SEntMan.AddComponent<KsCrewVehicleGunnerTestShotRecorderComponent>(weapon);
        });

        await RequestShotAsync(weapon, OutsideAt(target));

        await Server.WaitAssertion(() =>
        {
            Assert.That(Rounds(weapon), Has.Count.EqualTo(1), "the shot should fire");
            var aimedAts = SComp<KsCrewVehicleGunnerTestShotRecorderComponent>(weapon).AimedAt;
            Assert.That(aimedAts, Has.Count.EqualTo(1));
            var aimedAt = aimedAts[0];
            var expected = muzzle + (target - muzzle) * MathF.Min(1f, 1f / rangeMultiple);
            Assert.That((aimedAt - expected).Length(), Is.LessThan(0.01f),
                $"aimed at {aimedAt}, expected {expected}: no further than {range} from the muzzle, on the same bearing");
        });
    }

    public enum GunnerState
    {
        Healthy,
        Critical,
        Pacified,
    }

    /// <summary>
    ///     A gunner who could not shoot a gun in hand does not fire the mounted one either.
    /// </summary>
    [TestCase(GunnerState.Healthy)]
    [TestCase(GunnerState.Critical)]
    [TestCase(GunnerState.Pacified)]
    public async Task IncapacitatedGunnerCannotFire(GunnerState gunner)
    {
        var (_, weapon) = await SeatGunnerAsync();
        var target = Vector2.Zero;
        await Server.WaitPost(() =>
        {
            target = MuzzlePosition(weapon) + new Vector2(0f, -2f);
            switch (gunner)
            {
                case GunnerState.Critical:
                    SEntMan.System<MobStateSystem>().ChangeMobState(SPlayer, MobState.Critical);
                    break;
                case GunnerState.Pacified:
                    SEntMan.EnsureComponent<PacifiedComponent>(SPlayer);
                    break;
            }
        });
        await RunTicks(5);

        await RequestShotAsync(weapon, OutsideAt(target));

        await Server.WaitAssertion(() =>
        {
            if (gunner == GunnerState.Healthy)
                Assert.That(Rounds(weapon), Has.Count.EqualTo(1), "a healthy gunner fires");
            else
                Assert.That(Rounds(weapon), Is.Empty, $"a {gunner} gunner shouldn't fire");
        });
    }

    /// <summary>
    ///     The gunner is paid for what the weapon's shot does, while the weapon is what did it: during the shot, and for a
    ///         round that lands after they have stood up. A weapon nobody has fired pays nobody.
    /// </summary>
    [TestCase(Shot.Round)]
    [TestCase(Shot.Beam)]
    public async Task KillCreditGoesToTheGunner(Shot shot)
    {
        var (scene, weapon) = await SeatGunnerAsync(shot == Shot.Beam ? _beamHull : _gunnerHull);

        var target = EntityUid.Invalid;
        var idleHull = EntityUid.Invalid;
        await Server.WaitPost(() =>
        {
            target = SEntMan.SpawnAtPosition(HullPrototype, OriginCoordinates.Offset(new Vector2(0f, -4f)));
            SEntMan.AddComponent<KsCrewVehicleGunnerTestHitRecorderComponent>(target);
            idleHull = SEntMan.SpawnAtPosition(_gunnerHull, OriginCoordinates.Offset(new Vector2(-5f, 5f)));
        });
        await RunTicks(5);

        var targetPosition = Vector2.Zero;
        await Server.WaitPost(() => targetPosition = WorldPosition(target));
        await RequestShotAsync(weapon, OutsideAt(targetPosition));

        var gunnerUserId = ServerSession.UserId;
        await Server.WaitAssertion(() =>
        {
            var recorder = SComp<KsCrewVehicleGunnerTestHitRecorderComponent>(target);
            Assert.That(recorder.Hits, Is.Not.Empty, "precondition: the shot hit the target");
            var (origin, recipient) = recorder.Hits[0];
            Assert.That(origin, Is.EqualTo(weapon), "the weapon is what dealt the damage");
            Assert.That(recipient, Is.EqualTo(gunnerUserId), "and its gunner is paid for it");
        });

        await StandAsync();
        await Server.WaitAssertion(() =>
        {
            var killCreditSystem = SEntMan.System<KsKillCreditSystem>();
            Assert.That(killCreditSystem.GetHitRecipient(weapon), Is.EqualTo(gunnerUserId),
                "a round landing after its gunner stood up still pays them");
            Assert.That(killCreditSystem.GetHitRecipient(Weapon(idleHull)), Is.Null, "a weapon nobody fired pays nobody");
        });
    }

    /// <summary>
    ///     An interior is refused for a gunner seat on a mount the hull lacks, one that is not a view seat, and a mount two
    ///         seats name.
    /// </summary>
    [Test]
    public async Task BadGunnerSeatsAreRefused()
    {
        List<string> errors = default!;
        await Server.WaitPost(() => errors = RefusedInterior(_badGunnerHull));
        await Server.WaitAssertion(() =>
        {
            Assert.That(errors, Has.Some.Contains("'absent'"), "a seat naming a mount the hull lacks");
            Assert.That(errors, Has.Some.Contains("not also a view seat"), "a gunner seat that is not a view seat");
            Assert.That(errors, Has.Some.Contains("already"), "a mount named by two seats");
            Assert.That(errors, Has.Some.Contains("ammo port").And.Contains("'missing'"), "an ammo port on a mount the hull lacks");
        });
    }

    /// <summary>
    ///     A hull's weapons are spawned when it is map-initialised, on their mounts, and go with it; one that goes on its
    ///         own leaves its mount empty.
    /// </summary>
    [Test]
    public async Task WeaponsAreMountedAtMapInit()
    {
        await Server.WaitAssertion(() =>
        {
            var mountSystem = SEntMan.System<SharedKsCrewVehicleMountSystem>();
            var uninitialisedMap = SEntMan.System<SharedMapSystem>().CreateMap(out _, runMapInit: false);
            var waitingHull = SEntMan.SpawnAtPosition(_gunnerHull, new EntityCoordinates(uninitialisedMap, Vector2.Zero));
            Assert.That(mountSystem.TryGetWeapon(waitingHull, MountId, out _), Is.False, "nothing is mounted before map init");
            SEntMan.System<SharedMapSystem>().InitializeMap(uninitialisedMap);
            Assert.That(mountSystem.TryGetWeapon(waitingHull, MountId, out var waitingWeapon), Is.True, "mounted at map init");

            var mount = SComp<KsCrewVehicleMountsComponent>(waitingHull).Mounts[MountId];
            var weaponTransform = SComp<TransformComponent>(waitingWeapon!.Value);
            Assert.That(weaponTransform.ParentUid, Is.EqualTo(waitingHull), "a child of the hull");
            Assert.That((weaponTransform.LocalPosition - mount.Offset).Length(), Is.LessThan(0.001f), "at its mount");
            Assert.That(SComp<KsCrewVehicleWeaponComponent>(waitingWeapon.Value).Vehicle, Is.EqualTo(waitingHull));

            SEntMan.DeleteEntity(waitingWeapon.Value);
            Assert.That(SComp<KsCrewVehicleMountsComponent>(waitingHull).Weapons, Does.Not.ContainKey(MountId),
                "a weapon deleted on its own leaves its mount empty");

            var hull = SEntMan.SpawnAtPosition(_gunnerHull, OriginCoordinates.Offset(new Vector2(5f, 5f)));
            var weapon = Weapon(hull);
            SEntMan.DeleteEntity(hull);
            Assert.That(SEntMan.Deleted(weapon), Is.True, "the weapon goes with its hull");

            SEntMan.DeleteEntity(uninitialisedMap);
        });
    }

    /// <summary>
    ///     Every crew hull is on the layer lasers hit, so a beam fired at one from outside stops at it (while a weapon
    ///         mounted on it still fires through it: <see cref="ShotsPassThroughTheirOwnHull"/>).
    /// </summary>
    [Test]
    public async Task HullsAreOnTheLaserLayer()
    {
        await Server.WaitAssertion(() =>
        {
            var factory = SEntMan.ComponentFactory;
            var hulls = 0;
            foreach (var prototype in ProtoMan.EnumeratePrototypes<EntityPrototype>())
            {
                if (prototype.Abstract ||
                    !prototype.TryComp<KsCrewVehicleDriveComponent>(out _, factory) ||
                    !prototype.TryComp<FixturesComponent>(out var fixturesComponent, factory))
                    continue;

                hulls++;
                Assert.That(fixturesComponent.Fixtures.Values.Any(fixture => fixture.Hard && (fixture.CollisionLayer & (int)Content.Shared.Physics.CollisionGroup.Opaque) != 0),
                    $"{prototype.ID} has no hard fixture on the Opaque layer, so lasers would pass through it");
            }

            Assert.That(hulls, Is.GreaterThan(0), "precondition: there are hulls to check");
        });
    }
}
