using System.Numerics;
using Content.Shared._KS14.CrewVehicle.Interior;
using Content.Shared._KS14.CrewVehicle.View;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.CrewVehicle.View;

/// <summary>
///     Sends everyone inside a crew vehicle what is around it outside, for as long as they are inside, seated or not:
///         each occupant with a player gets a viewer entity on the hull, with its own eye, that their session is
///         subscribed to.
/// </summary>
/// <remarks>
///     A separate entity rather than the hull itself, because the eye's PVS scale is the viewer's own and differs from
///         occupant to occupant, and because pointing an eye at the hull and back (<see cref="SharedEyeSystem.SetTarget"/>)
///         subscribes the session to the hull and unsubscribes it again by itself.
///     <para/>
///     Opened on entry rather than on sitting, so the hull is already on the client by the time any eye is pointed at
///         it: the client drops an eye target it has not been sent.
/// </remarks>
public sealed partial class KsCrewVehicleOutsidePvsSystem : EntitySystem
{
    private static readonly EntProtoId ViewerPrototype = "KsCrewVehicleOutsideViewer";

    [Dependency] private ViewSubscriberSystem _viewSubscriberSystem = default!;
    [Dependency] private SharedEyeSystem _eyeSystem = default!;
    [Dependency] private EntityQuery<ActorComponent> _actorQuery = default!;
    [Dependency] private EntityQuery<EyeComponent> _eyeQuery = default!;
    [Dependency] private EntityQuery<KsCrewVehicleOutsideViewComponent> _outsideViewQuery = default!;

    [SubscribeLocalEvent]
    private void OnOccupantStartup(Entity<KsCrewVehicleOccupantComponent> occupant, ref ComponentStartup args)
    {
        TryOpen(occupant);
    }

    [SubscribeLocalEvent]
    private void OnOccupantShutdown(Entity<KsCrewVehicleOccupantComponent> occupant, ref ComponentShutdown args)
    {
        Close(occupant.Owner);
    }

    // PlayerAttachedEvent and PlayerDetachedEvent are by-value class events, which take no Entity<T> handler (RA0054).
    [SubscribeLocalEvent]
    private void OnOccupantPlayerAttached(EntityUid uid, KsCrewVehicleOccupantComponent component, PlayerAttachedEvent args)
    {
        TryOpen((uid, component));
    }

    [SubscribeLocalEvent]
    private void OnOccupantPlayerDetached(EntityUid uid, KsCrewVehicleOccupantComponent component, PlayerDetachedEvent args)
    {
        Close(uid);
    }

    [SubscribeLocalEvent]
    private void OnOutsideViewChanged(Entity<KsCrewVehicleOutsideViewComponent> occupant, ref KsCrewVehicleOutsideViewChangedEvent args)
    {
        RefreshScale(occupant);
    }

    private void TryOpen(Entity<KsCrewVehicleOccupantComponent> occupant)
    {
        if (!_actorQuery.TryComp(occupant, out var actorComponent) || TerminatingOrDeleted(occupant.Comp.Vehicle))
            return;

        var session = actorComponent.PlayerSession;
        if (_outsideViewQuery.TryComp(occupant, out var existing) && existing.LifeStage <= ComponentLifeStage.Running)
        {
            if (existing.Session == session && !TerminatingOrDeleted(existing.Viewer))
                return;

            Close(occupant.Owner);
        }

        var viewerUid = SpawnAttachedTo(ViewerPrototype, new EntityCoordinates(occupant.Comp.Vehicle, Vector2.Zero));
        _viewSubscriberSystem.AddViewSubscriber(viewerUid, session);

        var outsideViewComponent = EnsureComp<KsCrewVehicleOutsideViewComponent>(occupant);
        outsideViewComponent.Viewer = viewerUid;
        outsideViewComponent.Session = session;
        RefreshScale((occupant.Owner, outsideViewComponent));
    }

    private void Close(EntityUid occupantUid)
    {
        if (!_outsideViewQuery.TryComp(occupantUid, out var outsideViewComponent) ||
            outsideViewComponent.LifeStage > ComponentLifeStage.Running)
            return;

        // A viewer that went with its hull has already been taken off the session by the engine.
        if (!TerminatingOrDeleted(outsideViewComponent.Viewer))
        {
            _viewSubscriberSystem.RemoveViewSubscriber(outsideViewComponent.Viewer, outsideViewComponent.Session);
            QueueDel(outsideViewComponent.Viewer);
        }

        // Deferred: this runs from the shutdown of the occupant's other components, and from player detachment.
        RemCompDeferred(occupantUid, outsideViewComponent);
    }

    private void RefreshScale(Entity<KsCrewVehicleOutsideViewComponent> occupant)
    {
        if (occupant.Comp.LifeStage > ComponentLifeStage.Running ||
            TerminatingOrDeleted(occupant.Comp.Viewer) ||
            !_eyeQuery.TryComp(occupant.Comp.Viewer, out var eyeComponent))
            return;

        var ev = new KsGetCrewVehicleOutsidePvsScaleEvent(1f);
        RaiseLocalEvent(occupant, ref ev);
        _eyeSystem.SetPvsScale((occupant.Comp.Viewer, eyeComponent), ev.Scale);
    }
}
