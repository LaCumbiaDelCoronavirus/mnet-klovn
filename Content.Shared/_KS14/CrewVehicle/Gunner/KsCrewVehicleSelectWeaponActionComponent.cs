using Robust.Shared.GameStates;

namespace Content.Shared._KS14.CrewVehicle.Gunner;

/// <summary>
///     On a gunner seat's select-weapon action: which of the seat's mounts it selects.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(KsCrewVehicleGunnerSystem))]
public sealed partial class KsCrewVehicleSelectWeaponActionComponent : Component
{
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public string MountId = string.Empty;
}
