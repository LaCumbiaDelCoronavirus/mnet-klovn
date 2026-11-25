using System.Numerics;
using Content.Shared.Damage;
using Content.Shared.Guidebook;
using Robust.Shared.GameStates;
using Robust.Shared.Utility;

namespace Content.Shared._KS14.CrewVehicle.Interior;

/// <summary>
///     A vehicle people walk into: it owns a private map, loaded from <see cref="InteriorPath"/> the first time it is
///         needed, that is its inside.
/// </summary>
/// <remarks>
///     Only <see cref="LivingOccupants"/> is networked, for the client's predicted capacity check: the client has the
///         prototype's data anyway, and <see cref="InteriorMap"/> is only ever known to the server. Clients find a
///         vehicle from the inside through <see cref="KsCrewVehicleInteriorMapComponent"/>.
/// </remarks>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, Access(typeof(SharedKsCrewVehicleInteriorSystem))]
public sealed partial class KsCrewVehicleInteriorComponent : Component
{
    /// <summary>
    ///     The map file of the interior: one unrotated grid whose front is grid north.
    /// </summary>
    [DataField(required: true)]
    public ResPath InteriorPath;

    /// <summary>
    ///     The point on the interior grid, in its local coordinates, that sits over the hull's centre.
    /// </summary>
    [DataField]
    public Vector2 InteriorAnchor;

    /// <summary>
    ///     Where people get in and out, by doorway id. Every doorway has exactly one
    ///         <see cref="KsCrewVehicleExitComponent"/> door inside naming it.
    /// </summary>
    [DataField(required: true)]
    public Dictionary<string, KsCrewVehicleDoorway> Doorways = new();

    /// <summary>
    ///     How long getting in takes, standing at a doorway.
    /// </summary>
    [DataField]
    public TimeSpan EnterDelay = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     How long getting out takes, standing at an exit door.
    /// </summary>
    [DataField]
    public TimeSpan ExitDelay = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     How many living people fit inside. The dead, ghosts and things pulled in do not count.
    /// </summary>
    [DataField, GuidebookData]
    public int MaxOccupants = 4;

    /// <summary>
    ///     Nobody gets out while the hull moves faster than this, in metres per second.
    /// </summary>
    [DataField]
    public float MaxExitSpeed = 0.5f;

    /// <summary>
    ///     Dealt to each mob thrown clear when the vehicle is destroyed.
    /// </summary>
    [DataField]
    public DamageSpecifier? EjectDamage;

    /// <summary>
    ///     How long each mob thrown clear when the vehicle is destroyed is knocked down.
    /// </summary>
    [DataField]
    public TimeSpan EjectStun = TimeSpan.Zero;

    /// <summary>
    ///     How far from the hull's centre, in tiles, things thrown out of it are put: rings of eight places each, the
    ///         nearest free one first.
    /// </summary>
    [DataField]
    public List<float> EjectRadii = new() { 1.5f, 2.5f, 3.5f };

    /// <summary>
    ///     How many living occupants are inside, as the server last counted them: what <see cref="MaxOccupants"/> is
    ///         checked against.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public int LivingOccupants;

    /// <summary>
    ///     The loaded interior map, once something has needed it. Always null on the client.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public EntityUid? InteriorMap;
}

/// <summary>
///     One way in and out of a crew vehicle.
/// </summary>
[DataDefinition]
public sealed partial class KsCrewVehicleDoorway
{
    /// <summary>
    ///     Where the doorway is on the hull, in the hull's local frame (<see cref="KsCrewVehicleHullFrame"/>), and where
    ///         someone leaving through it lands.
    /// </summary>
    [DataField(required: true)]
    public Vector2 Offset;

    /// <summary>
    ///     How close to <see cref="Offset"/> someone must stand to use it.
    /// </summary>
    [DataField]
    public float Radius = 0.5f;
}
