using System.Numerics;
using Content.Shared._KS14.CrewVehicle;
using Content.Shared._KS14.CrewVehicle.Interior;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Client._KS14.CrewVehicle.Interior;

/// <summary>
///     The client only looks interiors up; it never loads them.
/// </summary>
public sealed partial class KsCrewVehicleInteriorSystem : SharedKsCrewVehicleInteriorSystem
{
    [Dependency] private EntityQuery<KsCrewVehicleInteriorMapComponent> _interiorMapQuery = default!;
    [Dependency] private EntityQuery<MapGridComponent> _gridQuery = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    /// <summary>
    ///     Whether an eye on <paramref name="eyeMapId"/> is inside a crew vehicle this client knows, whose outside is
    ///         drawn behind its interior, and where that vehicle is and how its interior lies over it: what the backdrop
    ///         eye is worked out from (<see cref="KsCrewVehicleInteriorTransform.GetBackdropEye"/>).
    /// </summary>
    /// <param name="anchor">The anchor on the interior map, not on its grid.</param>
    /// <param name="interiorRotation">
    ///     How far the interior's grid is turned on its map, plus the cabin on its grid
    ///         (<see cref="KsCrewVehicleInteriorTransform.GetInteriorRotation"/>).
    /// </param>
    public bool TryGetBackdropFrame(
        MapId eyeMapId,
        out Entity<KsCrewVehicleInteriorMapComponent> interiorMap,
        out EntityUid backdropMapUid,
        out MapId backdropMapId,
        out Vector2 anchor,
        out Angle interiorRotation,
        out Vector2 hullPosition,
        out Angle hullRotation)
    {
        interiorMap = default;
        backdropMapUid = EntityUid.Invalid;
        backdropMapId = MapId.Nullspace;
        anchor = default;
        interiorRotation = default;
        hullPosition = default;
        hullRotation = default;

        if (!_mapSystem.TryGetMap(eyeMapId, out var interiorMapUid) ||
            !_interiorMapQuery.TryComp(interiorMapUid, out var interiorMapComponent) ||
            !DrawsOutsideBackdrop((interiorMapUid.Value, interiorMapComponent)))
            return false;

        // The vehicle is only drawn from while it is really here: in PVS (a vehicle out of it is detached, in
        //      nullspace) and on a map other than its own interior's.
        var vehicleUid = interiorMapComponent.Vehicle;
        if (!vehicleUid.IsValid() ||
            !EntityManager.TransformQuery.TryComp(vehicleUid, out var vehicleTransform) ||
            !EntityManager.MetaQuery.TryComp(vehicleUid, out var vehicleMetaData) ||
            (vehicleMetaData.Flags & MetaDataFlags.Detached) != 0 ||
            vehicleTransform.MapID == MapId.Nullspace ||
            vehicleTransform.MapID == eyeMapId ||
            vehicleTransform.MapUid is not { } vehicleMapUid)
            return false;

        (hullPosition, hullRotation) = _transformSystem.GetWorldPositionRotation(vehicleTransform);
        interiorMap = (interiorMapUid.Value, interiorMapComponent);
        backdropMapUid = vehicleMapUid;
        backdropMapId = vehicleTransform.MapID;

        // The anchor is in the interior grid's coordinates. An interior is loaded unrotated, usually at the map's
        //      origin, but nothing makes it so; with no grid at all, its coordinates are taken as the map's. The cabin
        //      may face any way on its grid, which turns it further.
        anchor = interiorMapComponent.Anchor;
        interiorRotation = interiorMapComponent.CabinRotation;
        var mapChildren = Transform(interiorMapUid.Value).ChildEnumerator;
        while (mapChildren.MoveNext(out var childUid))
        {
            if (!_gridQuery.HasComp(childUid))
                continue;

            var (gridPosition, gridRotation) = _transformSystem.GetWorldPositionRotation(childUid);
            anchor = gridPosition + gridRotation.RotateVec(interiorMapComponent.Anchor);
            interiorRotation = KsCrewVehicleInteriorTransform.GetInteriorRotation(gridRotation, interiorMapComponent.CabinRotation);
            break;
        }

        return true;
    }
}
