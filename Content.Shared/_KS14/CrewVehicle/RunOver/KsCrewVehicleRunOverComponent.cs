using Robust.Shared.GameStates;
using Content.Shared.Damage;
using Robust.Shared.Audio;

namespace Content.Shared._KS14.CrewVehicle.RunOver;

/// <summary>
///     A crew vehicle that runs over a standing mob it drives into fast enough, and is stopped by one it meets slower.
/// </summary>
/// <remarks>
///     A driven hull is a kinematic controller and so is a mob, and physics never lets two of those collide: everything
///         here is through the hull's non-hard <see cref="Fixture"/>. A parked hull is a Dynamic body, which mobs
///         collide with as usual.
/// </remarks>
[RegisterComponent, Access(typeof(KsCrewVehicleRunOverSystem), typeof(KsCrewVehicleRunOverController))]
public sealed partial class KsCrewVehicleRunOverComponent : Component
{
    /// <summary>
    ///     The hull's non-hard fixture, a little larger than its body, that finds the mobs it touches.
    /// </summary>
    [DataField]
    public string Fixture = "contact";

    /// <summary>
    ///     How fast, in m/s, the hull must close on a mob to run it over. Slower, the mob stops it.
    /// </summary>
    [DataField]
    public float MinSpeed = 3f;

    /// <summary>
    ///     How fast, in m/s, the hull must close on a standing mob to throw it along its way rather than go over it.
    ///         Null: it never does. A mob that can't be thrown (no <c>KsThrowableMob</c>) is run over instead.
    /// </summary>
    [DataField]
    public float? ThrowSpeed;

    /// <summary>
    ///     The throw's force for each m/s the hull closes at: how far and how fast the mob flies is its own
    ///         <c>KsThrowableMob</c>'s to say from that.
    /// </summary>
    [DataField]
    public float ThrowForcePerSpeed = 5f;

    /// <summary>
    ///     How long a thrown mob is left alone by the hull that threw it, neither carried nor hit again, while it gets clear.
    /// </summary>
    [DataField]
    public TimeSpan StruckIgnoreTime = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     Dealt once to a standing mob run over or thrown.
    /// </summary>
    [DataField(required: true)]
    public DamageSpecifier Damage = default!;

    /// <summary>
    ///     How long a mob run over is knocked down for. It stays down while the hull is still over it.
    /// </summary>
    [DataField]
    public TimeSpan Knockdown = TimeSpan.FromSeconds(3);

    /// <summary>
    ///     A mob is out from under the hull once <see cref="Fixture"/> no longer touches it, or, whatever its possibly
    ///         stale contact says, once it is this far, in m, from the hull's body. More than the fixture reaches.
    /// </summary>
    [DataField]
    public float ReleaseDistance = 1f;

    [DataField]
    public SoundSpecifier? HitSound;
}

/// <summary>
///     On a mob a crew vehicle has just thrown: left alone by hulls until <see cref="Until"/>, while it flies clear.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause, Access(typeof(KsCrewVehicleRunOverSystem))]
public sealed partial class KsCrewVehicleStruckComponent : Component
{
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField, AutoPausedField]
    public TimeSpan Until;
}

/// <summary>
///     Raised, broadcast, when a crew vehicle strikes a standing mob hard enough to throw it, for the server's throw to
///         answer. <see cref="Thrown"/> is false if the mob can't be thrown; it is then run over instead.
/// </summary>
/// <param name="Direction">Which way: the hull's travel.</param>
[ByRefEvent]
public record struct KsCrewVehicleThrowStruckMobEvent(EntityUid Hull, EntityUid Mob, System.Numerics.Vector2 Direction, float Force)
{
    public bool Thrown;
}
