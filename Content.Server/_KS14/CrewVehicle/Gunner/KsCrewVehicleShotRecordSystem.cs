using Content.Server._KS14.KillCredit;
using Content.Server.Administration.Logs;
using Content.Shared._KS14.CrewVehicle.Mounts;
using Content.Shared.Database;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;

namespace Content.Server._KS14.CrewVehicle.Gunner;

/// <summary>
///     What the server keeps of a mounted weapon's shots: the admin log naming the gunner, and the kill credit. A mounted
///         weapon is the shooter of its own rounds, so it is what their damage names; this pays its gunner for it.
/// </summary>
public sealed partial class KsCrewVehicleShotRecordSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLogManager = default!;
    [Dependency] private KsKillCreditSystem _killCreditSystem = default!;
    [Dependency] private SharedGunSystem _gunSystem = default!;
    [Dependency] private EntityQuery<KsCrewVehicleWeaponCreditComponent> _creditQuery = default!;

    /// <summary>
    ///     During a shot (a beam's hit, a round being fired) the gunner whose shot it is; for a round landing later, whoever
    ///         fired the weapon last; failing both, whoever the hull itself answers to.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnGetCreditRecipient(Entity<KsCrewVehicleWeaponComponent> weapon, ref KsGetCreditRecipientEvent args)
    {
        if (args.Recipient != null)
            return;

        if (GetGunner(weapon.Owner) is { } gunnerUid)
        {
            args.Recipient = _killCreditSystem.GetRecipient(gunnerUid);
            return;
        }

        if (_creditQuery.TryComp(weapon.Owner, out var creditComponent) && creditComponent.LastShotRecipient is { } recipient)
        {
            args.Recipient = recipient;
            return;
        }

        // The hull answers through what is on it; nothing on it passes the question back to its weapons.
        if (weapon.Comp.Vehicle is { } vehicleUid)
            args.Recipient = _killCreditSystem.GetRecipient(vehicleUid);
    }

    [SubscribeLocalEvent]
    private void OnGunShot(Entity<KsCrewVehicleWeaponComponent> weapon, ref GunShotEvent args)
    {
        if (GetGunner(weapon.Owner) is not { } gunnerUid)
            return;

        EnsureComp<KsCrewVehicleWeaponCreditComponent>(weapon.Owner).LastShotRecipient = _killCreditSystem.GetRecipient(gunnerUid);

        _adminLogManager.Add(LogType.AttackArmedClick,
            LogImpact.Low,
            $"{ToPrettyString(gunnerUid):user} fired {ToPrettyString(weapon.Owner):weapon} mounted on {ToPrettyString(weapon.Comp.Vehicle):vehicle} toward {args.ToCoordinates}");
    }

    /// <summary>
    ///     The player whose shot the weapon is firing right now, if it is firing one for a player.
    /// </summary>
    private EntityUid? GetGunner(EntityUid weaponUid)
    {
        return _gunSystem.KsGetPredictingUser(weaponUid) is { } gunnerUid && gunnerUid != weaponUid ? gunnerUid : null;
    }
}
