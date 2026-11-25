using Content.Server._KS14.KillCredit;
using Content.Server.Administration.Logs;
using Content.Shared._KS14.CrewVehicle.Gunner;
using Content.Shared._KS14.CrewVehicle.Mounts;
using Content.Shared._Trauma.Projectiles;
using Content.Shared.Database;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Timing;

namespace Content.Server._KS14.CrewVehicle.Gunner;

/// <summary>
///     What the server keeps of a mounted weapon's shots: the admin log naming the gunner, and the kill credit. A mounted
///         weapon is the shooter of its own rounds, so it is what their damage names; this pays the gunner whose shot it
///         was for it.
/// </summary>
/// <remarks>
///     A beam lands inside its shot, so its gunner is the shot's. A round is stamped with its gunner as it is fired
///         (<see cref="KsCrewVehicleRoundComponent"/>), and its hit tells this which round is landing, just before the
///         damage is dealt with the weapon as its origin.
/// </remarks>
public sealed partial class KsCrewVehicleShotRecordSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLogManager = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private KsKillCreditSystem _killCreditSystem = default!;
    [Dependency] private SharedGunSystem _gunSystem = default!;
    [Dependency] private EntityQuery<KsCrewVehicleWeaponComponent> _weaponQuery = default!;
    [Dependency] private EntityQuery<ProjectileComponent> _projectileQuery = default!;

    /// <summary>
    ///     The round landing now, between its hit and the damage it deals this same tick: its weapon and its gunner.
    /// </summary>
    private (EntityUid Weapon, EntityUid Gunner, GameTick Tick)? _landingRound;

    /// <summary>
    ///     The gunner whose shot it is: during a shot (a beam's hit, a round being fired), the shot's; for a round
    ///         landing later, the round's, even if they have stood up since; failing both, whoever has the weapon
    ///         selected. A gunner who is gone is paid nothing.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnGetCreditRecipient(Entity<KsCrewVehicleWeaponComponent> weapon, ref KsGetCreditRecipientEvent args)
    {
        if (args.Recipient != null)
            return;

        if (GetShotGunner(weapon.Owner) is { } shotGunnerUid)
        {
            args.Recipient = _killCreditSystem.GetRecipient(shotGunnerUid);
            return;
        }

        if (_landingRound is { } round && round.Weapon == weapon.Owner && round.Tick == _gameTiming.CurTick)
        {
            args.Recipient = TerminatingOrDeleted(round.Gunner) ? null : _killCreditSystem.GetRecipient(round.Gunner);
            return;
        }

        if (GetSelectingGunner(weapon) is { } selectingUid)
            args.Recipient = _killCreditSystem.GetRecipient(selectingUid);
    }

    /// <summary>
    ///     A round fired for a gunner remembers them.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnPlayerShotProjectile(ref PlayerShotProjectileEvent args)
    {
        if (!_weaponQuery.HasComp(args.User) || GetShotGunner(args.User) is not { } gunnerUid)
            return;

        EnsureComp<KsCrewVehicleRoundComponent>(args.Projectile).Gunner = gunnerUid;
    }

    /// <summary>
    ///     A round about to deal its damage, with its weapon as the origin.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnRoundHit(Entity<KsCrewVehicleRoundComponent> round, ref ProjectileHitEvent args)
    {
        if (!_projectileQuery.TryComp(round.Owner, out var projectileComponent) || projectileComponent.Weapon is not { } weaponUid)
            return;

        _landingRound = (weaponUid, round.Comp.Gunner, _gameTiming.CurTick);
    }

    [SubscribeLocalEvent]
    private void OnGunShot(Entity<KsCrewVehicleWeaponComponent> weapon, ref GunShotEvent args)
    {
        if (GetShotGunner(weapon.Owner) is not { } gunnerUid)
            return;

        _adminLogManager.Add(LogType.AttackArmedClick,
            LogImpact.Low,
            $"{ToPrettyString(gunnerUid):user} fired {ToPrettyString(weapon.Owner):weapon} mounted on {ToPrettyString(weapon.Comp.Vehicle):vehicle} toward {args.ToCoordinates}");
    }

    /// <summary>
    ///     The player whose shot the weapon is firing right now, if it is firing one for a player.
    /// </summary>
    private EntityUid? GetShotGunner(EntityUid weaponUid)
    {
        return _gunSystem.KsGetPredictingUser(weaponUid) is { } gunnerUid && gunnerUid != weaponUid ? gunnerUid : null;
    }

    /// <summary>
    ///     Whoever has <paramref name="weapon"/> selected now. Gunners are few, and this is asked only for damage the
    ///         weapon deals outside a shot or a round's hit.
    /// </summary>
    private EntityUid? GetSelectingGunner(Entity<KsCrewVehicleWeaponComponent> weapon)
    {
        var query = EntityQueryEnumerator<KsCrewVehicleGunnerComponent>();
        while (query.MoveNext(out var gunnerUid, out var gunnerComponent))
        {
            if (gunnerComponent.Selected == weapon.Owner)
                return gunnerUid;
        }

        return null;
    }
}
