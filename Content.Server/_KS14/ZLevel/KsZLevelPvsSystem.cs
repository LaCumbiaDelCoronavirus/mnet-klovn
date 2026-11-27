using System.Linq;
using System.Numerics;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.ZLevel;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._KS14.ZLevel;

/// <summary>
///     Sends players the z-levels below (and, if asked for, above) wherever they see from: their own attached entity,
///         and every entity their session is view-subscribed to. Each of those is a <see cref="KsZLevelViewerComponent"/>
///         with subscribers of its own on the other levels, at its position and its PVS scale.
/// </summary>
/// <remarks>
///     It follows the engine's view subscriptions (<see cref="ViewSubscriberAddedEvent"/>), so an eye target, a
///         vehicle's outside viewer or a camera on a z-level gets the levels below with no code of its own here.
///         Several view points of one session at one spot (an eye target and a viewer riding it) share one set:
///         only the widest of them keeps subscribers.
/// </remarks>
public sealed partial class KsZLevelPvsSystem : EntitySystem
{
    /// <summary>
    ///     How close two view points of one session must be for one to stand in for the other.
    /// </summary>
    private const float SharedViewDistance = 1f;

    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private KsZLevelSystem _zLevelSystem = default!;
    [Dependency] private TransformSystem _transformSystem = default!;
    [Dependency] private ViewSubscriberSystem _viewSubscriberSystem = default!;
    [Dependency] private SharedEyeSystem _eyeSystem = default!;
    [Dependency] private EntityQuery<EyeComponent> _eyeQuery = default!;
    [Dependency] private EntityQuery<KsZLevelViewerComponent> _viewerQuery = default!;
    [Dependency] private EntityQuery<KsZLevelViewSubscriberComponent> _subscriberQuery = default!;

    private TimeSpan _updateInterval = TimeSpan.FromSeconds(0.25d);
    private TimeSpan _nextUpdate = TimeSpan.MinValue;
    private bool _sendAbove;
    private int _levelsBelow = 1;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(
            _configurationManager,
            KsCCVars.ZLevelPvsUpdateInterval,
            value => _updateInterval = TimeSpan.FromSeconds(value),
            true
        );

        Subs.CVar(_configurationManager, KsCCVars.ZLevelPvsSendAbove, OnSendAboveChanged, true);
        Subs.CVar(_configurationManager, KsCCVars.ZLevelPvsLevelsBelow, value => _levelsBelow = Math.Max(value, 0), true);
    }

    /// <summary>
    ///     Says out loud what flipping this will actually do, per player, at the moment it is flipped.
    /// </summary>
    /// <remarks>
    ///     There are three ways for "nothing is being sent" to be the right answer - the cvar is off, the
    ///         player has no z-level above them, or one is already being sent - and from outside the server
    ///         they look identical. Asking afterwards is too late, because a no-op logs nothing at all.
    /// </remarks>
    private void OnSendAboveChanged(bool value)
    {
        if (value == _sendAbove)
            return;

        _sendAbove = value;

        var query = AllEntityQuery<KsZLevelViewerComponent>();
        var viewers = 0;

        while (query.MoveNext(out var viewerUid, out var viewerComponent))
        {
            viewers++;
            var names = SessionNames(viewerComponent);

            if (!_zLevelSystem.TryGetZLevel(viewerUid, out var zLevelEntity))
            {
                Log.Info($"z-level pvs: {names} on {ToPrettyString(viewerUid)}, which is not on a z-level at all.");
                continue;
            }

            if (!_zLevelSystem.TryGetZLevelAbove(zLevelEntity.Value.Owner, out var aboveEntity))
            {
                Log.Info(
                    $"z-level pvs: {names} on {ToPrettyString(zLevelEntity.Value.Owner)}, " +
                    "which has nothing above it - there is no z-level for light to fall from.");

                continue;
            }

            Log.Info(
                $"z-level pvs: {names} on {ToPrettyString(zLevelEntity.Value.Owner)}, " +
                $"with {ToPrettyString(aboveEntity.Value.Owner)} above it.");
        }

        Log.Info($"z-level pvs: pvs_send_above is now {value}, across {viewers} viewer(s).");
    }

    private static string SessionNames(KsZLevelViewerComponent component)
    {
        return string.Join(", ", component.Sessions.Select(session => session.Name));
    }

    [SubscribeLocalEvent]
    private void OnPlayerAttached(PlayerAttachedEvent args)
    {
        AddSession(args.Entity, args.Player);
    }

    [SubscribeLocalEvent]
    private void OnPlayerDetached(PlayerDetachedEvent args)
    {
        // Still seen from if the session is also view-subscribed to it.
        if (args.Player.ViewSubscriptions.Contains(args.Entity))
            return;

        RemoveSession(args.Entity, args.Player);
    }

    [SubscribeLocalEvent]
    private void OnViewSubscriberAdded(ViewSubscriberAddedEvent args)
    {
        // Our own subscribers are view-subscribed too: they must not need subscribers of their own.
        if (_subscriberQuery.HasComp(args.View) || TerminatingOrDeleted(args.View))
            return;

        AddSession(args.View, args.Subscriber);
    }

    [SubscribeLocalEvent]
    private void OnViewSubscriberRemoved(ViewSubscriberRemovedEvent args)
    {
        if (args.Subscriber.AttachedEntity == args.View)
            return;

        RemoveSession(args.View, args.Subscriber);
    }

    private void AddSession(EntityUid viewUid, ICommonSession session)
    {
        var viewerComponent = EnsureComp<KsZLevelViewerComponent>(viewUid);
        if (!viewerComponent.Sessions.Add(session))
            return;

        foreach (var subscriberUid in SubscriberUids(viewerComponent))
        {
            _viewSubscriberSystem.AddViewSubscriber(subscriberUid, session);
        }
    }

    private void RemoveSession(EntityUid viewUid, ICommonSession session)
    {
        if (!_viewerQuery.TryComp(viewUid, out var viewerComponent) || !viewerComponent.Sessions.Remove(session))
            return;

        foreach (var subscriberUid in SubscriberUids(viewerComponent))
        {
            if (!TerminatingOrDeleted(subscriberUid))
                _viewSubscriberSystem.RemoveViewSubscriber(subscriberUid, session);
        }

        // Its shutdown takes the subscribers with it.
        if (viewerComponent.Sessions.Count == 0)
            RemComp(viewUid, viewerComponent);
    }

    private static IEnumerable<EntityUid> SubscriberUids(KsZLevelViewerComponent component)
    {
        foreach (var subscriberUid in component.BelowViewSubscriberUids)
        {
            yield return subscriberUid;
        }

        if (component.AboveViewSubscriberUid != EntityUid.Invalid)
            yield return component.AboveViewSubscriberUid;
    }

    [SubscribeLocalEvent]
    private void OnViewerShutdown(Entity<KsZLevelViewerComponent> entity, ref ComponentShutdown args)
    {
        // The subscriber may already be gone - its z-level deleted out from under it, say - and deleting a
        //      dead uid logs an error.
        RemoveBelowViewers(entity, keep: 0);
        DeleteSubscriber(entity.Comp.AboveViewSubscriberUid);
    }

    private void DeleteSubscriber(EntityUid subscriberUid)
    {
        if (subscriberUid == EntityUid.Invalid || TerminatingOrDeleted(subscriberUid))
            return;

        Del(subscriberUid);
    }

    [SubscribeLocalEvent]
    private void OnSubscriberShutdown(Entity<KsZLevelViewSubscriberComponent> entity, ref ComponentShutdown args)
    {
        if (!_viewerQuery.TryComp(entity.Comp.ViewerUid, out var viewerComponent))
            return;

        // Clear the now-dangling reference rather than removing the viewer component. Update spawns a fresh
        //      subscriber next tick; removing the component instead would cost the player z-level visibility
        //      for the rest of the round, and would recurse straight back into OnViewerShutdown.
        if (entity.Comp.Above)
        {
            if (viewerComponent.AboveViewSubscriberUid != entity.Owner)
                return;

            viewerComponent.AboveActive = false;
            viewerComponent.AboveViewSubscriberUid = EntityUid.Invalid;
        }
        else if (!viewerComponent.BelowViewSubscriberUids.Remove(entity.Owner))
        {
            // Not one of the viewer's any more: taken off the list by whoever is deleting it.
            return;
        }

        // Deleting the entity takes its subscriptions with it, so only unsubscribe while it is still alive.
        if (Terminating(entity.Owner))
            return;

        foreach (var session in viewerComponent.Sessions)
        {
            _viewSubscriberSystem.RemoveViewSubscriber(entity.Owner, session);
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_gameTiming.CurTime < _nextUpdate)
            return;

        _nextUpdate = _gameTiming.CurTime + _updateInterval;

        var eqe = AllEntityQuery<KsZLevelViewerComponent, TransformComponent>();
        while (eqe.MoveNext(out var viewerUid, out var viewerComponent, out var viewerTransformComponent))
        {
            var viewer = (viewerUid, viewerComponent);
            var position = _transformSystem.GetWorldPosition(viewerTransformComponent);
            var scale = PvsScale(viewerUid);

            // Off the stack entirely, so neither of them has anything to point at; or another view point of the same
            //      players, at the same spot and at least as wide, already loads the same levels.
            if (!_zLevelSystem.TryGetZLevel(viewerUid, out var zLevelEntity) ||
                IsStoodInFor(viewer, viewerTransformComponent.MapUid, position, scale))
            {
                RemoveBelowViewers(viewer, keep: 0);

                if (viewerComponent.AboveActive)
                    RemoveAboveViewer(viewer);

                continue;
            }

            // Handled before the one below and independently of it: somebody standing on the bottom z-level
            //      of a stack has nothing under them and can still have a lit one over their head.
            UpdateAboveViewer(viewer, zLevelEntity.Value, position, scale);

            UpdateBelowViewers(viewer, zLevelEntity.Value, position, scale);
        }
    }

    private float PvsScale(EntityUid viewUid)
    {
        return MathF.Max(_eyeQuery.CompOrNull(viewUid)?.PvsScale ?? 1f, 0.1f);
    }

    /// <summary>
    ///     Whether every session seeing from <paramref name="viewer"/> also sees from another of our viewers right beside
    ///         it that is at least as wide (the lower uid winning a tie), which loads the same levels for them.
    /// </summary>
    private bool IsStoodInFor(Entity<KsZLevelViewerComponent> viewer, EntityUid? mapUid, Vector2 position, float scale)
    {
        if (viewer.Comp.Sessions.Count == 0)
            return false;

        foreach (var session in viewer.Comp.Sessions)
        {
            if (!HasWiderViewBeside(session, viewer.Owner, mapUid, position, scale))
                return false;
        }

        return true;
    }

    private bool HasWiderViewBeside(ICommonSession session, EntityUid selfUid, EntityUid? mapUid, Vector2 position, float scale)
    {
        if (session.AttachedEntity is { } attachedUid && IsWiderViewBeside(attachedUid, selfUid, mapUid, position, scale))
            return true;

        foreach (var viewUid in session.ViewSubscriptions)
        {
            if (IsWiderViewBeside(viewUid, selfUid, mapUid, position, scale))
                return true;
        }

        return false;
    }

    private bool IsWiderViewBeside(EntityUid otherUid, EntityUid selfUid, EntityUid? mapUid, Vector2 position, float scale)
    {
        if (otherUid == selfUid ||
            !_viewerQuery.HasComp(otherUid) ||
            !TryComp(otherUid, out TransformComponent? otherTransformComponent) ||
            otherTransformComponent.MapUid != mapUid ||
            (_transformSystem.GetWorldPosition(otherTransformComponent) - position).Length() > SharedViewDistance)
            return false;

        var otherScale = PvsScale(otherUid);
        return otherScale > scale || otherScale == scale && otherUid.Id < selfUid.Id;
    }

    /// <summary>
    ///     Keeps a subscriber under the viewer on each of the z-levels below that are sent to them, and none on
    ///         any other.
    /// </summary>
    private void UpdateBelowViewers(
        Entity<KsZLevelViewerComponent> entity,
        Entity<KsZLevelComponent> zLevelEntity,
        Vector2 position,
        float scale)
    {
        var subscriberUids = entity.Comp.BelowViewSubscriberUids;
        var levels = 0;
        var currentUid = zLevelEntity.Owner;

        // Asked through the navigation API rather than by walking Node.Previous here, because a viewer
        //      riding a grid across a gap is standing on a map that is on no stack at all - so the walk
        //      finds nothing below them and takes their subscribers away, and the floor they can plainly
        //      see under their feet stops being transmitted for the whole of the ride. The API resolves
        //      a gap to the z-level it is anchored to, which is exactly that floor.
        // It null-checks Node internally too, the same way every other walk of the stack does: a z-level
        //      removed from its stack keeps the field pointing at a node that no longer belongs to a
        //      list, and one replicated before the rebuild has never been given one at all.
        while (levels < _levelsBelow &&
            _zLevelSystem.TryGetZLevelBelow(currentUid, out var belowZLevelEntity) &&
            TryComp<MapComponent>(belowZLevelEntity.Value.Owner, out var belowMapComponent))
        {
            currentUid = belowZLevelEntity.Value.Owner;

            if (levels == subscriberUids.Count)
                subscriberUids.Add(SpawnSubscriber(entity, above: false));

            // lol
            _transformSystem.SetMapCoordinates(
                subscriberUids[levels],
                new MapCoordinates(position, belowMapComponent.MapId)
            );
            SetSubscriberScale(subscriberUids[levels], scale);

            levels++;
        }

        RemoveBelowViewers(entity, keep: levels);
    }

    /// <summary>
    ///     Keeps the second subscriber, the one loading the z-level above, in step with the viewer.
    /// </summary>
    /// <remarks>
    ///     Nothing up there is ever drawn - the stack renders downwards only - so this exists purely so that
    ///         the z-level above has lights and occluders for its light map to be built from, which is what
    ///         lets light fall onto the viewer from it. Off unless asked for, because it is a whole extra
    ///         z-level of entities per player.
    /// </remarks>
    private void UpdateAboveViewer(
        Entity<KsZLevelViewerComponent> entity,
        Entity<KsZLevelComponent> zLevelEntity,
        Vector2 position,
        float scale)
    {
        if (!_sendAbove || !_zLevelSystem.TryGetZLevelAbove(zLevelEntity.Owner, out var aboveEntity))
        {
            if (entity.Comp.AboveActive)
                RemoveAboveViewer(entity);

            return;
        }

        if (!entity.Comp.AboveActive)
            AddAboveViewer(entity);

        if (entity.Comp.AboveViewSubscriberUid == EntityUid.Invalid ||
            !TryComp<MapComponent>(aboveEntity.Value.Owner, out var aboveMapComponent))
            return;

        _transformSystem.SetMapCoordinates(
            entity.Comp.AboveViewSubscriberUid,
            new MapCoordinates(position, aboveMapComponent.MapId)
        );
        SetSubscriberScale(entity.Comp.AboveViewSubscriberUid, scale);
    }

    private EntityUid SpawnSubscriber(Entity<KsZLevelViewerComponent> entity, bool above)
    {
        var subscriberUid = Spawn(null);
        Transform(subscriberUid).GridTraversal = false; // You know exactly where this is from

        // Without this the shutdown hook below never fires, and a subscriber deleted by anything other than us
        //      - its z-level being deleted, most likely - leaves the viewer pointing at a dead entity forever.
        //      Added before it is subscribed: it is how OnViewSubscriberAdded knows this is one of ours.
        var subscriberComponent = EnsureComp<KsZLevelViewSubscriberComponent>(subscriberUid);
        subscriberComponent.ViewerUid = entity.Owner;
        subscriberComponent.Above = above;

        // Its own eye, so that it is sent as wide around as the viewer it stands under.
        EnsureComp<EyeComponent>(subscriberUid);

        foreach (var session in entity.Comp.Sessions)
        {
            _viewSubscriberSystem.AddViewSubscriber(subscriberUid, session);
        }

        return subscriberUid;
    }

    private void SetSubscriberScale(EntityUid subscriberUid, float scale)
    {
        if (_eyeQuery.TryComp(subscriberUid, out var eyeComponent) && !MathHelper.CloseTo(eyeComponent.PvsScale, scale))
            _eyeSystem.SetPvsScale((subscriberUid, eyeComponent), scale);
    }

    /// <summary>
    ///     Deletes the viewer's subscribers below all but the nearest <paramref name="keep"/> levels.
    /// </summary>
    private void RemoveBelowViewers(Entity<KsZLevelViewerComponent> entity, int keep)
    {
        var subscriberUids = entity.Comp.BelowViewSubscriberUids;

        // Off the list before it is deleted: its shutdown looks for it there, and what it finds is the viewer's.
        for (var i = subscriberUids.Count - 1; i >= keep; i--)
        {
            var subscriberUid = subscriberUids[i];
            subscriberUids.RemoveAt(i);
            DeleteSubscriber(subscriberUid);
        }
    }

    private void AddAboveViewer(Entity<KsZLevelViewerComponent> entity)
    {
        var subscriberUid = SpawnSubscriber(entity, above: true);

        // Loud on purpose, and only on the transition. This is the one thing about the feature that is
        //      invisible from the client - there is no way to tell "the z-level above was never sent" apart
        //      from "it was sent and is unlit" by looking at it.
        Log.Info($"Now sending the z-level above {ToPrettyString(entity.Owner)} to {SessionNames(entity.Comp)}.");

        entity.Comp.AboveActive = true;
        entity.Comp.AboveViewSubscriberUid = subscriberUid;
    }

    private void RemoveAboveViewer(Entity<KsZLevelViewerComponent> entity)
    {
        Log.Info($"No longer sending the z-level above {ToPrettyString(entity.Owner)} to {SessionNames(entity.Comp)}.");

        entity.Comp.AboveActive = false;

        DeleteSubscriber(entity.Comp.AboveViewSubscriberUid);
        entity.Comp.AboveViewSubscriberUid = EntityUid.Invalid;
    }
}
