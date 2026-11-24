using Content.Shared._KS14.CrewVehicle.Interior;
using Content.Shared.Buckle.Components;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Vehicle;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.CrewVehicle.View;

/// <summary>
///     Someone sitting in a <see cref="KsCrewVehicleViewSeatComponent"/> looks out from the hull: their eye follows the
///         vehicle, at the seat's zoom and with its cursor lean, and they are sent as far around it as the seat says.
///         Standing up puts all of it back.
/// </summary>
/// <remarks>
///     What is sent around the hull is <c>KsCrewVehicleOutsidePvsSystem</c>'s, on the server: this answers its
///         <see cref="KsGetCrewVehicleOutsidePvsScaleEvent"/> and tells it when the answer changes.
///     <para/>
///     A driver seat is also a view seat, and the upstream vehicle system points its operator's eye at the vehicle and
///         clears it again as well. Both set the same target, which <see cref="SharedEyeSystem.SetTarget"/> ignores
///         the second time, and this clears it only if it is still the vehicle; when the operator is taken away from
///         someone still seated, this puts it back.
/// </remarks>
public sealed partial class KsCrewVehicleViewSystem : EntitySystem
{
    /// <summary>
    ///     <see cref="SharedEyeCursorOffsetComponent"/> is abstract, with a concrete class on each side.
    /// </summary>
    private const string CursorOffsetComponentName = "EyeCursorOffset";

    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private SharedKsCrewVehicleInteriorSystem _interiorSystem = default!;
    [Dependency] private SharedEyeSystem _eyeSystem = default!;
    [Dependency] private SharedContentEyeSystem _contentEyeSystem = default!;
    [Dependency] private EntityQuery<KsCrewVehicleViewSeatComponent> _viewSeatQuery = default!;
    [Dependency] private EntityQuery<KsCrewVehicleViewerComponent> _viewerQuery = default!;
    [Dependency] private EntityQuery<EyeComponent> _eyeQuery = default!;
    [Dependency] private EntityQuery<ContentEyeComponent> _contentEyeQuery = default!;
    [Dependency] private EntityQuery<BuckleComponent> _buckleQuery = default!;
    [Dependency] private EntityQuery<StrapComponent> _strapQuery = default!;

    private Type _cursorOffsetType = default!;

    public override void Initialize()
    {
        base.Initialize();

        _cursorOffsetType = Factory.GetRegistration(CursorOffsetComponentName).Type;
    }

    [SubscribeLocalEvent]
    private void OnStrapped(Entity<KsCrewVehicleViewSeatComponent> seat, ref StrappedEvent args)
    {
        // A seat that arrives already occupied raises this again while the client applies it; the server's state
        //      already has the viewer, eye and zoom.
        if (_gameTiming.ApplyingState)
            return;

        if (!_interiorSystem.TryGetVehicle(seat.Owner, out var vehicle))
            return;

        var occupantUid = args.Buckle.Owner;
        if (_viewerQuery.TryComp(occupantUid, out var oldViewer))
            RemComp(occupantUid, oldViewer);

        var viewerComponent = new KsCrewVehicleViewerComponent
        {
            Vehicle = vehicle.Value.Owner,
            Seat = seat.Owner,
            AddedCursorOffset = seat.Comp.CursorOffset != null && !HasComp(occupantUid, _cursorOffsetType),
        };

        if (_contentEyeQuery.TryComp(occupantUid, out var contentEyeComponent))
        {
            viewerComponent.PreviousZoom = contentEyeComponent.TargetZoom;
            viewerComponent.PreviousMaxZoom = contentEyeComponent.MaxZoom;
        }

        // Filled in first, so that its startup has everything.
        AddComp(occupantUid, viewerComponent);
        Dirty(occupantUid, viewerComponent);
    }

    [SubscribeLocalEvent]
    private void OnUnstrapped(Entity<KsCrewVehicleViewSeatComponent> seat, ref UnstrappedEvent args)
    {
        if (_gameTiming.ApplyingState)
            return;

        if (_viewerQuery.TryComp(args.Buckle.Owner, out var viewerComponent) && viewerComponent.Seat == seat.Owner)
            RemComp(args.Buckle.Owner, viewerComponent);
    }

    [SubscribeLocalEvent]
    private void OnViewerStartup(Entity<KsCrewVehicleViewerComponent> viewer, ref ComponentStartup args)
    {
        // Arriving from the server, it starts before its fields are filled in: OnViewerAfterState does that case.
        if (_gameTiming.ApplyingState || !_viewSeatQuery.TryComp(viewer.Comp.Seat, out var seatComponent))
            return;

        ApplyCursorOffset(viewer, (viewer.Comp.Seat, seatComponent));

        if (_eyeQuery.TryComp(viewer.Owner, out var eyeComponent))
            _eyeSystem.SetTarget(viewer.Owner, viewer.Comp.Vehicle, eyeComponent);

        if (_contentEyeQuery.TryComp(viewer.Owner, out var contentEyeComponent))
        {
            _contentEyeSystem.SetMaxZoom(viewer.Owner, seatComponent.Zoom, contentEyeComponent);
            _contentEyeSystem.SetZoom(viewer.Owner, seatComponent.Zoom, eye: contentEyeComponent);
        }

        var ev = new KsCrewVehicleOutsideViewChangedEvent();
        RaiseLocalEvent(viewer.Owner, ref ev);
    }

    /// <summary>
    ///     EyeCursorOffset has no state, so the values the server gives it never reach the client: each side sets them,
    ///         the client also when a viewer arrives from the server.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnViewerAfterState(Entity<KsCrewVehicleViewerComponent> viewer, ref AfterAutoHandleStateEvent args)
    {
        if (_viewSeatQuery.TryComp(viewer.Comp.Seat, out var seatComponent))
            ApplyCursorOffset(viewer, (viewer.Comp.Seat, seatComponent));
    }

    [SubscribeLocalEvent]
    private void OnViewerShutdown(Entity<KsCrewVehicleViewerComponent> viewer, ref ComponentShutdown args)
    {
        // The server's state carries the eye, the zoom and the removal of the cursor lean along with the viewer's.
        if (_gameTiming.ApplyingState || TerminatingOrDeleted(viewer.Owner))
            return;

        if (viewer.Comp.AddedCursorOffset && HasComp(viewer.Owner, _cursorOffsetType))
            RemComp(viewer.Owner, _cursorOffsetType);

        // Only our own target: anything else the eye was pointed at since is not ours to clear.
        if (_eyeQuery.TryComp(viewer.Owner, out var eyeComponent) && eyeComponent.Target == viewer.Comp.Vehicle)
            _eyeSystem.SetTarget(viewer.Owner, null, eyeComponent);

        if (_contentEyeQuery.TryComp(viewer.Owner, out var contentEyeComponent))
        {
            _contentEyeSystem.SetMaxZoom(viewer.Owner, viewer.Comp.PreviousMaxZoom, contentEyeComponent);
            _contentEyeSystem.SetZoom(viewer.Owner, viewer.Comp.PreviousZoom, eye: contentEyeComponent);
        }

        // Still stored while it shuts down, which OnGetOutsidePvsScale knows to ignore.
        var ev = new KsCrewVehicleOutsideViewChangedEvent();
        RaiseLocalEvent(viewer.Owner, ref ev);
    }

    [SubscribeLocalEvent]
    private void OnGetOutsidePvsScale(Entity<KsCrewVehicleViewerComponent> viewer, ref KsGetCrewVehicleOutsidePvsScaleEvent args)
    {
        if (viewer.Comp.LifeStage > ComponentLifeStage.Running ||
            !_viewSeatQuery.TryComp(viewer.Comp.Seat, out var seatComponent))
            return;

        args.Scale = MathF.Max(args.Scale, seatComponent.PvsScale);
    }

    /// <summary>
    ///     The upstream vehicle system clears its operator's eye target when it stops being the operator, by any route.
    ///         Someone who is still in the seat keeps looking out.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnVehicleExited(Entity<KsCrewVehicleViewerComponent> viewer, ref OnVehicleExitedEvent args)
    {
        if (viewer.Comp.LifeStage > ComponentLifeStage.Running ||
            args.Vehicle.Owner != viewer.Comp.Vehicle ||
            TerminatingOrDeleted(viewer.Comp.Vehicle))
            return;

        // Standing up clears the buckle before the seat hears of it, so this is false for anyone getting up.
        if (!_buckleQuery.TryComp(viewer.Owner, out var buckleComponent) || buckleComponent.BuckledTo != viewer.Comp.Seat)
            return;

        if (_eyeQuery.TryComp(viewer.Owner, out var eyeComponent))
            _eyeSystem.SetTarget(viewer.Owner, viewer.Comp.Vehicle, eyeComponent);
    }

    [SubscribeLocalEvent]
    private void OnGetInteriorErrors(Entity<KsCrewVehicleViewSeatComponent> seat, ref KsGetCrewVehicleInteriorErrorsEvent args)
    {
        var validation = args.Validation;
        var name = ToPrettyString(seat);
        if (!_strapQuery.HasComp(seat))
            validation.AddError($"view seat {name} has no Strap, so nobody can sit in it");

        var pvsScale = seat.Comp.PvsScale;
        if (!float.IsFinite(pvsScale) || pvsScale < 1f || pvsScale > 100f)
            validation.AddError($"view seat {name} has pvsScale {pvsScale}, outside 1 to 100");

        var zoom = seat.Comp.Zoom;
        if (!float.IsFinite(zoom.X) || !float.IsFinite(zoom.Y) || zoom.X <= 0f || zoom.Y <= 0f)
            validation.AddError($"view seat {name} has zoom {zoom}, which is not positive");
        else if (pvsScale < MathF.Max(zoom.X, zoom.Y))
            validation.AddError($"view seat {name} has pvsScale {pvsScale}, too small for its zoom {zoom}: the edge of the view would be empty");

        if (seat.Comp.CursorOffset is { } cursorOffset && (!float.IsFinite(cursorOffset) || cursorOffset < 0f))
            validation.AddError($"view seat {name} has cursorOffset {cursorOffset}, which is negative");
    }

    /// <summary>
    ///     Gives the viewer the seat's cursor lean, if the seat gave it one. A component the state brings later finds this
    ///         one already there and keeps it.
    /// </summary>
    private void ApplyCursorOffset(Entity<KsCrewVehicleViewerComponent> viewer, Entity<KsCrewVehicleViewSeatComponent> seat)
    {
        if (!viewer.Comp.AddedCursorOffset || seat.Comp.CursorOffset is not { } maxOffset)
            return;

        if (!TryComp(viewer.Owner, _cursorOffsetType, out var component))
        {
            component = Factory.GetComponent(_cursorOffsetType);
            AddComp(viewer.Owner, component);
        }

        if (component is not SharedEyeCursorOffsetComponent cursorOffsetComponent)
            return;

        cursorOffsetComponent.MaxOffset = maxOffset;
        cursorOffsetComponent.OffsetSpeed = seat.Comp.CursorOffsetSpeed;
        // What is sent around the hull is the seat's pvsScale, not this.
        cursorOffsetComponent.PvsIncrease = 0f;
    }
}
