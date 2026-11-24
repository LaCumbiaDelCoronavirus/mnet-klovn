using Robust.Shared.GameStates;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.CrewVehicle.Turret;

/// <summary>
///     Where a turret is turning to. Added by its first aim; the angle it points at now is its transform's rotation.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, UnsavedComponent]
[Access(typeof(KsCrewVehicleTurretSystem))]
public sealed partial class KsCrewVehicleTurretAimComponent : Component
{
    /// <summary>
    ///     The rotation relative to its hull it turns to.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public Angle TargetAngle;

    /// <summary>
    ///     The tick the last aim was taken on: one a tick. Networked, so that the client's replays of its own aim reset
    ///         with it.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public GameTick LastAimTick;
}
