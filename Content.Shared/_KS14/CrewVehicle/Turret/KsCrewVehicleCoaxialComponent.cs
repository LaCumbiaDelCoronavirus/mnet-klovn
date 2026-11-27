namespace Content.Shared._KS14.CrewVehicle.Turret;

/// <summary>
///     A mounted weapon fixed to the turret it is mounted on (its mount's <c>Parent</c>, a weapon with
///         <see cref="KsCrewVehicleTurretComponent"/>): it has no traverse of its own, aiming it aims that turret, and it
///         fires only once the turret has brought it onto the target. Its gunner's seat must list that turret too, which is
///         checked when the interior loads.
/// </summary>
/// <remarks>
///     Tuning only, never changed at runtime, so not networked: the prototype is on both sides.
/// </remarks>
[RegisterComponent, Access(typeof(KsCrewVehicleTurretSystem))]
public sealed partial class KsCrewVehicleCoaxialComponent : Component
{
    /// <summary>
    ///     How far off the target its turret may point and still let it fire.
    /// </summary>
    [DataField]
    public Angle FireTolerance = Angle.FromDegrees(5);
}
