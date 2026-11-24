using System.Diagnostics.CodeAnalysis;
using Content.Shared._KS14.CrewVehicle.Interior;
using Content.Shared._KS14.RCD;
using Content.Shared.Atmos.Components;
using Content.Shared.Construction.EntitySystems;
using Content.Shared.Ghost;
using Content.Shared.Physics;
using Content.Shared.Light.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Robust.Shared.Map;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;

namespace Content.Server._KS14.CrewVehicle.Interior;

/// <summary>
///     Loads each crew vehicle's interior map the first time it is needed, refusing one that does not fit its vehicle,
///         and keeps track of what is inside.
/// </summary>
public sealed partial class KsCrewVehicleInteriorSystem : SharedKsCrewVehicleInteriorSystem
{
    [Dependency] private MapLoaderSystem _mapLoaderSystem = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private KsNoBuildZoneSystem _noBuildZoneSystem = default!;
    [Dependency] private AnchorableSystem _anchorableSystem = default!;
    [Dependency] private MetaDataSystem _metaDataSystem = default!;
    [Dependency] private MobStateSystem _mobStateSystem = default!;
    [Dependency] private PullingSystem _pullingSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private EntityQuery<KsCrewVehicleExitComponent> _exitQuery = default!;
    [Dependency] private EntityQuery<GhostComponent> _ghostQuery = default!;
    [Dependency] private EntityQuery<MobStateComponent> _mobStateQuery = default!;
    [Dependency] private EntityQuery<KsCrewVehicleInteriorComponent> _interiorQuery = default!;
    [Dependency] private EntityQuery<KsCrewVehicleOccupantComponent> _occupantQuery = default!;

    private const string DoorwayClaim = "doorway";

    /// <summary>
    ///     Loaded paused and uninitialised, so that a refused interior never runs anything: it is initialised only
    ///         once it has been checked and linked.
    /// </summary>
    private static readonly DeserializationOptions LoadOptions = DeserializationOptions.Default with
    {
        InitializeMaps = false,
        PauseMaps = true,
    };

    /// <summary>
    ///     The vehicle's interior map, loading it the first time.
    /// </summary>
    /// <param name="errors">If given, filled with why a loaded interior was refused.</param>
    /// <returns>False, having logged an error, if the map could not be loaded or does not fit the vehicle.</returns>
    public bool EnsureInterior(Entity<KsCrewVehicleInteriorComponent> vehicle,
        [NotNullWhen(true)] out EntityUid? interiorMap,
        List<string>? errors = null)
    {
        interiorMap = vehicle.Comp.InteriorMap;
        if (interiorMap is { } existingMap && !TerminatingOrDeleted(existingMap))
            return true;

        interiorMap = null;
        var path = vehicle.Comp.InteriorPath;
        if (!_mapLoaderSystem.TryLoadMap(path, out var loadedMap, out var grids, LoadOptions))
        {
            Log.Error($"Failed to load interior '{path}' of crew vehicle {ToPrettyString(vehicle)}");
            errors?.Add($"map '{path}' failed to load");
            return false;
        }

        var mapUid = loadedMap.Value.Owner;
        var validation = new KsCrewVehicleInteriorValidation();
        Validate(vehicle, mapUid, grids.Count, validation);
        if (validation.Errors.Count > 0)
        {
            errors?.AddRange(validation.Errors);
            Log.Error($"Interior '{path}' of crew vehicle {ToPrettyString(vehicle)} is invalid: {string.Join("; ", validation.Errors)}");
            QueueDel(mapUid);
            return false;
        }

        foreach (var grid in grids)
        {
            // ShuttleSystem roofs every grid that is not a map, which would shut out the interior map's ambient light.
            RemComp<ImplicitRoofComponent>(grid);
            // AutomaticAtmosSystem gives any grid of a few tiles its own atmosphere, which starts as vacuum. The air
            //      inside is the interior map's fixed, breathable one.
            RemComp<GridAtmosphereComponent>(grid);
        }

        LinkInterior(vehicle, mapUid);
        _noBuildZoneSystem.SetBlocked(mapUid, KsCrewVehicleNoBuildSystem.NoBuildMessage);
        _mapSystem.InitializeMap(mapUid, unpause: true);

        // Parent changes during the load came before the link, so nobody placed in the map file was seen arriving.
        foreach (var grid in grids)
        {
            TrackPresentOccupants(grid, vehicle);
        }

        interiorMap = mapUid;
        return true;
    }

    /// <summary>
    ///     Unlinks the vehicle from its interior map, which it is about to lose, and returns the map.
    /// </summary>
    public EntityUid? DetachInterior(Entity<KsCrewVehicleInteriorComponent> vehicle)
    {
        var interiorMap = vehicle.Comp.InteriorMap;
        vehicle.Comp.InteriorMap = null;
        return interiorMap;
    }

    /// <summary>
    ///     Whether a mob cannot stand at <paramref name="position"/>: an anchored structure in its way, such as a wall.
    ///         Unanchored things, the hull and whatever is mounted on it among them, do not count.
    /// </summary>
    public bool IsBlockedForMobs(MapCoordinates position)
    {
        if (!_mapSystem.TryFindGridAt(position, out var gridUid, out var gridComponent))
            return false;

        var indices = _mapSystem.TileIndicesFor(gridUid, gridComponent, position);
        return !_anchorableSystem.TileFree((gridUid, gridComponent), indices, (int)CollisionGroup.MobLayer, (int)CollisionGroup.MobMask);
    }

    /// <summary>
    ///     Whether <paramref name="uid"/> is the kind of thing that is an occupant while on an interior grid.
    /// </summary>
    public bool CanBeOccupant(EntityUid uid)
    {
        return _mobStateQuery.HasComp(uid) || _ghostQuery.HasComp(uid);
    }

    private void TrackPresentOccupants(EntityUid parentUid, EntityUid vehicleUid)
    {
        var children = Transform(parentUid).ChildEnumerator;
        while (children.MoveNext(out var childUid))
        {
            if (CanBeOccupant(childUid))
                TrackOccupant(childUid, vehicleUid);
            else
                TrackPresentOccupants(childUid, vehicleUid);
        }
    }

    /// <summary>
    ///     The exit door inside <paramref name="interiorMap"/> leading out of <paramref name="doorway"/>.
    /// </summary>
    public bool TryGetExitDoor(EntityUid interiorMap, string doorway, [NotNullWhen(true)] out EntityUid? exitDoor)
    {
        exitDoor = null;
        var query = EntityQueryEnumerator<KsCrewVehicleExitComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var exitComponent, out var transform))
        {
            if (transform.MapUid != interiorMap || exitComponent.Doorway != doorway)
                continue;

            exitDoor = uid;
            return true;
        }

        return false;
    }

    /// <summary>
    ///     How many of the vehicle's occupants are alive, which is what its capacity counts.
    /// </summary>
    public int CountLivingOccupants(EntityUid vehicleUid)
    {
        var count = 0;
        var query = EntityQueryEnumerator<KsCrewVehicleOccupantComponent, MobStateComponent>();
        while (query.MoveNext(out var uid, out var occupantComponent, out var mobStateComponent))
        {
            if (occupantComponent.Vehicle == vehicleUid
                && occupantComponent.LifeStage <= ComponentLifeStage.Running
                && !_mobStateSystem.IsDead(uid, mobStateComponent))
                count++;
        }

        return count;
    }

    /// <summary>
    ///     Starts tracking <paramref name="uid"/>, which must already be on the vehicle's interior map, as inside it.
    /// </summary>
    public void TrackOccupant(EntityUid uid, EntityUid vehicleUid)
    {
        if (_occupantQuery.TryComp(uid, out var existing))
        {
            if (existing.Vehicle == vehicleUid)
                return;

            RemComp(uid, existing);
        }

        var occupantComponent = AddComp<KsCrewVehicleOccupantComponent>(uid);
        occupantComponent.Vehicle = vehicleUid;
        // For MapUidChangedEvent, which tells when it has left the interior some other way.
        _metaDataSystem.AddFlag(uid, MetaDataFlags.ExtraTransformEvents);

        var ev = new KsCrewVehicleEnteredEvent(uid);
        RaiseLocalEvent(vehicleUid, ref ev);
    }

    /// <summary>
    ///     Stops tracking <paramref name="uid"/> as inside a vehicle.
    /// </summary>
    public void UntrackOccupant(EntityUid uid)
    {
        RemComp<KsCrewVehicleOccupantComponent>(uid);
    }

    // ComponentRemove, not ComponentShutdown: the occupant's startup and shutdown belong to KsCrewVehicleOutsidePvsSystem.
    [SubscribeLocalEvent]
    private void OnOccupantRemove(Entity<KsCrewVehicleOccupantComponent> entity, ref ComponentRemove args)
    {
        _metaDataSystem.RemoveFlag(entity.Owner, MetaDataFlags.ExtraTransformEvents);

        if (Deleted(entity.Comp.Vehicle))
            return;

        var ev = new KsCrewVehicleExitedEvent(entity.Owner);
        RaiseLocalEvent(entity.Comp.Vehicle, ref ev);
    }

    [SubscribeLocalEvent]
    private void OnOccupantMetaFlagRemoveAttempt(Entity<KsCrewVehicleOccupantComponent> entity, ref MetaFlagRemoveAttemptEvent args)
    {
        if (entity.Comp.LifeStage <= ComponentLifeStage.Running)
            args.ToRemove &= ~MetaDataFlags.ExtraTransformEvents;
    }

    /// <summary>
    ///     An occupant moved off the interior by anything but the doors (an admin teleport, a ghost warp) is no longer
    ///         inside.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnOccupantMapUidChanged(Entity<KsCrewVehicleOccupantComponent> entity, ref MapUidChangedEvent args)
    {
        if (_interiorQuery.TryComp(entity.Comp.Vehicle, out var interiorComponent)
            && interiorComponent.InteriorMap is { } interiorMap
            && args.NewMap == interiorMap)
            return;

        // Deferred: the move that raised this is still being processed.
        RemCompDeferred<KsCrewVehicleOccupantComponent>(entity.Owner);
    }

    /// <summary>
    ///     Moves <paramref name="uid"/> to <paramref name="destination"/>, bringing along whatever it is pulling, still
    ///         pulled. Whoever was pulling <paramref name="uid"/> lets go.
    /// </summary>
    /// <returns>The entity it brought along, if any.</returns>
    public EntityUid? MoveWithPulled(EntityUid uid, EntityCoordinates destination)
    {
        if (TryComp<PullableComponent>(uid, out var pullableComponent) && pullableComponent.BeingPulled)
            _pullingSystem.TryStopPull(uid, pullableComponent);

        EntityUid? pulledUid = null;
        if (TryComp<PullerComponent>(uid, out var pullerComponent)
            && pullerComponent.Pulling is { } pulling
            && TryComp<PullableComponent>(pulling, out var pulledPullableComponent))
        {
            _pullingSystem.TryStopPull(pulling, pulledPullableComponent);
            pulledUid = pulling;
        }

        _transformSystem.SetCoordinates(uid, destination);
        if (pulledUid is not { } pulled)
            return null;

        _transformSystem.SetCoordinates(pulled, destination);
        _pullingSystem.TryStartPull(uid, pulled);
        return pulled;
    }

    private void Validate(Entity<KsCrewVehicleInteriorComponent> vehicle,
        EntityUid mapUid,
        int gridCount,
        KsCrewVehicleInteriorValidation validation)
    {
        if (gridCount != 1)
            validation.AddError($"it has {gridCount} grids, not exactly one");

        var ev = new KsGetCrewVehicleInteriorErrorsEvent(vehicle, mapUid, validation);
        ValidateChildren(mapUid, vehicle, validation, ref ev);

        foreach (var doorway in vehicle.Comp.Doorways.Keys)
        {
            if (!validation.IsClaimed(DoorwayClaim, doorway))
                validation.AddError($"no exit door names doorway '{doorway}'");
        }

        RaiseLocalEvent(vehicle.Owner, ref ev);
    }

    /// <summary>
    ///     Checks everything under <paramref name="parentUid"/>: grids, what stands on them, and what that holds.
    /// </summary>
    private void ValidateChildren(EntityUid parentUid,
        Entity<KsCrewVehicleInteriorComponent> vehicle,
        KsCrewVehicleInteriorValidation validation,
        ref KsGetCrewVehicleInteriorErrorsEvent ev)
    {
        var children = Transform(parentUid).ChildEnumerator;
        while (children.MoveNext(out var childUid))
        {
            if (_exitQuery.TryComp(childUid, out var exitComponent))
                ValidateExit((childUid, exitComponent), vehicle, validation);

            RaiseLocalEvent(childUid, ref ev);
            ValidateChildren(childUid, vehicle, validation, ref ev);
        }
    }

    private void ValidateExit(Entity<KsCrewVehicleExitComponent> exit,
        Entity<KsCrewVehicleInteriorComponent> vehicle,
        KsCrewVehicleInteriorValidation validation)
    {
        var doorway = exit.Comp.Doorway;
        if (!vehicle.Comp.Doorways.ContainsKey(doorway))
        {
            validation.AddError($"exit door {ToPrettyString(exit)} names doorway '{doorway}', which the vehicle does not have");
            return;
        }

        if (!validation.TryClaim(DoorwayClaim, doorway, exit, out var previousExit))
            validation.AddError($"exit doors {ToPrettyString(previousExit)} and {ToPrettyString(exit)} both name doorway '{doorway}'");
    }
}
