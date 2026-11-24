using System.Diagnostics.CodeAnalysis;
using Content.Shared._KS14.CrewVehicle.Interior;
using Content.Shared._KS14.RCD;
using Content.Shared.Light.Components;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;

namespace Content.Server._KS14.CrewVehicle.Interior;

/// <summary>
///     Loads each crew vehicle's interior map the first time it is needed, and refuses one that does not fit its
///         vehicle.
/// </summary>
public sealed partial class KsCrewVehicleInteriorSystem : SharedKsCrewVehicleInteriorSystem
{
    [Dependency] private MapLoaderSystem _mapLoaderSystem = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private KsNoBuildZoneSystem _noBuildZoneSystem = default!;
    [Dependency] private EntityQuery<KsCrewVehicleExitComponent> _exitQuery = default!;

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

        // ShuttleSystem roofs every grid that is not a map, which would shut out the interior map's ambient light.
        foreach (var grid in grids)
        {
            RemComp<ImplicitRoofComponent>(grid);
        }

        LinkInterior(vehicle, mapUid);
        _noBuildZoneSystem.SetBlocked(mapUid, KsCrewVehicleNoBuildSystem.NoBuildMessage);
        _mapSystem.InitializeMap(mapUid, unpause: true);

        interiorMap = mapUid;
        return true;
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
