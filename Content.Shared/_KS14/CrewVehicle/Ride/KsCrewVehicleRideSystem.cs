using System.Numerics;
using Content.Shared._KS14.CrewVehicle.Entry;
using Content.Shared._KS14.CrewVehicle.Smash;
using Content.Shared._KS14.PopupLocalization;
using Content.Shared.Climbing.Components;
using Content.Shared.Climbing.Events;
using Content.Shared.Climbing.Systems;
using Content.Shared.Interaction;
using Content.Shared.Maps;
using Content.Shared.Movement.Events;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.Pulling.Events;
using Content.Shared.Verbs;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.CrewVehicle.Ride;

/// <summary>
///     Riding a crew vehicle's hull. Climbing onto one is the stock climb; once on, the rider is parented to the hull,
///         so it moves and turns with it and walks about its deck in the deck's frame. It gets off by walking off the
///         edge (the climb ends, and the rider is unparented after the step), by the climb-down verb, or by being thrown
///         off in a crash (server). Riders are not occupants.
/// </summary>
/// <remarks>
///     <para>
///         There is no list of riders: they are the hull's transform children with a <see cref="KsCrewVehicleRiderComponent"/>
///             naming it. A rider has the marker exactly while it is parented to the hull and climbing on it.
///     </para>
///     <para>
///         Walking off and being thrown off are found inside physics callbacks (the climb sensor's end of contact, the
///             hull's crash), where reparenting is unsafe: both are queued and done by
///             <see cref="KsCrewVehicleRideController"/> after the step's solve, the same tick.
///     </para>
///     <para>
///         Predicted for the rider: the climb, the mount, walking off and the verb. The crash throw is the server's.
///     </para>
/// </remarks>
public sealed partial class KsCrewVehicleRideSystem : EntitySystem
{
    [Dependency] private ClimbSystem _climbSystem = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private INetManager _netManager = default!;
    [Dependency] private Content.Shared.Gravity.SharedGravitySystem _gravitySystem = default!;
    [Dependency] private SharedInteractionSystem _interactionSystem = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private SharedPhysicsSystem _physicsSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private TurfSystem _turfSystem = default!;

    [Dependency] private EntityQuery<ClimbingComponent> _climbingQuery = default!;
    [Dependency] private EntityQuery<FixturesComponent> _fixturesQuery = default!;
    [Dependency] private EntityQuery<MapGridComponent> _gridQuery = default!;
    [Dependency] private EntityQuery<PhysicsComponent> _physicsQuery = default!;
    [Dependency] private EntityQuery<KsCrewVehicleRiderComponent> _riderQuery = default!;
    [Dependency] private EntityQuery<TargetedProjectileComponent> _targetedProjectileQuery = default!;

    private static readonly LocId FullMessage = "ks-crew-vehicle-ride-full";
    private static readonly LocId ClimbDownVerb = "ks-crew-vehicle-ride-verb-climb-down";
    private static readonly LocId NowhereToClimbDownMessage = "ks-crew-vehicle-ride-nowhere-to-climb-down";
    private static readonly LocId ClimbDownFirstMessage = "ks-crew-vehicle-ride-climb-down-first";

    /// <summary>
    ///     How far past the deck's edge, beyond its own size, a rider climbing down lands.
    /// </summary>
    private const float ClimbDownMargin = 0.05f;

    /// <summary>
    ///     How far past the deck's edge a rider whose climb ended still counts as standing on it: less than the climb
    ///         sensor's radius, which a rider walking off has gone past.
    /// </summary>
    private const float ResumeMargin = 0.25f;

    // Found inside physics callbacks, done after the solve (KsCrewVehicleRideController). Transient: drained each step.
    private readonly HashSet<EntityUid> _walkedOff = new();
    private readonly List<PendingThrow> _pendingThrows = new();
    private readonly List<EntityUid> _scratch = new();
    private readonly List<EntityUid> _hullRiders = new();

    private readonly record struct PendingThrow(EntityUid Hull, EntityUid Rider, Vector2 Direction, float Force);

    #region Queries

    /// <summary>
    ///     Whether <paramref name="uid"/> rides <paramref name="hullUid"/>.
    /// </summary>
    public bool IsRiderOf(EntityUid? uid, EntityUid hullUid)
    {
        return uid is { } riderUid &&
               _riderQuery.TryComp(riderUid, out var riderComponent) &&
               riderComponent.LifeStage <= ComponentLifeStage.Running &&
               riderComponent.Hull == hullUid;
    }

    /// <summary>
    ///     The hull <paramref name="uid"/> rides, if any.
    /// </summary>
    public EntityUid? GetRiddenHull(EntityUid uid)
    {
        return _riderQuery.TryComp(uid, out var riderComponent) && riderComponent.LifeStage <= ComponentLifeStage.Running
            ? riderComponent.Hull
            : null;
    }

    /// <summary>
    ///     The hull's riders, into <paramref name="riders"/>.
    /// </summary>
    public void GetRiders(EntityUid hullUid, List<EntityUid> riders)
    {
        var children = Transform(hullUid).ChildEnumerator;
        while (children.MoveNext(out var childUid))
        {
            if (IsRiderOf(childUid, hullUid))
                riders.Add(childUid);
        }
    }

    private int CountRiders(EntityUid hullUid, EntityUid except)
    {
        var count = 0;
        var children = Transform(hullUid).ChildEnumerator;
        while (children.MoveNext(out var childUid))
        {
            if (childUid != except && IsRiderOf(childUid, hullUid))
                count++;
        }

        return count;
    }

    #endregion

    #region Getting on

    /// <summary>
    ///     Refused before the do-after, so the climber is told and doesn't wait for nothing.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnAttemptClimb(Entity<KsCrewVehicleRideableComponent> hull, ref AttemptClimbEvent args)
    {
        if (args.Cancelled || CountRiders(hull.Owner, args.Climber) < hull.Comp.MaxRiders)
            return;

        args.Cancelled = true;
        _popupSystem.PopupEntity(KsPopupMessage.Create(FullMessage, ("vehicle", hull.Owner)), hull.Owner, args.User);
    }

    /// <summary>
    ///     Every climb, those that skip the attempt too (a shove onto it, a direct <c>Climb</c>). Pure.
    /// </summary>
    // A by-value class event: the engine has Entity<T> handlers only for ref events (RA0054).
    [SubscribeLocalEvent]
    private void OnTargetBeforeClimb(EntityUid uid, KsCrewVehicleRideableComponent component, TargetBeforeClimbEvent args)
    {
        if (CountRiders(uid, args.GettingPutOnTable) >= component.MaxRiders)
            args.Cancel();
    }

    /// <summary>
    ///     Mounts: parents the climber to the hull and slides it, in the hull's frame, to where it climbed on, inside
    ///         the deck by <see cref="KsCrewVehicleRideableComponent.DeckInset"/>. Raised from a do-after's end or a
    ///         shove, never from inside physics, so reparenting is safe here.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnClimbedOn(Entity<KsCrewVehicleRideableComponent> hull, ref ClimbedOnEvent args)
    {
        var climberUid = args.Climber;
        if (IsRiderOf(climberUid, hull.Owner) || !TryGetDeck(hull, out var deck))
            return;

        var climberWorld = _transformSystem.GetWorldPosition(climberUid);
        var climberLocal = Vector2.Transform(climberWorld, _transformSystem.GetInvWorldMatrix(hull.Owner));
        var inner = Shrink(deck, hull.Comp.DeckInset);
        var target = new Vector2(Math.Clamp(climberLocal.X, inner.Left, inner.Right),
            Math.Clamp(climberLocal.Y, inner.Bottom, inner.Top));

        _climbSystem.KsMoveClimbingInto(climberUid, hull.Owner, target);

        var riderComponent = EnsureComp<KsCrewVehicleRiderComponent>(climberUid);
        riderComponent.Hull = hull.Owner;
        riderComponent.MountTarget = target;
        if (_physicsQuery.TryComp(climberUid, out var physicsComponent))
        {
            riderComponent.SleepingAllowed = physicsComponent.SleepingAllowed;
            _physicsSystem.SetSleepingAllowed(climberUid, physicsComponent, false);
            _physicsSystem.WakeBody(climberUid, body: physicsComponent);
        }

        Dirty(climberUid, riderComponent);
    }

    #endregion

    #region Getting off

    /// <summary>
    ///     Gets <paramref name="rider"/> off its hull: unparented, keeping its speed over the ground, to
    ///         <paramref name="at"/> if given, and no longer climbing.
    /// </summary>
    public void Dismount(Entity<KsCrewVehicleRiderComponent> rider, MapCoordinates? at = null)
    {
        var hullUid = rider.Comp.Hull;
        if (_physicsQuery.TryComp(rider, out var physicsComponent))
            _physicsSystem.SetSleepingAllowed(rider, physicsComponent, rider.Comp.SleepingAllowed);

        // First, so the parent change below isn't taken for being moved elsewhere.
        RemComp(rider, rider.Comp);

        var transformComponent = Transform(rider);
        if (at is { } coordinates)
            _transformSystem.SetMapCoordinates((rider, transformComponent), coordinates);
        else if (transformComponent.ParentUid == hullUid)
            _transformSystem.AttachToGridOrMap(rider, transformComponent);

        _climbSystem.KsStopClimb(rider.Owner);
    }

    /// <summary>
    ///     Walked off the edge, or the climb ended some other way: unparented after the step, as this is raised from
    ///         inside contact processing.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnEndClimb(Entity<KsCrewVehicleRiderComponent> rider, ref EndClimbEvent args)
    {
        // Only where the rider's body is simulated: the server, or its own predicting client.
        if (_netManager.IsClient &&
            (_gameTiming.ApplyingState || !_physicsQuery.TryComp(rider, out var physicsComponent) || !physicsComponent.Predict))
        {
            return;
        }

        _walkedOff.Add(rider);
    }

    /// <summary>
    ///     Taken elsewhere while riding (teleported, put in the interior, moved by an admin, buckled): no longer a rider.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnRiderParentChanged(Entity<KsCrewVehicleRiderComponent> rider, ref EntParentChangedMessage args)
    {
        if (_gameTiming.ApplyingState || args.Transform.ParentUid == rider.Comp.Hull)
            return;

        if (_physicsQuery.TryComp(rider, out var physicsComponent))
            _physicsSystem.SetSleepingAllowed(rider, physicsComponent, rider.Comp.SleepingAllowed);

        RemComp(rider, rider.Comp);
        _climbSystem.KsStopClimb(rider.Owner);
    }

    [SubscribeLocalEvent]
    private void OnGetVerbs(EntityUid uid, KsCrewVehicleRideableComponent component, GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanInteract || !_riderQuery.TryComp(args.User, out var riderComponent) || !IsRiderOf(args.User, uid))
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Act = () => TryClimbDown((user, riderComponent)),
            Text = Loc.GetString(ClimbDownVerb),
            Priority = 1,
        });
    }

    /// <summary>
    ///     Hops <paramref name="rider"/> down beside the hull, on the side nearest it if that is clear, else the next.
    /// </summary>
    public bool TryClimbDown(Entity<KsCrewVehicleRiderComponent> rider)
    {
        if (rider.Comp.LifeStage > ComponentLifeStage.Running)
            return false;

        if (TryGetClimbDownSpot(rider) is not { } spot)
        {
            _popupSystem.PopupEntity(KsPopupMessage.Create(NowhereToClimbDownMessage), rider, rider);
            return false;
        }

        Dismount(rider, spot);
        return true;
    }

    /// <summary>
    ///     The nearest clear point just outside the deck on each side, nearest side first: the rider's own size past the
    ///         edge, level with where it stands.
    /// </summary>
    public MapCoordinates? TryGetClimbDownSpot(Entity<KsCrewVehicleRiderComponent> rider)
    {
        var hullUid = rider.Comp.Hull;
        if (!TryComp<KsCrewVehicleRideableComponent>(hullUid, out var rideableComponent) ||
            !TryGetDeck((hullUid, rideableComponent), out var deck))
            return null;

        var riderTransform = Transform(rider);
        if (riderTransform.ParentUid != hullUid)
            return null;

        var local = riderTransform.LocalPosition;
        var margin = BodyRadius(rider) + ClimbDownMargin;
        var x = Math.Clamp(local.X, deck.Left, deck.Right);
        var y = Math.Clamp(local.Y, deck.Bottom, deck.Top);

        var sides = new[]
        {
            (Distance: local.X - deck.Left, Spot: new Vector2(deck.Left - margin, y)),
            (Distance: deck.Right - local.X, Spot: new Vector2(deck.Right + margin, y)),
            (Distance: local.Y - deck.Bottom, Spot: new Vector2(x, deck.Bottom - margin)),
            (Distance: deck.Top - local.Y, Spot: new Vector2(x, deck.Top + margin)),
        };
        Array.Sort(sides, (a, b) => a.Distance.CompareTo(b.Distance));

        var hullMatrix = _transformSystem.GetWorldMatrix(hullUid);
        var mapId = riderTransform.MapID;
        foreach (var (_, localSpot) in sides)
        {
            var world = Vector2.Transform(localSpot, hullMatrix);
            var coordinates = new MapCoordinates(world, mapId);
            if (IsClearToLand(rider, hullUid, coordinates))
                return coordinates;
        }

        return null;
    }

    private bool IsClearToLand(EntityUid riderUid, EntityUid hullUid, MapCoordinates coordinates)
    {
        var range = (coordinates.Position - _transformSystem.GetWorldPosition(riderUid)).Length() + 0.1f;
        if (!_interactionSystem.InRangeUnobstructed(riderUid, coordinates, range, predicate: uid => uid == hullUid))
            return false;

        // Off every grid, as a hull drifting in space, there is nothing to stand in the way.
        if (Transform(hullUid).GridUid is not { } gridUid || !_gridQuery.TryComp(gridUid, out var gridComponent))
            return true;

        var indices = _mapSystem.WorldToTile(gridUid, gridComponent, coordinates.Position);
        return !_turfSystem.IsTileBlocked(gridUid, indices, CollisionGroup.Impassable, gridComponent);
    }

    /// <summary>
    ///     How far the rider's hard body reaches from its centre.
    /// </summary>
    private float BodyRadius(EntityUid uid)
    {
        var radius = 0f;
        if (!_fixturesQuery.TryComp(uid, out var fixturesComponent))
            return radius;

        foreach (var fixture in fixturesComponent.Fixtures.Values)
        {
            if (!fixture.Hard)
                continue;

            var bounds = fixture.Shape.ComputeAABB(Robust.Shared.Physics.Transform.Empty, 0);
            radius = MathF.Max(radius, MathF.Max(MathF.Max(-bounds.Left, bounds.Right), MathF.Max(-bounds.Bottom, bounds.Top)));
        }

        return radius;
    }

    /// <summary>
    ///     Thrown off by a crash: the server's, as the run-over throw is. Raised inside the hull's start of contact,
    ///         after the crash stopped it, so the travel before is the event's.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnCrashed(Entity<KsCrewVehicleRideableComponent> hull, ref KsCrewVehicleCrashedEvent args)
    {
        if (!_netManager.IsServer || args.ImpactSpeed < hull.Comp.ThrowOffSpeed)
            return;

        var direction = args.Velocity.LengthSquared() > 0.0001f ? Vector2.Normalize(args.Velocity) : Vector2.Zero;
        var force = args.ImpactSpeed * hull.Comp.ThrowForcePerSpeed;
        _scratch.Clear();
        GetRiders(hull.Owner, _scratch);
        foreach (var riderUid in _scratch)
        {
            _pendingThrows.Add(new PendingThrow(hull.Owner, riderUid, direction, force));
        }

        _scratch.Clear();
    }

    /// <summary>
    ///     At termination, not shutdown: deleting an entity deletes its children before its components shut down, which
    ///         would delete the riders with it (CONTRIBUTING §6), as <c>SharedBuckleSystem.OnStrapTerminating</c> knows.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnHullTerminating(Entity<KsCrewVehicleRideableComponent> hull, ref EntityTerminatingEvent args)
    {
        DismountAll(hull.Owner);
    }

    [SubscribeLocalEvent]
    private void OnHullShutdown(Entity<KsCrewVehicleRideableComponent> hull, ref ComponentShutdown args)
    {
        if (!TerminatingOrDeleted(hull.Owner))
            DismountAll(hull.Owner);
    }

    private void DismountAll(EntityUid hullUid)
    {
        // The server's: a client deleting a hull it was sent the deletion of has the riders' new parent in that state.
        if (_netManager.IsClient)
            return;

        // Going with the grid or the map it is on, as a whole: nowhere to put them, and they go too.
        var hullTransform = Transform(hullUid);
        if (TerminatingOrDeleted(hullTransform.MapUid) || hullTransform.GridUid is { } gridUid && TerminatingOrDeleted(gridUid))
            return;

        var riders = new List<EntityUid>();
        GetRiders(hullUid, riders);
        foreach (var riderUid in riders)
        {
            if (_riderQuery.TryComp(riderUid, out var riderComponent))
                Dismount((riderUid, riderComponent));
        }
    }

    /// <summary>
    ///     Does what physics callbacks found this step: those a crash threw off are dismounted and thrown, and those whose
    ///         climb ended are unparented, if they really left the deck. Called after the solve by
    ///         <see cref="KsCrewVehicleRideController"/>; through copies, as dismounting can end a climb, which queues again.
    /// </summary>
    public void ProcessDeferred()
    {
        if (_pendingThrows.Count > 0)
        {
            var throws = _pendingThrows.ToArray();
            _pendingThrows.Clear();
            foreach (var pending in throws)
            {
                if (TerminatingOrDeleted(pending.Rider) ||
                    !IsRiderOf(pending.Rider, pending.Hull) ||
                    !_riderQuery.TryComp(pending.Rider, out var riderComponent))
                {
                    continue;
                }

                Dismount((pending.Rider, riderComponent));
                if (pending.Direction == Vector2.Zero)
                    continue;

                var ev = new KsCrewVehicleThrowRiderEvent(pending.Hull, pending.Rider, pending.Direction, pending.Force);
                RaiseLocalEvent(ref ev);
            }
        }

        if (_walkedOff.Count == 0)
            return;

        _scratch.Clear();
        _scratch.AddRange(_walkedOff);
        _walkedOff.Clear();
        foreach (var riderUid in _scratch)
        {
            if (TerminatingOrDeleted(riderUid) ||
                !_riderQuery.TryComp(riderUid, out var riderComponent) ||
                riderComponent.LifeStage > ComponentLifeStage.Running ||
                _climbingQuery.CompOrNull(riderUid) is { IsClimbing: true })
            {
                continue;
            }

            // Still over the deck: its climb sensor's contact was destroyed, not left, as when the hull's body type changes
            //      (driven or parked, stalled by a crash). It stays on.
            if (IsOverDeck((riderUid, riderComponent)) && _climbSystem.KsResumeClimb(riderUid))
                continue;

            Dismount((riderUid, riderComponent));
        }

        _scratch.Clear();
    }

    /// <summary>
    ///     Whether the rider still stands over its hull's deck, its centre within <see cref="ResumeMargin"/> of it. Walking
    ///         off ends the climb only once its climb sensor (0.35 m round it) has left the deck.
    /// </summary>
    private bool IsOverDeck(Entity<KsCrewVehicleRiderComponent> rider)
    {
        var hullUid = rider.Comp.Hull;
        var transformComponent = Transform(rider);
        return transformComponent.ParentUid == hullUid &&
               TryComp<KsCrewVehicleRideableComponent>(hullUid, out var rideableComponent) &&
               TryGetDeck((hullUid, rideableComponent), out var deck) &&
               deck.Enlarged(ResumeMargin).Contains(transformComponent.LocalPosition);
    }

    #endregion

    #region What riders can't do

    [SubscribeLocalEvent]
    private void OnCanEnter(Entity<KsCrewVehicleRideableComponent> hull, ref KsCanEnterCrewVehicleEvent args)
    {
        if (args.Cancelled || !IsRiderOf(args.User, hull.Owner))
            return;

        args.Cancelled = true;
        args.Message = ClimbDownFirstMessage;
    }

    // A by-value class event: the engine has Entity<T> handlers only for ref events (RA0054).
    [SubscribeLocalEvent]
    private void OnBeingPulledAttempt(EntityUid uid, KsCrewVehicleRideableComponent component, BeingPulledAttemptEvent args)
    {
        if (IsRiderOf(args.Puller, uid))
            args.Cancel();
    }

    /// <summary>
    ///     Riders are told when the hull moves from grid to grid, or off every grid: their own parent, the hull, stays
    ///         the same, and their gravity is worked out again only on a change of parent.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnHullParentChanged(Entity<KsCrewVehicleRideableComponent> hull, ref EntParentChangedMessage args)
    {
        _hullRiders.Clear();
        GetRiders(hull.Owner, _hullRiders);
        foreach (var riderUid in _hullRiders)
        {
            _gravitySystem.RefreshWeightless(riderUid);
        }

        _hullRiders.Clear();
    }

    /// <summary>
    ///     A rider stands on the deck: it can walk about it with no gravity, as on a hull drifting in space.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnCanWeightlessMove(Entity<KsCrewVehicleRiderComponent> rider, ref CanWeightlessMoveEvent args)
    {
        if (rider.Comp.LifeStage <= ComponentLifeStage.Running)
            args.CanMove = true;
    }

    /// <summary>
    ///     A shot aimed at a rider isn't stopped by the hull it rides; one that isn't hits what it meets.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnHullPreventCollide(Entity<KsCrewVehicleRideableComponent> hull, ref PreventCollideEvent args)
    {
        if (_targetedProjectileQuery.TryComp(args.OtherEntity, out var targetedComponent) &&
            IsRiderOf(targetedComponent.Target, hull.Owner))
        {
            args.Cancelled = true;
        }
    }

    #endregion

    /// <summary>
    ///     The deck: the hull's body fixture's bounds, in its own frame.
    /// </summary>
    public bool TryGetDeck(Entity<KsCrewVehicleRideableComponent> hull, out Box2 deck)
    {
        deck = default;
        if (!_fixturesQuery.TryComp(hull, out var fixturesComponent) ||
            !fixturesComponent.Fixtures.TryGetValue(hull.Comp.Fixture, out var fixture))
        {
            return false;
        }

        deck = fixture.Shape.ComputeAABB(Robust.Shared.Physics.Transform.Empty, 0);
        return true;
    }

    private static Box2 Shrink(Box2 box, float inset)
    {
        var insetX = MathF.Min(inset, box.Width / 2f);
        var insetY = MathF.Min(inset, box.Height / 2f);
        return new Box2(box.Left + insetX, box.Bottom + insetY, box.Right - insetX, box.Top - insetY);
    }
}
