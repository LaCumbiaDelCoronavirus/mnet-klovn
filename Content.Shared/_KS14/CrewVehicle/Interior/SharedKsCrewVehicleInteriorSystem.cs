using System.Diagnostics.CodeAnalysis;
using Robust.Shared.Map;

namespace Content.Shared._KS14.CrewVehicle.Interior;

/// <summary>
///     Finds the vehicle a crew vehicle interior belongs to. Loading interiors is the server's
///         <c>KsCrewVehicleInteriorSystem</c>.
/// </summary>
public abstract partial class SharedKsCrewVehicleInteriorSystem : EntitySystem
{
    [Dependency] private EntityQuery<KsCrewVehicleInteriorComponent> _interiorQuery = default!;
    [Dependency] private EntityQuery<KsCrewVehicleInteriorMapComponent> _interiorMapQuery = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

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
    ///     The doorway nearest <paramref name="position"/> among those it is within reach of.
    /// </summary>
    public bool TryFindDoorway(Entity<KsCrewVehicleInteriorComponent> vehicle,
        MapCoordinates position,
        [NotNullWhen(true)] out string? doorway)
    {
        doorway = null;
        var (hullPosition, hullRotation) = _transformSystem.GetWorldPositionRotation(vehicle.Owner);
        if (position.MapId != Transform(vehicle.Owner).MapID)
            return false;

        var local = KsCrewVehicleHullFrame.WorldToHull(hullPosition, hullRotation, position.Position);
        var nearest = float.MaxValue;
        foreach (var (id, candidate) in vehicle.Comp.Doorways)
        {
            var distance = (local - candidate.Offset).Length();
            if (distance > candidate.Radius || distance >= nearest)
                continue;

            nearest = distance;
            doorway = id;
        }

        return doorway != null;
    }

    /// <summary>
    ///     The vehicle's doorway called <paramref name="doorwayId"/>.
    /// </summary>
    public bool TryGetDoorway(Entity<KsCrewVehicleInteriorComponent> vehicle,
        string doorwayId,
        [NotNullWhen(true)] out KsCrewVehicleDoorway? doorway)
    {
        return vehicle.Comp.Doorways.TryGetValue(doorwayId, out doorway);
    }

    /// <summary>
    ///     Where <paramref name="doorway"/> is in the world, as the hull stands now: where someone leaving by it lands.
    /// </summary>
    public MapCoordinates GetDoorwayPosition(Entity<KsCrewVehicleInteriorComponent> vehicle, KsCrewVehicleDoorway doorway)
    {
        var (hullPosition, hullRotation) = _transformSystem.GetWorldPositionRotation(vehicle.Owner);
        return new MapCoordinates(KsCrewVehicleHullFrame.HullToWorld(hullPosition, hullRotation, doorway.Offset),
            Transform(vehicle.Owner).MapID);
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
