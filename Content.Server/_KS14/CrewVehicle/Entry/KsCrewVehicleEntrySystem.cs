using Content.Server._KS14.CrewVehicle.Interior;
using Content.Shared._KS14.CrewVehicle.Entry;
using Content.Shared._KS14.CrewVehicle.Interior;
using Content.Shared._KS14.PopupLocalization;
using Content.Shared.Popups;

namespace Content.Server._KS14.CrewVehicle.Entry;

/// <summary>
///     The server's part of getting in: loading the interior and moving the player, and whoever they pull, onto it.
/// </summary>
public sealed partial class KsCrewVehicleEntrySystem : SharedKsCrewVehicleEntrySystem
{
    [Dependency] private KsCrewVehicleInteriorSystem _interiorSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    private static readonly LocId FailedMessage = "ks-crew-vehicle-enter-failed";

    protected override bool CompleteEnter(Entity<KsCrewVehicleInteriorComponent> vehicle, EntityUid user, string doorwayId)
    {
        if (!_interiorSystem.EnsureInterior(vehicle, out var interiorMap)
            || !_interiorSystem.TryGetExitDoor(interiorMap.Value, doorwayId, out var exitDoor))
        {
            _popupSystem.PopupEntity(KsPopupMessage.Create(FailedMessage), vehicle, user);
            return false;
        }

        // Tracked at once rather than when KsCrewVehiclePresenceSystem next looks, so that someone else getting in
        //      this same tick counts them against the capacity.
        var pulled = _interiorSystem.MoveWithPulled(user, _transformSystem.GetMapCoordinates(exitDoor.Value));
        _interiorSystem.TrackOccupant(user, vehicle);
        if (pulled is { } pulledUid && IsLiving(pulledUid))
            _interiorSystem.TrackOccupant(pulledUid, vehicle);

        return true;
    }
}
