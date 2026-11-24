using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.CrewVehicle.Horn;

/// <summary>
///     A vehicle whose driver has a horn to sound, as an action of the vehicle's own, handed to whoever drives it.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, Access(typeof(KsCrewVehicleHornSystem))]
public sealed partial class KsCrewVehicleHornComponent : Component
{
    [DataField(required: true)]
    public SoundSpecifier Sound = default!;

    /// <summary>
    ///     The action the driver sounds it with. Its <c>useDelay</c> is the horn's cooldown.
    /// </summary>
    [DataField]
    public EntProtoId<InstantActionComponent> Action = "KsCrewVehicleActionHorn";

    /// <summary>
    ///     The vehicle's horn action, made in its own action container when it is initialised. The client hands it to a
    ///         driver it predicts, so this is networked.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public EntityUid? ActionEntity;
}

/// <summary>
///     Sounds a crew vehicle's horn. Raised on the vehicle, whose action it is.
/// </summary>
public sealed partial class KsCrewVehicleHornActionEvent : InstantActionEvent;
