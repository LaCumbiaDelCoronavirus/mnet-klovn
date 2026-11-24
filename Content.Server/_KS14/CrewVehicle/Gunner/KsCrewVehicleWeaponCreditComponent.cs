using Robust.Shared.Network;

namespace Content.Server._KS14.CrewVehicle.Gunner;

/// <summary>
///     Who last fired a mounted weapon, for the kill credit of its rounds that land after the shot.
/// </summary>
[RegisterComponent, UnsavedComponent, Access(typeof(KsCrewVehicleShotRecordSystem))]
public sealed partial class KsCrewVehicleWeaponCreditComponent : Component
{
    [ViewVariables(VVAccess.ReadOnly)]
    public NetUserId? LastShotRecipient;
}
