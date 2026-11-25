using Content.Shared._KS14.CrewVehicle.Push;
using Content.Shared.Movement.Components;
using Robust.Client.Physics;
using Robust.Client.Player;
using Robust.Shared.Physics.Systems;

namespace Content.Client._KS14.CrewVehicle.Push;

/// <summary>
///     The driver's client predicts what its hull pushes, as it does what its player pulls, so the hull isn't stopped
///         on that client by an object it can't move until the server's state catches up.
/// </summary>
public sealed partial class KsCrewVehiclePushPredictionSystem : EntitySystem
{
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private SharedPhysicsSystem _physicsSystem = default!;

    [Dependency] private EntityQuery<MovementRelayTargetComponent> _relayTargetQuery = default!;

    [SubscribeLocalEvent]
    private void OnUpdatePredicted(Entity<KsCrewVehiclePushedComponent> pushed, ref UpdateIsPredictedEvent args)
    {
        if (pushed.Comp.LifeStage > ComponentLifeStage.Running || _playerManager.LocalEntity is not { } localUid)
            return;

        foreach (var hullUid in pushed.Comp.Hulls)
        {
            // Not one going away: this is also asked while the relay is taken down, as the driver gets up.
            if (_relayTargetQuery.TryComp(hullUid, out var relayTarget) &&
                relayTarget.LifeStage <= ComponentLifeStage.Running &&
                relayTarget.Source == localUid)
            {
                args.IsPredicted = true;
                return;
            }
        }
    }

    /// <summary>
    ///     The hull's own prediction is asked again whenever its relay comes or goes, as a driver sits or gets up: so is
    ///         that of what it is pushing, or the ex-driver's client would go on predicting it.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnHullUpdatePredicted(Entity<KsCrewVehiclePushComponent> hull, ref UpdateIsPredictedEvent args)
    {
        // Asked from inside whatever took the relay down: done afterwards.
        _hullsToRefresh.Add(hull.Owner);
    }

    // Transient work list: hulls whose relay came or went, drained each update.
    private readonly HashSet<EntityUid> _hullsToRefresh = new();
    private readonly List<EntityUid> _bodiesToRefresh = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_hullsToRefresh.Count == 0)
            return;

        var query = EntityQueryEnumerator<KsCrewVehiclePushedComponent>();
        while (query.MoveNext(out var pushedUid, out var pushedComponent))
        {
            foreach (var hullUid in pushedComponent.Hulls)
            {
                if (!_hullsToRefresh.Contains(hullUid))
                    continue;

                _bodiesToRefresh.Add(pushedUid);
                break;
            }
        }

        _hullsToRefresh.Clear();
        foreach (var bodyUid in _bodiesToRefresh)
        {
            _physicsSystem.UpdateIsPredicted(bodyUid);
        }

        _bodiesToRefresh.Clear();
    }
    [SubscribeLocalEvent]
    private void OnStartup(Entity<KsCrewVehiclePushedComponent> pushed, ref ComponentStartup args)
    {
        _physicsSystem.UpdateIsPredicted(pushed.Owner);
    }

    [SubscribeLocalEvent]
    private void OnAfterState(Entity<KsCrewVehiclePushedComponent> pushed, ref AfterAutoHandleStateEvent args)
    {
        _physicsSystem.UpdateIsPredicted(pushed.Owner);
    }

    [SubscribeLocalEvent]
    private void OnShutdown(Entity<KsCrewVehiclePushedComponent> pushed, ref ComponentShutdown args)
    {
        _physicsSystem.UpdateIsPredicted(pushed.Owner);
    }
}
