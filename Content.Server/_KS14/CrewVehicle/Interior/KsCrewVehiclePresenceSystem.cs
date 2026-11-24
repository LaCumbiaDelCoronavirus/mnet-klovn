using Content.Shared._KS14.CrewVehicle.Interior;

namespace Content.Server._KS14.CrewVehicle.Interior;

/// <summary>
///     Being on a crew vehicle's interior grid is what makes someone an occupant, however they got there: by a door,
///         teleported, spawned there. Whatever ends up on the interior map but off its grid is put out beside the
///         vehicle.
/// </summary>
/// <remarks>
///     Reacts to parent changes, which every arrival makes (a spawn raises one at startup), never to a scan. The
///         check runs from <see cref="Update"/>: the change is reported from inside the transform system, mid-move.
/// </remarks>
public sealed partial class KsCrewVehiclePresenceSystem : EntitySystem
{
    [Dependency] private KsCrewVehicleEjectionSystem _ejectionSystem = default!;
    [Dependency] private KsCrewVehicleInteriorSystem _interiorSystem = default!;
    [Dependency] private EntityQuery<KsCrewVehicleInteriorMapComponent> _interiorMapQuery = default!;
    [Dependency] private EntityQuery<KsCrewVehicleOccupantComponent> _occupantQuery = default!;

    /// <summary>
    ///     Entities whose parent changed on an interior map since the last update.
    /// </summary>
    private readonly HashSet<EntityUid> _queued = new();

    /// <summary>
    ///     What <see cref="Update"/> works through, so that what it does can queue more for the next one.
    /// </summary>
    private readonly List<EntityUid> _draining = new();

    [SubscribeLocalEvent]
    private void OnParentChanged(ref EntParentChangedMessage args)
    {
        // Almost every parent change in the game is elsewhere: this is the whole cost for them.
        if (!_interiorMapQuery.HasComp(args.Transform.MapUid))
            return;

        _queued.Add(args.Entity);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_queued.Count == 0)
            return;

        _draining.Clear();
        _draining.AddRange(_queued);
        _queued.Clear();

        foreach (var uid in _draining)
        {
            if (TerminatingOrDeleted(uid))
                continue;

            var transform = Transform(uid);
            if (!_interiorSystem.TryGetInteriorMap(transform.MapUid, out var interiorMap))
                continue;

            // The interior grid is the only place to be on an interior map: off it, out you go.
            if (transform.GridUid == null)
            {
                if (_interiorSystem.CanBeOccupant(uid) || _ejectionSystem.IsLoose(uid))
                    _ejectionSystem.EjectBesideVehicle(uid, interiorMap.Value.Comp.Vehicle);

                continue;
            }

            if (!_occupantQuery.HasComp(uid) && _interiorSystem.CanBeOccupant(uid))
                _interiorSystem.TrackOccupant(uid, interiorMap.Value.Comp.Vehicle);
        }

        _draining.Clear();
    }
}
