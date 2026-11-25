using Robust.Shared.GameStates;

namespace Content.Shared._KS14.CrewVehicle.RunOver;

/// <summary>
///     On a mob that crew vehicles are passing over: it stays down, and isn't run over again, until they have all left
///         it. Exists only while a hull is over it, which is the only thing run-over checks every tick.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, UnsavedComponent, Access(typeof(KsCrewVehicleRunOverSystem))]
public sealed partial class KsCrewVehicleUnderHullComponent : Component
{
    /// <summary>
    ///     The hulls over it. Several, so that two hulls over one mob don't take turns forgetting it.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public HashSet<EntityUid> Hulls = new();
}
