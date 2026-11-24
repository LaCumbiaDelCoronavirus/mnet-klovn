using Robust.Shared.GameStates;

namespace Content.Shared._KS14.CrewVehicle.AmmoPort;

/// <summary>
///     A console inside a crew vehicle through which one of its mounted weapons is loaded: ammunition that weapon takes,
///         used on the port, goes into it after a moment. Examining it shows the weapon's rounds.
/// </summary>
/// <remarks>
///     Networked, so that a port placed in a map file without a prototype still has its mount on the client, which
///         examines it.
/// </remarks>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedKsCrewVehicleAmmoPortSystem))]
public sealed partial class KsCrewVehicleAmmoPortComponent : Component
{
    /// <summary>
    ///     The hull's mount (<c>KsCrewVehicleMountsComponent</c>) whose weapon this loads.
    /// </summary>
    [DataField(required: true), AutoNetworkedField]
    public string Mount = string.Empty;

    /// <summary>
    ///     How long loading takes, whatever is loaded.
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan LoadDelay = TimeSpan.FromSeconds(2);
}
