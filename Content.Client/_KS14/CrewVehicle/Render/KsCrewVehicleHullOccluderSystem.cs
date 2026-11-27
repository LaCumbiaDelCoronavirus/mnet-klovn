using System.Linq;
using Content.Shared._KS14.CrewVehicle.Interior;
using Content.Shared._KS14.CrewVehicle.Render;
using Content.Shared._KS14.CrewVehicle.View;
using Robust.Client.Graphics;
using Robust.Client.Player;

namespace Content.Client._KS14.CrewVehicle.Render;

/// <summary>
///     Builds each crew vehicle's hull occluder on this client only, from its <see cref="KsCrewVehicleHullOccluderComponent"/>,
///         and keeps it off on the one hull this client's eye is inside: the occluder would blind an eye inside it.
/// </summary>
/// <remarks>
///     The occluder is added with <c>NetSyncEnabled</c> off, so it is not one of the entity's networked components:
///         neither a server state nor a prediction reset removes it. Its <c>Enabled</c> is read when FOV and shadows are
///         built, so switching it takes effect that frame.
/// </remarks>
public sealed partial class KsCrewVehicleHullOccluderSystem : EntitySystem
{
    [Dependency] private OccluderSystem _occluderSystem = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private IEyeManager _eyeManager = default!;
    [Dependency] private EntityQuery<KsCrewVehicleHullOccluderComponent> _hullOccluderQuery = default!;
    [Dependency] private EntityQuery<KsCrewVehicleViewerComponent> _viewerQuery = default!;
    [Dependency] private EntityQuery<KsCrewVehicleInteriorMapComponent> _interiorMapQuery = default!;
    [Dependency] private EntityQuery<OccluderComponent> _occluderQuery = default!;

    /// <summary>
    ///     The hull whose occluder this client has switched off, if any.
    /// </summary>
    private EntityUid? _ownHull;

    [SubscribeLocalEvent]
    private void OnStartup(Entity<KsCrewVehicleHullOccluderComponent> hull, ref ComponentStartup args)
    {
        // A networked one is refused by validation; this is only a client noticing it too late.
        if (_occluderQuery.HasComp(hull))
        {
            Log.Error($"{ToPrettyString(hull)} has an Occluder already; its hull occluder was not built");
            return;
        }

        if (KsCrewVehicleHullOccluderValidationSystem.GetPolygonError(hull.Comp.Polygon) is { } error)
        {
            Log.Error($"hull occluder of {ToPrettyString(hull)} {error}; not built");
            return;
        }

        var occluder = Factory.GetComponent<OccluderComponent>();
        occluder.NetSyncEnabled = false;
        AddComp(hull, occluder);
        var polygon = hull.Comp.Polygon;
        _occluderSystem.SetPolygon(hull, polygon.ToArray(), occluder);

        // A hull this client is already inside starts off.
        if (hull.Owner == _ownHull)
            _occluderSystem.SetEnabled(hull, false, occluder);
    }

    [SubscribeLocalEvent]
    private void OnShutdown(Entity<KsCrewVehicleHullOccluderComponent> hull, ref ComponentShutdown args)
    {
        if (TerminatingOrDeleted(hull))
            return;

        if (_occluderQuery.TryComp(hull, out var occluder) && !occluder.NetSyncEnabled)
            RemComp(hull, occluder);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        // Worked out every frame because three things feed it (what the player is attached to, whether they sit in a
        //      view seat, and which map their eye is on), and it is two lookups.
        var ownHull = GetOwnHull();
        if (ownHull == _ownHull)
            return;

        SetHullOccluderEnabled(_ownHull, true);
        SetHullOccluderEnabled(ownHull, false);
        _ownHull = ownHull;
    }

    /// <summary>
    ///     The hull this client's eye looks from the inside of: the vehicle of the view seat the player sits in, or the
    ///         vehicle whose interior map their eye is on (whose outside view is cast from the hull).
    /// </summary>
    public EntityUid? GetOwnHull()
    {
        if (_playerManager.LocalEntity is { } localUid &&
            _viewerQuery.TryComp(localUid, out var viewerComponent))
        {
            var viewedVehicle = viewerComponent.Vehicle;
            if (viewedVehicle.IsValid())
                return viewedVehicle;
        }

        if (_mapSystem.TryGetMap(_eyeManager.CurrentEye.Position.MapId, out var mapUid) &&
            _interiorMapQuery.TryComp(mapUid, out var interiorMapComponent))
        {
            var interiorVehicle = interiorMapComponent.Vehicle;
            if (interiorVehicle.IsValid())
                return interiorVehicle;
        }

        return null;
    }

    private void SetHullOccluderEnabled(EntityUid? hullUid, bool enabled)
    {
        if (hullUid is not { } uid ||
            !_hullOccluderQuery.HasComp(uid) ||
            !_occluderQuery.TryComp(uid, out var occluder) ||
            occluder.NetSyncEnabled)
            return;

        _occluderSystem.SetEnabled(uid, enabled, occluder);
    }
}
