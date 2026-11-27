using System.Diagnostics.CodeAnalysis;
using Content.Shared._KS14.Vehicle;
using Robust.Shared.Map;

namespace Content.Shared._KS14.CrewVehicle.Mounts;

/// <summary>
///     Finds a crew vehicle's mounted weapons and where their shots leave. Spawning them is the server's
///         <c>KsCrewVehicleMountSystem</c>.
/// </summary>
public abstract partial class SharedKsCrewVehicleMountSystem : EntitySystem
{
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] protected EntityQuery<KsCrewVehicleMountsComponent> MountsQuery = default!;

    public bool HasMount(EntityUid vehicleUid, string mountId)
    {
        return MountsQuery.TryComp(vehicleUid, out var mountsComponent) && mountsComponent.Mounts.ContainsKey(mountId);
    }

    /// <summary>
    ///     The weapon on <paramref name="vehicleUid"/>'s mount <paramref name="mountId"/>, if it is there.
    /// </summary>
    public bool TryGetWeapon(EntityUid vehicleUid, string mountId, [NotNullWhen(true)] out EntityUid? weaponUid)
    {
        weaponUid = null;
        if (!MountsQuery.TryComp(vehicleUid, out var mountsComponent) ||
            !mountsComponent.Weapons.TryGetValue(mountId, out var uid) ||
            TerminatingOrDeleted(uid))
            return false;

        weaponUid = uid;
        return true;
    }

    /// <summary>
    ///     Where <paramref name="weapon"/>'s shots leave, as it stands now, on the grid or map under it: not on the weapon
    ///         itself, or what a shot spawns (its round, its casing) would be the weapon's children.
    /// </summary>
    public EntityCoordinates GetMuzzleCoordinates(Entity<KsCrewVehicleWeaponComponent> weapon)
    {
        var transformComponent = Transform(weapon.Owner);
        var (position, rotation) = _transformSystem.GetWorldPositionRotation(transformComponent);
        var muzzle = new MapCoordinates(position + rotation.RotateVec(weapon.Comp.MuzzleOffset), transformComponent.MapID);
        return _transformSystem.ToCoordinates(muzzle);
    }

    /// <summary>
    ///     Which way <paramref name="weapon"/>'s mount points at rest, relative to what it is mounted on (the hull, or its
    ///         parent mount's weapon): the local rotation of a fixed weapon, and the centre of the weapon's fire arc.
    /// </summary>
    public Angle GetRestRotation(Entity<KsCrewVehicleWeaponComponent> weapon)
    {
        return weapon.Comp.Vehicle is { } vehicleUid &&
               weapon.Comp.MountId is { } mountId &&
               MountsQuery.TryComp(vehicleUid, out var mountsComponent) &&
               mountsComponent.Mounts.TryGetValue(mountId, out var mount)
            ? mount.Rotation
            : Angle.Zero;
    }

    /// <summary>
    ///     Whether a shot from <paramref name="weapon"/> toward <paramref name="worldDirection"/> is inside its fire arc.
    /// </summary>
    public bool IsInFireArc(Entity<KsCrewVehicleWeaponComponent> weapon, Angle worldDirection)
    {
        if (weapon.Comp.FireArc is not { } fireArc || weapon.Comp.Vehicle is not { } vehicleUid)
            return true;

        var restWorld = GetMountedOnWorldRotation(weapon.Owner, vehicleUid) + GetRestRotation(weapon);
        return Math.Abs(Angle.ShortestDistance(restWorld, worldDirection).Theta) <= fireArc.Theta / 2 + 0.0001;
    }

    /// <summary>
    ///     The world rotation of what <paramref name="weaponUid"/> is mounted on: its transform parent, the hull or another
    ///         mount's weapon, which its local rotation is measured from. The hull's, if it has no parent yet.
    /// </summary>
    public Angle GetMountedOnWorldRotation(EntityUid weaponUid, EntityUid vehicleUid)
    {
        var parentUid = Transform(weaponUid).ParentUid;
        return _transformSystem.GetWorldRotation(parentUid.IsValid() ? parentUid : vehicleUid);
    }

    /// <summary>
    ///     A mounted weapon fires from its hull, and its shots pass through it.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnGetShooterVehicle(Entity<KsCrewVehicleWeaponComponent> weapon, ref KsGetShooterVehicleEvent args)
    {
        args.Vehicle ??= weapon.Comp.Vehicle;
    }

    protected void SetWeapon(Entity<KsCrewVehicleMountsComponent> vehicle, string mountId, Entity<KsCrewVehicleWeaponComponent> weapon)
    {
        weapon.Comp.Vehicle = vehicle.Owner;
        weapon.Comp.MountId = mountId;
        Dirty(weapon);

        vehicle.Comp.Weapons[mountId] = weapon.Owner;
        Dirty(vehicle);
    }

    protected void ClearWeapon(Entity<KsCrewVehicleMountsComponent> vehicle, string mountId, EntityUid weaponUid)
    {
        if (!vehicle.Comp.Weapons.TryGetValue(mountId, out var mountedUid) || mountedUid != weaponUid)
            return;

        vehicle.Comp.Weapons.Remove(mountId);
        Dirty(vehicle);
    }
}
