namespace Content.Shared._KS14.CrewVehicle.Turret;

/// <summary>
///     A mounted weapon that turns toward where its gunner aims, independent of the hull, and fires only once it points
///         there. Tuning only, never changed at runtime; what it turns to is <see cref="KsCrewVehicleTurretAimComponent"/>.
/// </summary>
[RegisterComponent, Access(typeof(KsCrewVehicleTurretSystem))]
public sealed partial class KsCrewVehicleTurretComponent : Component
{
    /// <summary>
    ///     How fast it turns, per second.
    /// </summary>
    [DataField]
    public Angle TraverseRate = Angle.FromDegrees(180);

    /// <summary>
    ///     How far off the target it may point and still fire.
    /// </summary>
    [DataField]
    public Angle FireTolerance = Angle.FromDegrees(20);

    /// <summary>
    ///     The gunner's client tells the turret where it aims at most this often...
    /// </summary>
    [DataField]
    public TimeSpan AimSendInterval = TimeSpan.FromSeconds(0.05);

    /// <summary>
    ///     ...and only once the aim has moved at least this far.
    /// </summary>
    [DataField]
    public Angle AimSendThreshold = Angle.FromDegrees(1);
}
