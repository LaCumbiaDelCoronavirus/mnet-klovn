using Robust.Shared.GameStates;

namespace Content.Shared._KS14.CrewVehicle.Drive;

/// <summary>
///     When the driver can no longer drive (stunned, critical, dead, asleep, cuffed), the hull rolls once: with
///         <see cref="Chance"/> it goes out of control, on the throttle last held and with its steering wandering, until it
///         hits something, the driver recovers or someone else takes the wheel; otherwise it rolls to a stop.
/// </summary>
/// <remarks>
///     Rolled once per incapacitation, predicted with a seeded random: being stunned again while out of control doesn't
///         roll again. Runtime state is networked and never saved.
/// </remarks>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, Access(typeof(KsCrewVehicleLossOfControlSystem))]
public sealed partial class KsCrewVehicleLossOfControlComponent : Component
{
    /// <summary>
    ///     The chance, 0 to 1, that the hull goes out of control rather than rolling to a stop.
    /// </summary>
    [DataField]
    public float Chance = 0.5f;

    /// <summary>
    ///     How long the wandering steering keeps to one way (left, right or straight on) before it picks again.
    /// </summary>
    [DataField]
    public TimeSpan WanderInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     The share of steps, 0 to 1, on which a wandering hull turns its way: how slowly it wanders.
    /// </summary>
    [DataField]
    public float WanderStrength = 0.3f;

    /// <summary>
    ///     Whether this incapacitation has been rolled for.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public bool Rolled;

    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public bool OutOfControl;

    /// <summary>
    ///     The throttle held when control was lost: forward, back or none, at that speed.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public float Throttle;
}
