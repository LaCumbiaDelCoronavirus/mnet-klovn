using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Movement;

/// <summary>
///     A mover that takes its frame, the grid or map its keys and its eye are turned to, from where
///         <see cref="Frame"/> is rather than from where it is itself. Someone looking out of a vehicle from inside it
///         sees the outside turned as a mob standing at the vehicle would.
/// </summary>
/// <remarks>
///     Read by the fork part of <c>SharedMoverController</c> (<c>SharedMoverController.Klovn.RelativeFrame.cs</c>). The
///         frame still changes the way a mob's does crossing from grid to grid: the eye holds still, then eases.
/// </remarks>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, UnsavedComponent]
public sealed partial class KsMoverRelativeFrameComponent : Component
{
    /// <summary>
    ///     The entity whose grid, or map when it is on none, is the mover's frame.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public EntityUid Frame;
}
