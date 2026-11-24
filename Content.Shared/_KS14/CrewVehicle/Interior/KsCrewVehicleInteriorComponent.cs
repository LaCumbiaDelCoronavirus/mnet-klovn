using System.Numerics;
using Robust.Shared.Utility;

namespace Content.Shared._KS14.CrewVehicle.Interior;

/// <summary>
///     A vehicle people walk into: it owns a private map, loaded from <see cref="InteriorPath"/> the first time it is
///         needed, that is its inside.
/// </summary>
/// <remarks>
///     Not networked: the client has the prototype's data anyway, and <see cref="InteriorMap"/> is only ever known to
///         the server. Clients find a vehicle from the inside through <see cref="KsCrewVehicleInteriorMapComponent"/>.
/// </remarks>
[RegisterComponent, Access(typeof(SharedKsCrewVehicleInteriorSystem))]
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
    ///     Where the doorway is on the hull, in the hull's local frame.
    /// </summary>
    [DataField(required: true)]
    public Vector2 Offset;

    /// <summary>
    ///     How close to <see cref="Offset"/> someone must stand to use it.
    /// </summary>
    [DataField]
    public float Radius = 0.5f;
}
