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

    /// <summary>
    ///     Played when the hull strikes something to push it at <see cref="ShoveSoundMinSpeed"/> or more: once a strike,
    ///         not while it keeps pushing, and no more often than <see cref="ShoveSoundCooldown"/>.
    /// </summary>
    [DataField]
    public Robust.Shared.Audio.SoundSpecifier? ShoveSound;

    /// <summary>
    ///     The same, for striking another vehicle.
    /// </summary>
    [DataField]
    public Robust.Shared.Audio.SoundSpecifier? ShoveVehicleSound;

    /// <summary>
    ///     How fast, in m/s, the hull must close on what it strikes for the sound.
    /// </summary>
    [DataField]
    public float ShoveSoundMinSpeed = 1.5f;

    [DataField]
    public TimeSpan ShoveSoundCooldown = TimeSpan.FromSeconds(0.5);

    /// <summary>
    ///     Each side keeps its own, so not networked: the server plays for everyone but the driver, whose client plays its
    ///         own.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public TimeSpan NextShoveSound;
}

/// <summary>
///     On a loose object a driven hull is pushing. The driver's client predicts it while it does, so the hull isn't
///         stopped on that client by an object it can't move.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true), UnsavedComponent, Access(typeof(KsCrewVehiclePushSystem))]
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

/// <summary>
///     Raised, broadcast, by whatever moves a body along with a driven hull other than by pushing it (the run-over
///         controller carrying a mob): it is then marked as the hull's, and predicted with it on its driver's client.
/// </summary>
[ByRefEvent]
public readonly record struct KsCrewVehicleMovesBodyEvent(EntityUid Hull, EntityUid Body);