using Robust.Shared.GameStates;

namespace Content.Shared._KS14.CrewVehicle.Gunner;

/// <summary>
///     On a gunner seat's select-weapon action: which of the seat's mounts it selects, or that it cycles through them.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(KsCrewVehicleGunnerSystem))]
public sealed partial class KsCrewVehicleSelectWeaponActionComponent : Component
{
    /// <summary>
    ///     Written at the seat's map init. A data field, because the action is saved with its seat and must still pick its
    ///         weapon when loaded.
    /// </summary>
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadOnly)]
    public string MountId = string.Empty;

    /// <summary>
    ///     Switches to the seat's next weapon after the selected one, whichever that is, instead of picking
    ///         <see cref="MountId"/>; it shows the selected weapon's rounds (<see cref="KsCrewVehicleGunnerSeatComponent.CycleWeapons"/>).
    /// </summary>
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadOnly)]
    public bool Cycle;
}
