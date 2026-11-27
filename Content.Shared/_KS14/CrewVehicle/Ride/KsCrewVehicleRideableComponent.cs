namespace Content.Shared._KS14.CrewVehicle.Ride;

/// <summary>
///     A hull people can climb onto and ride: they are parented to it and move with it, and get off by walking off its
///         edge, by a verb, or by being thrown off in a crash. Needs <c>Climbable</c>; its body fixture,
///         <see cref="Fixture"/>, is the deck (the contract is on <c>KsCrewVehicleHullBase</c>).
/// </summary>
/// <remarks>
///     Not networked: prototype data, which the client has as well.
/// </remarks>
[RegisterComponent, Access(typeof(KsCrewVehicleRideSystem))]
public sealed partial class KsCrewVehicleRideableComponent : Component
{
    /// <summary>
    ///     How many may ride at once.
    /// </summary>
    [DataField]
    public int MaxRiders = 4;

    /// <summary>
    ///     How fast, in m/s, a crash must be to throw the riders off (<c>KsCrewVehicleCrashedEvent.ImpactSpeed</c>).
    ///         Every crash the hull reports is already a hard one (<c>KsCrewVehicleSmash.MinSpeed</c>), so zero throws
    ///         them off at each.
    /// </summary>
    [DataField]
    public float ThrowOffSpeed;

    /// <summary>
    ///     Throw force per m/s of impact, as <c>KsCrewVehicleRunOver.ThrowForcePerSpeed</c>.
    /// </summary>
    [DataField]
    public float ThrowForcePerSpeed = 6f;

    /// <summary>
    ///     A rider who climbs on ends up at least this far inside the deck's edge.
    /// </summary>
    [DataField]
    public float DeckInset = 0.4f;

    /// <summary>
    ///     The hull's hard body fixture, which is the deck.
    /// </summary>
    [DataField]
    public string Fixture = "fix1";
}
