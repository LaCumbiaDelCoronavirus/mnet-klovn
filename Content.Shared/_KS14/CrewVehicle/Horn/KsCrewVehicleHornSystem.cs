using Content.Shared.Actions;
using Content.Shared.Vehicle;
using Robust.Shared.Audio.Systems;

namespace Content.Shared._KS14.CrewVehicle.Horn;

/// <summary>
///     Gives a crew vehicle's driver its horn while they drive, and sounds it.
/// </summary>
public sealed partial class KsCrewVehicleHornSystem : EntitySystem
{
    [Dependency] private ActionContainerSystem _actionContainerSystem = default!;
    [Dependency] private SharedActionsSystem _actionsSystem = default!;
    [Dependency] private SharedAudioSystem _audioSystem = default!;

    [SubscribeLocalEvent]
    private void OnMapInit(Entity<KsCrewVehicleHornComponent> vehicle, ref MapInitEvent args)
    {
        _actionContainerSystem.EnsureAction(vehicle.Owner, ref vehicle.Comp.ActionEntity, vehicle.Comp.Action);
        Dirty(vehicle);
    }

    [SubscribeLocalEvent]
    private void OnOperatorSet(Entity<KsCrewVehicleHornComponent> vehicle, ref VehicleOperatorSetEvent args)
    {
        // A predicting client may not have been sent the action yet; the server's state brings it.
        if (TerminatingOrDeleted(vehicle.Owner) ||
            _actionsSystem.GetAction(vehicle.Comp.ActionEntity, logError: false) is not { } action)
            return;

        KsCrewVehicleDriverActions.Hand(_actionsSystem, vehicle.Owner, action, args);
    }

    /// <remarks>
    ///     A by-value class event: the engine has <c>Entity&lt;T&gt;</c> handlers only for ref events.
    /// </remarks>
    [SubscribeLocalEvent]
    private void OnHorn(EntityUid uid, KsCrewVehicleHornComponent component, KsCrewVehicleHornActionEvent args)
    {
        if (args.Handled)
            return;

        _audioSystem.PlayPredicted(component.Sound, uid, args.Performer);
        args.Handled = true;
    }
}
