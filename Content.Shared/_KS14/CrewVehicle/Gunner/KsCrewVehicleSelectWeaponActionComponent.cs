using Robust.Shared.GameStates;

namespace Content.Shared._KS14.CrewVehicle.Gunner;

/// <summary>
///     On a gunner seat's select-weapon action: which of the seat's mounts it selects.
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
}
