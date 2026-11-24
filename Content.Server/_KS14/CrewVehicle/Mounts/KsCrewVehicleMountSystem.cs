using Content.Shared._KS14.CrewVehicle.Mounts;
using Robust.Shared.Map;

namespace Content.Server._KS14.CrewVehicle.Mounts;

/// <summary>
///     Spawns a crew vehicle's mounted weapons when the hull is map-initialised, as children of the hull at their mounts.
/// </summary>
/// <remarks>
///     Server only: the client initialises networked entities too, and would make copies of its own.
/// </remarks>
public sealed partial class KsCrewVehicleMountSystem : SharedKsCrewVehicleMountSystem
{
    [SubscribeLocalEvent]
    private void OnMapInit(Entity<KsCrewVehicleMountsComponent> vehicle, ref MapInitEvent args)
    {
        foreach (var (mountId, mount) in vehicle.Comp.Mounts)
        {
            if (TryGetWeapon(vehicle.Owner, mountId, out _))
                continue;

            var weaponUid = SpawnAttachedTo(mount.Prototype, new EntityCoordinates(vehicle.Owner, mount.Offset), rotation: mount.Rotation);
            if (!TryComp<KsCrewVehicleWeaponComponent>(weaponUid, out var weaponComponent))
            {
                Log.Error($"Mount {mountId} of {ToPrettyString(vehicle)} spawned {ToPrettyString(weaponUid)}, which is not a crew vehicle weapon");
                Del(weaponUid);
                continue;
            }

            SetWeapon(vehicle, mountId, (weaponUid, weaponComponent));
        }
    }

    /// <summary>
    ///     A weapon deleted on its own leaves its mount empty, so the hull never names a deleted entity.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnWeaponShutdown(Entity<KsCrewVehicleWeaponComponent> weapon, ref ComponentShutdown args)
    {
        if (weapon.Comp.Vehicle is not { } vehicleUid ||
            weapon.Comp.MountId is not { } mountId ||
            TerminatingOrDeleted(vehicleUid) ||
            !MountsQuery.TryComp(vehicleUid, out var mountsComponent))
            return;

        ClearWeapon((vehicleUid, mountsComponent), mountId, weapon.Owner);
    }
}
