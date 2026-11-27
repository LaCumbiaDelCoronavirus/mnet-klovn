using Content.Shared.NPC.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Vehicle.Factions;

/// <summary>
///     A vehicle that takes its operator's NPC factions while they drive it, so NPCs fight it as they would the driver:
///         for a vehicle whose driver is out of their reach, as a crew vehicle's is, inside its interior. It drops them
///         while the driver is critical or dead, and takes them back if they recover.
/// </summary>
[RegisterComponent, Access(typeof(KsVehicleOperatorFactionsSystem))]
public sealed partial class KsVehicleOperatorFactionsComponent : Component
{
    /// <summary>
    ///     The vehicle's factions with nobody able to drive it. Empty, the default: it belongs to nobody, and no NPC is
    ///         hostile to it.
    /// </summary>
    [DataField]
    public HashSet<ProtoId<NpcFactionPrototype>> OwnFactions = new();
}
