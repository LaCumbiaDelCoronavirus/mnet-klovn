using Content.Shared.Actions.Components;
using Content.Shared.Alert;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

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

    /// <summary>
    ///     With more than one mount, one of these per mount picks it; each shows its weapon's rounds.
    /// </summary>
    [DataField]
    public EntProtoId<InstantActionComponent> SelectAction = "KsCrewVehicleActionSelectWeapon";

    /// <summary>
    ///     With more than one mount: one <see cref="CycleAction"/> that switches to the next of them in turn and shows the
    ///         selected weapon's rounds, in place of one <see cref="SelectAction"/> per mount.
    /// </summary>
    [DataField]
    public bool CycleWeapons;

    [DataField]
    public EntProtoId<InstantActionComponent> CycleAction = "KsCrewVehicleActionCycleWeapon";

    /// <summary>
    ///     Shown to the gunner while they sit here: the selected weapon's rounds.
    /// </summary>
    [DataField]
    public ProtoId<AlertPrototype> AmmoAlert = "KsCrewVehicleAmmo";

    /// <summary>
    ///     The select actions, in the order of <see cref="Mounts"/>, or the one cycle action, made at map init in the seat's
    ///         action container. A data field, because the actions are saved in that container with the seat.
    /// </summary>
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadOnly)]
    public List<EntityUid> SelectActions = new();
}
