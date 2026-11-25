using System.Numerics;
using Content.Shared.Maps;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Shared._KS14.CrewVehicle.Floor;

/// <summary>
///     What counts as floor for a crew vehicle: a tile that isn't open to space, on whichever grid is there. Kept apart
///         from <see cref="KsCrewVehicleFloorSystem"/>, which holds hulls to it, so that the interior (where people
///         are put down beside a hull) can ask without depending on the drive.
/// </summary>
public sealed partial class KsCrewVehicleFloorTileSystem : EntitySystem
{
    [Dependency] private ITileDefinitionManager _tileDefinitionManager = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private EntityQuery<MapGridComponent> _gridQuery = default!;

    /// <summary>
    ///     Whether there is a floor tile at <paramref name="worldPoint"/>: on <paramref name="hintGridUid"/>, the grid the
    ///         asker is on, else on whichever grid is there.
    /// </summary>
    public bool IsFloor(EntityUid mapUid, EntityUid? hintGridUid, Vector2 worldPoint)
    {
        if (_gridQuery.TryComp(hintGridUid, out var hintGridComponent) &&
            IsFloorTile((hintGridUid.Value, hintGridComponent), worldPoint))
        {
            return true;
        }

        return _mapSystem.TryFindGridAt(mapUid, worldPoint, out var gridUid, out var gridComponent) &&
               gridUid != hintGridUid &&
               IsFloorTile((gridUid, gridComponent), worldPoint);
    }

    /// <summary>
    ///     Whether there is a floor tile at <paramref name="position"/>.
    /// </summary>
    public bool IsFloor(MapCoordinates position)
    {
        return _mapSystem.GetMapOrInvalid(position.MapId) is { Valid: true } mapUid &&
               IsFloor(mapUid, null, position.Position);
    }

    private bool IsFloorTile(Entity<MapGridComponent> grid, Vector2 worldPoint)
    {
        var indices = _mapSystem.WorldToTile(grid.Owner, grid.Comp, worldPoint);
        if (!_mapSystem.TryGetTileRef(grid.Owner, grid.Comp, indices, out var tileRef) || tileRef.Tile.IsEmpty)
            return false;

        // Lattice is open to space: a walkway, not a road.
        return _tileDefinitionManager[tileRef.Tile.TypeId] is ContentTileDefinition { MapAtmosphere: false };
    }
}
