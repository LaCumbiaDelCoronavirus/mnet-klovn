using Robust.Shared.GameStates;

namespace Content.Shared._KS14.CrewVehicle.Gunner;

/// <summary>
///     A seat inside a crew vehicle whose occupant fires the listed mounts of its hull with their ordinary shoot input.
///         It must also be a view seat, and no mount may be listed by two seats; both are checked when the interior
///         loads.
/// </summary>
/// <remarks>
///     Networked, so that a seat placed in a map file without a prototype still has its mounts on the client, which
///         predicts sitting down in it.
/// </remarks>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(KsCrewVehicleGunnerSystem))]
public sealed partial class KsCrewVehicleGunnerSeatComponent : Component
{
    /// <summary>
    ///     Mount ids on the hull (<c>KsCrewVehicleMountsComponent</c>), the first selected on sitting down.
    /// </summary>
    [DataField(required: true), AutoNetworkedField]
    public List<string> Mounts = new();
}
