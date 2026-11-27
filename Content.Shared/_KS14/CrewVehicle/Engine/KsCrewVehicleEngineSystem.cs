using Content.Shared.Actions;
using Content.Shared.Destructible;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Pulling.Events;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Vehicle;
using Content.Shared.Vehicle.Components;
using Content.Shared.Vehicle.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.CrewVehicle.Engine;

/// <summary>
///     A crew vehicle's engine: the driver's action starts it, which takes <see cref="KsCrewVehicleEngineComponent.StartDelay"/>,
///         or stops it; the upstream vehicle system's can-run check says no while it isn't running. It keeps running when
///         the driver gets off, stops when the vehicle is destroyed, and while it is off and nobody drives, the vehicle
///         can be pulled. Predicted; whether it may start is a pure event, the seam for fuel.
/// </summary>
public sealed partial class KsCrewVehicleEngineSystem : EntitySystem
{
    [Dependency] private ActionContainerSystem _actionContainerSystem = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private INetManager _netManager = default!;
    [Dependency] private PullingSystem _pullingSystem = default!;
    [Dependency] private SharedActionsSystem _actionsSystem = default!;
    [Dependency] private SharedAudioSystem _audioSystem = default!;
    [Dependency] private VehicleSystem _vehicleSystem = default!;

    [Dependency] private EntityQuery<PullableComponent> _pullableQuery = default!;
    [Dependency] private EntityQuery<VehicleComponent> _vehicleQuery = default!;

    [SubscribeLocalEvent]
    private void OnMapInit(Entity<KsCrewVehicleEngineComponent> vehicle, ref MapInitEvent args)
    {
        _actionContainerSystem.EnsureAction(vehicle.Owner, ref vehicle.Comp.ActionEntity, vehicle.Comp.Action);
        Dirty(vehicle);
    }

    [SubscribeLocalEvent]
    private void OnOperatorSet(Entity<KsCrewVehicleEngineComponent> vehicle, ref VehicleOperatorSetEvent args)
    {
        if (TerminatingOrDeleted(vehicle.Owner))
            return;

        // A driver at the wheel: no more being towed.
        if (args.NewOperator != null)
            StopPull(vehicle.Owner);

        // A predicting client may not have been sent the action yet; the server's state brings it.
        if (_actionsSystem.GetAction(vehicle.Comp.ActionEntity, logError: false) is { } action)
            KsCrewVehicleDriverActions.Hand(_actionsSystem, vehicle.Owner, action, args);
    }

    /// <remarks>
    ///     A by-value class event: the engine has <c>Entity&lt;T&gt;</c> handlers only for ref events.
    /// </remarks>
    [SubscribeLocalEvent]
    private void OnAction(EntityUid uid, KsCrewVehicleEngineComponent component, KsCrewVehicleEngineActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = component.State == KsCrewVehicleEngineState.Off
            ? TryStart((uid, component), args.Performer)
            : Stop((uid, component));
    }

    [SubscribeLocalEvent]
    private void OnCanRun(Entity<KsCrewVehicleEngineComponent> vehicle, ref VehicleCanRunEvent args)
    {
        if (!IsRunning(vehicle))
            args.CanRun = false;
    }

    /// <remarks>
    ///     A by-value class event: the engine has <c>Entity&lt;T&gt;</c> handlers only for ref events.
    /// </remarks>
    [SubscribeLocalEvent]
    private void OnBeingPulledAttempt(EntityUid uid, KsCrewVehicleEngineComponent component, BeingPulledAttemptEvent args)
    {
        if (component.State != KsCrewVehicleEngineState.Off || _vehicleQuery.CompOrNull(uid)?.Operator != null)
            args.Cancel();
    }

    /// <remarks>
    ///     A by-value class event: the engine has <c>Entity&lt;T&gt;</c> handlers only for ref events.
    /// </remarks>
    [SubscribeLocalEvent]
    private void OnDestruction(EntityUid uid, KsCrewVehicleEngineComponent component, DestructionEventArgs args)
    {
        SetState((uid, component), KsCrewVehicleEngineState.Off);
    }

    /// <summary>
    ///     Whether the engine runs, so the vehicle can be driven: running, or started and its start delay over.
    /// </summary>
    public bool IsRunning(Entity<KsCrewVehicleEngineComponent> vehicle)
    {
        return vehicle.Comp.State == KsCrewVehicleEngineState.Running ||
               vehicle.Comp.State == KsCrewVehicleEngineState.Starting && _gameTiming.CurTime >= vehicle.Comp.ReadyAt;
    }

    /// <summary>
    ///     Starts the engine, if it is off and nothing refuses: it runs once its start delay is over.
    /// </summary>
    public bool TryStart(Entity<KsCrewVehicleEngineComponent> vehicle, EntityUid? user)
    {
        if (vehicle.Comp.State != KsCrewVehicleEngineState.Off)
            return false;

        var ev = new KsCrewVehicleCanStartEngineEvent(user);
        RaiseLocalEvent(vehicle.Owner, ref ev);
        if (ev.Cancelled)
            return false;

        vehicle.Comp.ReadyAt = _gameTiming.CurTime + vehicle.Comp.StartDelay;
        _audioSystem.PlayPredicted(vehicle.Comp.StartSound, vehicle.Owner, user,
            vehicle.Comp.StartSound?.Params.WithPitchScale(vehicle.Comp.Pitch));
        _actionsSystem.SetCooldown(vehicle.Comp.ActionEntity, vehicle.Comp.StartDelay);

        if (vehicle.Comp.StartDelay <= TimeSpan.Zero)
        {
            SetState(vehicle, KsCrewVehicleEngineState.Running);
            return true;
        }

        EnsureComp<KsCrewVehicleEngineStartingComponent>(vehicle.Owner);
        SetState(vehicle, KsCrewVehicleEngineState.Starting);
        return true;
    }

    public bool Stop(Entity<KsCrewVehicleEngineComponent> vehicle)
    {
        if (vehicle.Comp.State == KsCrewVehicleEngineState.Off)
            return false;

        RemCompDeferred<KsCrewVehicleEngineStartingComponent>(vehicle.Owner);
        SetState(vehicle, KsCrewVehicleEngineState.Off);
        return true;
    }

    private void SetState(Entity<KsCrewVehicleEngineComponent> vehicle, KsCrewVehicleEngineState state)
    {
        var old = vehicle.Comp.State;
        if (old == state)
            return;

        vehicle.Comp.State = state;
        Dirty(vehicle);
        _actionsSystem.SetToggled(vehicle.Comp.ActionEntity, state != KsCrewVehicleEngineState.Off);

        // Starting up ends a tow.
        if (state != KsCrewVehicleEngineState.Off)
            StopPull(vehicle.Owner);

        _vehicleSystem.RefreshCanRun(vehicle.Owner);
        var ev = new KsCrewVehicleEngineStateChangedEvent(old, state);
        RaiseLocalEvent(vehicle.Owner, ref ev);
    }

    private void StopPull(EntityUid uid)
    {
        if (_pullableQuery.TryComp(uid, out var pullableComponent) && pullableComponent.BeingPulled)
            _pullingSystem.TryStopPull(uid, pullableComponent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<KsCrewVehicleEngineStartingComponent, KsCrewVehicleEngineComponent>();
        while (query.MoveNext(out var uid, out var startingComponent, out var engineComponent))
        {
            if (startingComponent.LifeStage > ComponentLifeStage.Running)
                continue;

            // The client's own marker goes once the server's state says it is no longer starting.
            if (_netManager.IsClient && engineComponent.State != KsCrewVehicleEngineState.Starting)
            {
                RemCompDeferred(uid, startingComponent);
                continue;
            }

            if (_gameTiming.CurTime < engineComponent.ReadyAt)
                continue;

            // The client only lets the hull go, the tick the server does; the state that it is running is the server's.
            if (_netManager.IsClient)
            {
                _vehicleSystem.RefreshCanRun(uid);
                continue;
            }

            // Removed at the end of the tick: this is enumerating them.
            RemCompDeferred(uid, startingComponent);
            SetState((uid, engineComponent), KsCrewVehicleEngineState.Running);
        }
    }
}
