using Content.Shared._KS14.CrewVehicle.Mounts;
using Robust.Shared.Map;

namespace Content.Server._KS14.CrewVehicle.Mounts;

/// <summary>
///     Spawns a crew vehicle's mounted weapons when the hull is map-initialised, as children of the hull at their mounts,
///         or of their parent mount's weapon (<see cref="KsCrewVehicleMount.Parent"/>).
/// </summary>
/// <remarks>
///     Server only: the client initialises networked entities too, and would make copies of its own.
/// </remarks>
public sealed partial class KsCrewVehicleMountSystem : SharedKsCrewVehicleMountSystem
{
    [SubscribeLocalEvent]
    private void OnMapInit(Entity<KsCrewVehicleMountsComponent> vehicle, ref MapInitEvent args)
    {
        // Mounts on the hull first, then each mount once its parent's weapon is there: a few mounts at most, so a pass
        //      over what is left until a pass spawns nothing.
        var pending = new List<string>(vehicle.Comp.Mounts.Keys);
        var progress = true;
        while (pending.Count > 0 && progress)
        {
            progress = false;
            for (var i = pending.Count - 1; i >= 0; i--)
            {
                var mountId = pending[i];
                var mount = vehicle.Comp.Mounts[mountId];
                EntityUid attachTo;
                if (mount.Parent is not { } parentId)
                {
                    attachTo = vehicle.Owner;
                }
                else if (TryGetWeapon(vehicle.Owner, parentId, out var parentWeaponUid))
                {
                    attachTo = parentWeaponUid.Value;
                }
                else
                {
                    continue;
                }

                pending.RemoveAt(i);
                progress = true;
                SpawnMount(vehicle, mountId, mount, attachTo);
            }
        }

        // What is left has a parent that is missing, never spawned, or in a cycle.
        foreach (var mountId in pending)
        {
            Log.Error($"Mount {mountId} of {ToPrettyString(vehicle)} is on mount '{vehicle.Comp.Mounts[mountId].Parent}', which it does not have, has no weapon, or is mounted on it in turn: nothing spawned");
        }
    }

    private void SpawnMount(Entity<KsCrewVehicleMountsComponent> vehicle, string mountId, KsCrewVehicleMount mount, EntityUid attachTo)
    {
        if (TryGetWeapon(vehicle.Owner, mountId, out _))
            return;

        var weaponUid = SpawnAttachedTo(mount.Prototype, new EntityCoordinates(attachTo, mount.Offset), rotation: mount.Rotation);
        if (!TryComp<KsCrewVehicleWeaponComponent>(weaponUid, out var weaponComponent))
        {
            Log.Error($"Mount {mountId} of {ToPrettyString(vehicle)} spawned {ToPrettyString(weaponUid)}, which is not a crew vehicle weapon");
            Del(weaponUid);
            return;
        }

        SetWeapon(vehicle, mountId, (weaponUid, weaponComponent));
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
