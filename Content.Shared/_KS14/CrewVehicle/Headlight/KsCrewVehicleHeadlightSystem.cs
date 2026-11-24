using Content.Shared.Actions;
using Content.Shared.Light.Components;
using Content.Shared.Vehicle;

namespace Content.Shared._KS14.CrewVehicle.Headlight;

/// <summary>
///     Gives a crew vehicle's driver its headlight switch while they drive.
/// </summary>
public sealed partial class KsCrewVehicleHeadlightSystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actionsSystem = default!;

    [Dependency] private EntityQuery<UnpoweredFlashlightComponent> _flashlightQuery = default!;

    [SubscribeLocalEvent]
    private void OnOperatorSet(Entity<KsCrewVehicleHeadlightComponent> vehicle, ref VehicleOperatorSetEvent args)
    {
        // A predicting client may not have been sent the action yet; the server's state brings it.
        if (TerminatingOrDeleted(vehicle.Owner) ||
            !_flashlightQuery.TryComp(vehicle.Owner, out var flashlightComponent) ||
            _actionsSystem.GetAction(flashlightComponent.ToggleActionEntity, logError: false) is not { } action)
            return;

        KsCrewVehicleDriverActions.Hand(_actionsSystem, vehicle.Owner, action, args);
    }
}
