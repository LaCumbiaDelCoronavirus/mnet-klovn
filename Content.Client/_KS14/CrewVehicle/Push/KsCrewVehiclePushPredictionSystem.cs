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
            if (_relayTargetQuery.TryComp(hullUid, out var relayTarget) && relayTarget.Source == localUid)
            {
                args.IsPredicted = true;
                return;
            }
        }
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
