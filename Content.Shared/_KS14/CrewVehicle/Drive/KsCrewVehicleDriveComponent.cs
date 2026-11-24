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
    ///     How fast the hull turns: toward a pressed heading, or while left or right is held with tank controls.
    /// </summary>
    [DataField]
    public Angle TurnRate = Angle.FromDegrees(120);

    /// <summary>
    ///     Fraction of the driving speed used while backing up.
    /// </summary>
    [DataField]
    public float ReverseSpeedFraction = 0.66f;

    /// <summary>
    ///     Pressing a direction: fraction of the driving speed used while the hull is still turning to face it.
    /// </summary>
    [DataField]
    public float TurningSpeedFraction = 0.35f;

    /// <summary>
    ///     Pressing a direction: how far off it the hull may be and still count as facing it.
    /// </summary>
    [DataField]
    public Angle AlignTolerance = Angle.FromDegrees(5);

    /// <summary>
    ///     The hull's non-hard fixture, a little larger than its body, that finds what it is about to touch. Two hulls
    ///         being driven are kinematic controllers, which physics never lets collide; this is how they still stop
    ///         against each other.
    /// </summary>
    [DataField]
    public string ContactFixture = "contact";
}
