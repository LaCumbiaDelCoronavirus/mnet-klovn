using Content.Shared._KS14.Construction;
using Content.Shared.Construction.Components;
using Content.Shared.Tiles;
using Robust.Shared.Map.Components;

namespace Content.Shared._KS14.CrewVehicle.Interior;

/// <summary>
///     Nothing is built inside a crew vehicle: no anchoring, no floor tiles, no construction ghosts. RCDs are refused
///         by the server marking each interior map blocked when it loads (<c>KsNoBuildZoneSystem.SetBlocked</c>).
/// </summary>
/// <remarks>
///     Flatpacks and deployable structures spawn anchored entities without any of these events, and are not refused.
/// </remarks>
public sealed partial class KsCrewVehicleNoBuildSystem : EntitySystem
{
    [Dependency] private SharedKsCrewVehicleInteriorSystem _interiorSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    /// <summary>
    ///     What a player trying to build inside a crew vehicle is told.
    /// </summary>
    public static readonly LocId NoBuildMessage = "ks-crew-vehicle-interior-no-build";

    // AnchorAttemptEvent is a by-value class event, which only the (uid, component, args) handler form accepts (RA0054).
    [SubscribeLocalEvent]
    private void OnAnchorAttempt(EntityUid uid, AnchorableComponent component, AnchorAttemptEvent args)
    {
        if (args.Cancelled || !_interiorSystem.IsInInterior(uid))
            return;

        // A locale key, not text: AnchorableSystem.TryAnchor turns it into a KsPopupMessage.
        args.FailMessage = NoBuildMessage;
        args.Cancel();
    }

    [SubscribeLocalEvent]
    private void OnFloorTileAttempt(Entity<MapGridComponent> grid, ref FloorTileAttemptEvent args)
    {
        if (_interiorSystem.IsInInterior(grid.Owner))
            args.Cancelled = true;
    }

    [SubscribeLocalEvent]
    private void OnCanStartConstruction(ref KsCanStartConstructionEvent args)
    {
        if (args.Cancelled || !_interiorSystem.TryGetInteriorMap(_transformSystem.GetMap(args.Location), out _))
            return;

        args.Cancelled = true;
        args.Message = NoBuildMessage;
    }
}
