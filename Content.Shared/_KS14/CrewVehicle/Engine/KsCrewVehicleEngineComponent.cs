using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.CrewVehicle.Engine;

/// <summary>
///     A crew vehicle with an engine its driver starts and stops: it can only be driven while the engine runs, and can be
///         pulled only while it is off and nobody drives it. A vehicle without one always runs.
/// </summary>
/// <remarks>
///     The state is runtime state, networked but never saved: a running engine loads off.
/// </remarks>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true), AutoGenerateComponentPause]
[Access(typeof(KsCrewVehicleEngineSystem))]
public sealed partial class KsCrewVehicleEngineComponent : Component
{
    /// <summary>
    ///     How long the engine takes to start, from the toggle to driving: the length of <see cref="StartSound"/>.
    /// </summary>
    [DataField]
    public TimeSpan StartDelay = TimeSpan.FromSeconds(2);

    [DataField]
    public SoundSpecifier? StartSound = new SoundPathSpecifier("/Audio/Effects/Vehicle/vehiclestartup.ogg");

    /// <summary>
    ///     Loops while the engine runs: on the hull, and quieter on the interior map for those inside.
    /// </summary>
    [DataField]
    public SoundSpecifier? IdleSound = new SoundPathSpecifier("/Audio/Effects/Vehicle/vehicleengineidle.ogg");

    /// <summary>
    ///     The pitch both sounds play at.
    /// </summary>
    [DataField]
    public float Pitch = 0.7f;

    [DataField]
    public float IdleVolume = -4f;

    [DataField]
    public float IdleRange = 8f;

    /// <summary>
    ///     The idle loop's volume as heard inside, where it isn't positional.
    /// </summary>
    [DataField]
    public float InteriorIdleVolume = -10f;

    [DataField]
    public EntProtoId<InstantActionComponent> Action = "KsCrewVehicleActionEngine";

    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public EntityUid? ActionEntity;

    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public KsCrewVehicleEngineState State = KsCrewVehicleEngineState.Off;

    /// <summary>
    ///     While <see cref="KsCrewVehicleEngineState.Starting"/>: when it runs.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField, AutoPausedField]
    public TimeSpan ReadyAt;
}

[Serializable, NetSerializable]
public enum KsCrewVehicleEngineState : byte
{
    Off,
    Starting,
    Running,
}

/// <summary>
///     On an engine while it starts: the only engines visited each tick.
/// </summary>
[RegisterComponent, UnsavedComponent, Access(typeof(KsCrewVehicleEngineSystem))]
public sealed partial class KsCrewVehicleEngineStartingComponent : Component;

/// <summary>
///     Starts or stops a crew vehicle's engine. Raised on the vehicle, whose action it is.
/// </summary>
public sealed partial class KsCrewVehicleEngineActionEvent : InstantActionEvent;

/// <summary>
///     Whether the engine may start. Pure: raised by a UI or a prediction as often as it likes. The seam for fuel.
/// </summary>
[ByRefEvent]
public record struct KsCrewVehicleCanStartEngineEvent(EntityUid? User)
{
    public bool Cancelled;
}

/// <summary>
///     The engine has started starting, started, or stopped. Raised on the vehicle.
/// </summary>
[ByRefEvent]
public readonly record struct KsCrewVehicleEngineStateChangedEvent(KsCrewVehicleEngineState Old, KsCrewVehicleEngineState New);
