using System.Diagnostics.CodeAnalysis;

namespace Content.Shared._KS14.CrewVehicle.Interior;

/// <summary>
///     Finds the vehicle a crew vehicle interior belongs to. Loading interiors is the server's
///         <c>KsCrewVehicleInteriorSystem</c>.
/// </summary>
public abstract partial class SharedKsCrewVehicleInteriorSystem : EntitySystem
{
    [Dependency] private EntityQuery<KsCrewVehicleInteriorComponent> _interiorQuery = default!;
    [Dependency] private EntityQuery<KsCrewVehicleInteriorMapComponent> _interiorMapQuery = default!;

    /// <summary>
    ///     The interior link of <paramref name="mapUid"/>, if it is a crew vehicle's interior map.
    /// </summary>
    public bool TryGetInteriorMap(EntityUid? mapUid, [NotNullWhen(true)] out Entity<KsCrewVehicleInteriorMapComponent>? interiorMap)
    {
        interiorMap = null;
        if (!_interiorMapQuery.TryComp(mapUid, out var interiorMapComponent))
            return false;

        interiorMap = (mapUid.Value, interiorMapComponent);
        return true;
    }

    /// <summary>
    ///     Whether <paramref name="uid"/> is anywhere on a crew vehicle's interior map.
    /// </summary>
    public bool IsInInterior(EntityUid uid)
    {
        return _interiorMapQuery.HasComp(Transform(uid).MapUid);
    }

    /// <summary>
    ///     The vehicle whose interior map <paramref name="insideUid"/> is on.
    /// </summary>
    public bool TryGetVehicle(EntityUid insideUid, [NotNullWhen(true)] out Entity<KsCrewVehicleInteriorComponent>? vehicle)
    {
        vehicle = null;
        if (!TryGetInteriorMap(Transform(insideUid).MapUid, out var interiorMap) ||
            !_interiorQuery.TryComp(interiorMap.Value.Comp.Vehicle, out var interiorComponent))
            return false;

        vehicle = (interiorMap.Value.Comp.Vehicle, interiorComponent);
        return true;
    }

    /// <summary>
    ///     Links a freshly loaded map to the vehicle it is the inside of, on both ends.
    /// </summary>
    protected void LinkInterior(Entity<KsCrewVehicleInteriorComponent> vehicle, EntityUid mapUid)
    {
        var interiorMapComponent = EnsureComp<KsCrewVehicleInteriorMapComponent>(mapUid);
        interiorMapComponent.Vehicle = vehicle.Owner;
        interiorMapComponent.Anchor = vehicle.Comp.InteriorAnchor;
        Dirty(mapUid, interiorMapComponent);

        vehicle.Comp.InteriorMap = mapUid;
    }
}
