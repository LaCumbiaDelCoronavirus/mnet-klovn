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
    ///     Dealt once to a standing mob run over.
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
