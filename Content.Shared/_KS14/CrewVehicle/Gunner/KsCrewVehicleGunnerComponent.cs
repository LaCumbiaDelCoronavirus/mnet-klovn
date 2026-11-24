using Robust.Shared.GameStates;

namespace Content.Shared._KS14.CrewVehicle.Gunner;

/// <summary>
///     On someone while they sit in a <see cref="KsCrewVehicleGunnerSeatComponent"/>: their shoot input fires
///         <see cref="Selected"/>, which is the only record of which weapon they fire.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, UnsavedComponent]
[Access(typeof(KsCrewVehicleGunnerSystem))]
public sealed partial class KsCrewVehicleGunnerComponent : Component
{
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public EntityUid Vehicle;

    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public EntityUid Seat;

    /// <summary>
    ///     The seat's weapons, in its order.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public List<EntityUid> Weapons = new();

    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public EntityUid? Selected;
}
