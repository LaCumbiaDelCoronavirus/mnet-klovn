using Content.Server._KS14.CrewVehicle.Interior;
using Content.Shared._KS14.CrewVehicle.Entry;
using Robust.Shared.Map;

namespace Content.Server._KS14.CrewVehicle.Entry;

/// <summary>
///     The server's part of getting out: moving the player, and whoever they pull, out beside the hull.
/// </summary>
public sealed partial class KsCrewVehicleExitSystem : SharedKsCrewVehicleExitSystem
{
    [Dependency] private KsCrewVehicleInteriorSystem _interiorSystem = default!;

    protected override bool CompleteExit(EntityUid user, MapCoordinates landing)
    {
        // Untracked first: moving out would otherwise remove it later in the tick, from the map change.
        _interiorSystem.UntrackOccupant(user);
        var pulled = _interiorSystem.MoveWithPulled(user, landing);
        if (pulled is { } pulledUid)
            _interiorSystem.UntrackOccupant(pulledUid);

        return true;
    }
}
