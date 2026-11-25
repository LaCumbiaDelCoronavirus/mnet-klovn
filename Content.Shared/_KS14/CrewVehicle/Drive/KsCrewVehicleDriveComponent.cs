namespace Content.Shared._KS14.CrewVehicle.Drive;

/// <summary>
///     How a crew vehicle steers. The movement itself is the stock mover's, reached through the upstream vehicle
///         system's relay from whoever sits in the interior's driver seat.
/// </summary>
/// <remarks>
///     Only tuning, read from the prototype on both sides, so not networked.
/// </remarks>
[RegisterComponent, Access(typeof(KsCrewVehicleDriveSystem), typeof(KsCrewVehicleGripController))]
public sealed partial class KsCrewVehicleDriveComponent : Component
{
    /// <summary>
    ///     How fast the hull turns while left or right is held: always for tracks, at full speed for wheels.
    /// </summary>
    [DataField]
    public Angle TurnRate = Angle.FromDegrees(120);

    /// <summary>
    ///     Wheels or tracks: whether the hull can turn on the spot.
    /// </summary>
    [DataField]
    public KsCrewVehicleDriveTrain DriveTrain = KsCrewVehicleDriveTrain.Wheeled;

    /// <summary>
    ///     Wheeled: the speed along its facing, in m/s, at which the hull turns at the full <see cref="TurnRate"/>.
    ///         Slower it turns slower, at rest not at all, and backing up it turns the other way.
    /// </summary>
    [DataField]
    public float FullTurnSpeed = 2f;

    /// <summary>
    ///     Fraction of the driving speed used while backing up.
    /// </summary>
    [DataField]
    public float ReverseSpeedFraction = 0.66f;

    /// <summary>
    ///     The hull's non-hard fixture, a little larger than its body, that finds what it is about to touch. Two hulls
    ///         being driven are kinematic controllers, which physics never lets collide; this is how they still stop
    ///         against each other.
    /// </summary>
    [DataField]
    public string ContactFixture = "contact";

    /// <summary>
    ///     Whether the hull may be driven, turned or pushed off the floor, over space or lattice. Off, it stops at the
    ///         edge as at a wall (<see cref="Floor.KsCrewVehicleFloorSystem"/>).
    /// </summary>
    [DataField]
    public bool DriveOffGrid;

    /// <summary>
    ///     Scales the ground friction of the hull while nobody drives it, on top of what its movement's friction gives it:
    ///         a parked vehicle rolls when shoved rather than sliding on locked wheels. Below 1, it is pushed further and
    ///         coasts further when its driver gets off.
    /// </summary>
    [DataField]
    public float ParkedFrictionModifier = 1f;

    /// <summary>
    ///     The hull's body fixture, whose rim must stay over floor.
    /// </summary>
    [DataField]
    public string FloorFixture = "fix1";

    /// <summary>
    ///     The points round <see cref="FloorFixture"/>'s rim, hull-local, that must stay over floor. Worked out from the
    ///         fixture on first use, on each side alike: neither saved nor networked.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), Access(typeof(Floor.KsCrewVehicleFloorSystem))]
    public System.Numerics.Vector2[]? FloorSamples;
}

/// <summary>
///     How a crew vehicle's hull turns.
/// </summary>
public enum KsCrewVehicleDriveTrain : byte
{
    /// <summary>
    ///     Turns only while rolling, faster the faster it goes, in an arc; the other way when backing up.
    /// </summary>
    Wheeled,

    /// <summary>
    ///     Turns at its full rate whether moving or not, on the spot if need be.
    /// </summary>
    Tracked,
}
