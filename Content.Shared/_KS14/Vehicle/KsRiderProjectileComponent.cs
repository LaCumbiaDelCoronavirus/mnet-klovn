using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Vehicle;

/// <summary>
///     On a projectile fired from a vehicle, by its operator or a gun mounted on it: it passes through that vehicle.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(KsRiderProjectileSystem))]
public sealed partial class KsRiderProjectileComponent : Component
{
    /// <summary>
    ///     The vehicle it was fired from.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public EntityUid? Vehicle;
}
