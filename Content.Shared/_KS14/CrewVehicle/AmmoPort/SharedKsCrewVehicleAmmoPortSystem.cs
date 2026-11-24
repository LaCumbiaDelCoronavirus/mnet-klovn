using System.Diagnostics.CodeAnalysis;
using Content.Shared._KS14.CrewVehicle.Interior;
using Content.Shared._KS14.CrewVehicle.Mounts;
using Content.Shared.Examine;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;

namespace Content.Shared._KS14.CrewVehicle.AmmoPort;

/// <summary>
///     What an ammo port shows and is checked for. Loading through it is the server's
///         <c>KsCrewVehicleAmmoPortSystem</c>.
/// </summary>
public abstract partial class SharedKsCrewVehicleAmmoPortSystem : EntitySystem
{
    [Dependency] private SharedKsCrewVehicleInteriorSystem _interiorSystem = default!;
    [Dependency] protected SharedKsCrewVehicleMountSystem MountSystem = default!;
    [Dependency] private EntityQuery<BallisticAmmoProviderComponent> _ballisticQuery = default!;

    /// <summary>
    ///     The weapon <paramref name="port"/> loads, if its vehicle has it mounted.
    /// </summary>
    public bool TryGetWeapon(Entity<KsCrewVehicleAmmoPortComponent> port, [NotNullWhen(true)] out EntityUid? weaponUid)
    {
        weaponUid = null;
        return _interiorSystem.TryGetVehicle(port.Owner, out var vehicle) &&
               MountSystem.TryGetWeapon(vehicle.Value.Owner, port.Comp.Mount, out weaponUid);
    }

    /// <remarks>
    ///     A by-value class event: the engine has <c>Entity&lt;T&gt;</c> handlers only for ref events. Formatted here, in
    ///         the examiner's culture: the server answers an examine request inside its culture scope.
    /// </remarks>
    [SubscribeLocalEvent]
    private void OnExamined(EntityUid uid, KsCrewVehicleAmmoPortComponent component, ExaminedEvent args)
    {
        if (!TryGetWeapon((uid, component), out var weaponUid))
            return;

        var ev = new GetAmmoCountEvent();
        RaiseLocalEvent(weaponUid.Value, ref ev);
        args.PushMarkup(Loc.GetString("ks-crew-vehicle-ammo-port-examine", ("count", ev.Count), ("capacity", ev.Capacity)));
    }

    [SubscribeLocalEvent]
    private void OnGetInteriorErrors(Entity<KsCrewVehicleAmmoPortComponent> port, ref KsGetCrewVehicleInteriorErrorsEvent args)
    {
        var name = ToPrettyString(port);
        var vehicleUid = args.Vehicle.Owner;
        if (!MountSystem.HasMount(vehicleUid, port.Comp.Mount))
        {
            args.Validation.AddError($"ammo port {name} loads mount '{port.Comp.Mount}', which {ToPrettyString(vehicleUid)} does not have");
            return;
        }

        if (MountSystem.TryGetWeapon(vehicleUid, port.Comp.Mount, out var weaponUid) && !_ballisticQuery.HasComp(weaponUid))
            args.Validation.AddError($"ammo port {name} loads mount '{port.Comp.Mount}', whose {ToPrettyString(weaponUid)} takes no rounds");
    }
}
