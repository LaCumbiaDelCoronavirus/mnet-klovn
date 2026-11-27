using System.Numerics;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.CrewVehicle.Mounts;

/// <summary>
///     The weapons mounted on a crew vehicle's hull. Each is spawned at the hull's map init as a child of the hull, or of
///         the weapon of its <see cref="KsCrewVehicleMount.Parent"/> mount, so it moves and turns with it; gunner seats and
///         ammo ports name them by mount id.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedKsCrewVehicleMountSystem))]
public sealed partial class KsCrewVehicleMountsComponent : Component
{
    /// <summary>
    ///     Mount id to what is mounted there. Ids are data, named by gunner seats.
    /// </summary>
    [DataField(required: true)]
    public Dictionary<string, KsCrewVehicleMount> Mounts = new();

    /// <summary>
    ///     Mount id to its weapon, written at map init and when a weapon goes. A data field, because the weapons are
    ///         saved with the hull and must find their mounts again when it is loaded.
    /// </summary>
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadOnly)]
    public Dictionary<string, EntityUid> Weapons = new();
}

[DataDefinition]
public sealed partial class KsCrewVehicleMount
{
    [DataField(required: true)]
    public EntProtoId<KsCrewVehicleWeaponComponent> Prototype;

    /// <summary>
    ///     Another mount of the same hull whose weapon this one is mounted on: it is spawned as that weapon's child, with
    ///         <see cref="Offset"/> and <see cref="Rotation"/> in that weapon's frame, and turns with it. Null: on the hull.
    /// </summary>
    [DataField]
    public string? Parent;

    /// <summary>
    ///     Where it sits on the hull, hull-local (<see cref="KsCrewVehicleHullFrame"/>: forward is -Y), or on its
    ///         <see cref="Parent"/>'s weapon, in that weapon's frame.
    /// </summary>
    [DataField]
    public Vector2 Offset;

    /// <summary>
    ///     Which way it points at rest, relative to the hull's forward, or to its <see cref="Parent"/>'s weapon's.
    /// </summary>
    [DataField]
    public Angle Rotation;
}
