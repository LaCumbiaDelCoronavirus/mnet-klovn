using Robust.Shared.GameStates;

namespace Content.Shared._KS14.CrewVehicle.Push;

/// <summary>
///     A crew vehicle that, driven, shoves what it drives into: loose objects, parked vehicles and other driven ones,
///         sharing its momentum with them by mass. Walls and anything anchored stay put, and stop it.
/// </summary>
[RegisterComponent, Access(typeof(KsCrewVehiclePushSystem), typeof(KsCrewVehiclePushController))]
public sealed partial class KsCrewVehiclePushComponent : Component
{
    /// <summary>
    ///     The hull's hard body, whose contacts with loose objects push them.
    /// </summary>
    [DataField]
    public string Fixture = "fix1";

    /// <summary>
    ///     How heavy what it pushes feels, against the hull: its real mass times this. Above 1 pushing is harder work.
    /// </summary>
    [DataField]
    public float PushedMassFactor = 1f;

    /// <summary>
    ///     How close, in m, the hull may come to another driven hull it pushes before that one counts as stuck (against a
    ///         wall, say) and stops it instead. Two driven hulls never collide, so this is all that keeps them apart.
    /// </summary>
    [DataField]
    public float StuckDistance = 0.03f;
}

/// <summary>
///     On a loose object a driven hull is pushing. The driver's client predicts it while it does, so the hull isn't
///         stopped on that client by an object it can't move.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true), Access(typeof(KsCrewVehiclePushSystem))]
public sealed partial class KsCrewVehiclePushedComponent : Component
{
    /// <summary>
    ///     The hulls pushing it.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public HashSet<EntityUid> Hulls = new();

    /// <summary>
    ///     Below this speed, in m/s, and no longer touched, it is let go: until then the driver's client keeps predicting
    ///         it sliding on.
    /// </summary>
    [DataField]
    public float RestSpeed = 0.05f;
}
