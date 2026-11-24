using Robust.Shared.Player;

namespace Content.Server._KS14.CrewVehicle.View;

/// <summary>
///     On an occupant of a crew vehicle with a player: the viewer entity on the hull that sends them what is around it.
/// </summary>
[RegisterComponent, UnsavedComponent, Access(typeof(KsCrewVehicleOutsidePvsSystem))]
public sealed partial class KsCrewVehicleOutsideViewComponent : Component
{
    /// <summary>
    ///     The viewer entity, a child of the hull with its own eye.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public EntityUid Viewer;

    /// <summary>
    ///     The session subscribed to <see cref="Viewer"/>.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public ICommonSession Session = default!;
}
