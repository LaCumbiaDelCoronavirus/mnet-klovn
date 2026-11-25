using Robust.Shared.GameStates;
using Robust.Shared.Network;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.CrewVehicle.Smash;

/// <summary>
///     On something a crew vehicle has smashed through: that hull passes through it until it is gone or
///         <see cref="Until"/>.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause, Access(typeof(KsCrewVehicleSmashedSystem))]
public sealed partial class KsCrewVehicleSmashedComponent : Component
{
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public EntityUid Hull;

    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField, AutoPausedField]
    public TimeSpan Until;
}

/// <summary>
///     The hull drives on through what it smashes, on every side alike. The smash is predicted but the breaking is the
///         server's <c>Destructible</c>: without this, the driver's client would keep colliding with the window for the
///         round trip until it hears it broke, stopping the hull at it and pushing it back out, a step backward.
/// </summary>
public sealed partial class KsCrewVehicleSmashedSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private INetManager _netManager = default!;
    [Dependency] private SharedBroadphaseSystem _broadphaseSystem = default!;
    [Dependency] private SharedPhysicsSystem _physicsSystem = default!;

    [Dependency] private EntityQuery<FixturesComponent> _fixturesQuery = default!;

    /// <summary>
    ///     Lets <paramref name="hullUid"/> pass through <paramref name="obstacleUid"/> for <paramref name="time"/>.
    /// </summary>
    public void MarkSmashed(EntityUid obstacleUid, EntityUid hullUid, TimeSpan time)
    {
        var smashedComponent = EnsureComp<KsCrewVehicleSmashedComponent>(obstacleUid);
        smashedComponent.Hull = hullUid;
        smashedComponent.Until = _gameTiming.CurTime + time;
        Dirty(obstacleUid, smashedComponent);
        Refilter(obstacleUid);

        // The refilter drops the contact at the next update, but this step's solve would still stop the hull at it,
        //      on a client that keeps the obstacle a round trip longer than the server: switch it off for this step too.
        var contacts = _physicsSystem.GetContacts(hullUid);
        while (contacts.MoveNext(out var contact))
        {
            if (contact.OtherEnt(hullUid) == obstacleUid)
                contact.Enabled = false;
        }
    }

    [SubscribeLocalEvent]
    private void OnPreventCollide(Entity<KsCrewVehicleSmashedComponent> obstacle, ref PreventCollideEvent args)
    {
        if (obstacle.Comp.LifeStage <= ComponentLifeStage.Running &&
            args.OtherEntity == obstacle.Comp.Hull &&
            _gameTiming.CurTime < obstacle.Comp.Until)
        {
            args.Cancelled = true;
        }
    }

    /// <summary>
    ///     A smash this client predicted: when it was, what it cost the hull, and until when the hull passes through.
    /// </summary>
    public readonly record struct PredictedSmash(EntityUid Hull, EntityUid Obstacle, GameTick Tick, System.Numerics.Vector2 Velocity, TimeSpan Until);

    // Client only, and transient: the smashes this client predicted and the server hasn't yet confirmed. Each new server
    //      state resets the hull and the obstacle to before the smash, taking the predicted marker with it, and the ticks
    //      since are replayed from there: but the contact persists, so the smash isn't raised again, and the hull would
    //      collide with the obstacle anew or keep its old speed until the server's state, a step back once it comes. These
    //      outlive the reset, so the replay passes through it and loses the speed on the same tick.
    private readonly List<PredictedSmash> _predictedSmashes = new();

    public IReadOnlyList<PredictedSmash> PredictedSmashes => _predictedSmashes;

    /// <summary>
    ///     Whether this is a replay of a tick on which this client already predicted this smash: the replay controller
    ///         gives the hull the speed it had then.
    /// </summary>
    public bool IsReplayedSmash(EntityUid hullUid, EntityUid obstacleUid)
    {
        if (!_netManager.IsClient || _gameTiming.IsFirstTimePredicted)
            return false;

        foreach (var smash in _predictedSmashes)
        {
            if (smash.Hull == hullUid && smash.Obstacle == obstacleUid)
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Keeps a smash the driver's client predicts for the ticks it will replay. Does nothing on the server.
    /// </summary>
    public void RecordPredictedSmash(EntityUid hullUid, EntityUid obstacleUid, System.Numerics.Vector2 velocity, TimeSpan passTime)
    {
        if (!_netManager.IsClient || !_gameTiming.IsFirstTimePredicted)
            return;

        _predictedSmashes.Add(new PredictedSmash(hullUid, obstacleUid, _gameTiming.CurTick, velocity, _gameTiming.CurTime + passTime));
    }

    [SubscribeLocalEvent]
    private void OnHullPreventCollide(Entity<KsCrewVehicleSmashComponent> hull, ref PreventCollideEvent args)
    {
        foreach (var smash in _predictedSmashes)
        {
            if (smash.Hull == hull.Owner && smash.Obstacle == args.OtherEntity && _gameTiming.CurTime < smash.Until)
            {
                args.Cancelled = true;
                return;
            }
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // The server's to say when it is over: the state that ends it reaches a predicting client as it does anyone.
        if (_netManager.IsClient)
        {
            // Past its pass-through time, the server's state has long since caught up with it.
            _predictedSmashes.RemoveAll(smash => _gameTiming.CurTime >= smash.Until);
            return;
        }

        var query = EntityQueryEnumerator<KsCrewVehicleSmashedComponent>();
        while (query.MoveNext(out var uid, out var smashedComponent))
        {
            if (smashedComponent.LifeStage > ComponentLifeStage.Running || _gameTiming.CurTime < smashedComponent.Until)
                continue;

            // Removed at the end of the tick: this is enumerating them. Shut down now, so the refilter sees it gone.
            RemCompDeferred(uid, smashedComponent);
            Refilter(uid);
        }
    }

    /// <summary>
    ///     Asks physics to look at the obstacle's contacts again, so that a contact begun before (or after) the mark is
    ///         dropped (or made) at the next contact update. Only flags them: safe from inside a collision callback.
    /// </summary>
    private void Refilter(EntityUid uid)
    {
        if (!_fixturesQuery.TryComp(uid, out var fixturesComponent))
            return;

        foreach (var fixture in fixturesComponent.Fixtures.Values)
        {
            _broadphaseSystem.Refilter(uid, fixture);
        }
    }
}
