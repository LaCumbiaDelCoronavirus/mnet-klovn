using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Vehicle;

namespace Content.Shared._KS14.CrewVehicle;

/// <summary>
///     Hands one of a vehicle's own actions to whoever drives it, and takes it back from whoever stopped.
/// </summary>
public static class KsCrewVehicleDriverActions
{
    /// <param name="vehicleUid">The vehicle, whose action container holds <paramref name="action"/>.</param>
    /// <param name="change">The driver change, as the vehicle system reported it.</param>
    public static void Hand(SharedActionsSystem actionsSystem,
        EntityUid vehicleUid,
        Entity<ActionComponent> action,
        VehicleOperatorSetEvent change)
    {
        // Removing an action from someone it isn't attached to logs an error.
        if (change.OldOperator is { } oldOperatorUid && action.Comp.AttachedEntity == oldOperatorUid)
            actionsSystem.RemoveProvidedAction(oldOperatorUid, vehicleUid, action.Owner);

        if (change.NewOperator is { } newOperatorUid)
            actionsSystem.AddActionDirect(newOperatorUid, action.AsNullable());
    }
}
