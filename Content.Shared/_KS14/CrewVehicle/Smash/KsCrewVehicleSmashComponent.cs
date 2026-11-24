using Content.Shared.Damage;
using Content.Shared.Whitelist;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._KS14.CrewVehicle.Smash;

/// <summary>
///     A crew vehicle that, fast enough, breaks through light obstacles and crashes into walls. Slower, either is an
///         ordinary bump.
/// </summary>
/// <remarks>
///     Which obstacles are which is the vehicle's to say, as whitelists, so a light car and a tank can differ and no
///         obstacle's prototype needs to know about vehicles. Anything hard on neither list is just collided with.
/// </remarks>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
[Access(typeof(KsCrewVehicleSmashSystem))]
public sealed partial class KsCrewVehicleSmashComponent : Component
{
    /// <summary>
    ///     The hull's hard body, whose collisions count.
    /// </summary>
    [DataField]
    public string Fixture = "fix1";

    /// <summary>
    ///     How fast, in m/s, the hull must be moving into an obstacle to smash it or crash.
    /// </summary>
    [DataField]
    public float MinSpeed = 3.5f;

    /// <summary>
    ///     What the hull breaks through.
    /// </summary>
    [DataField]
    public EntityWhitelist? Smashable;

    /// <summary>
    ///     What the hull crashes into: damaged and stopped dead, it stays put.
    /// </summary>
    [DataField]
    public EntityWhitelist? Walls;

    /// <summary>
    ///     Dealt to a smashable obstacle: enough to break it through its own <c>Destructible</c>.
    /// </summary>
    [DataField(required: true)]
    public DamageSpecifier SmashDamage = default!;

    /// <summary>
    ///     The fraction of its speed the hull keeps on smashing through.
    /// </summary>
    [DataField]
    public float SmashSpeedRetained = 0.6f;

    [DataField]
    public DamageSpecifier? SmashSelfDamage;

    [DataField]
    public DamageSpecifier? WallDamage;

    [DataField]
    public DamageSpecifier? WallSelfDamage;

    /// <summary>
    ///     The hull damages itself at most once per this, so grinding along a row of obstacles doesn't shred it.
    /// </summary>
    [DataField]
    public TimeSpan SelfDamageCooldown = TimeSpan.FromSeconds(1);

    [DataField]
    public SoundSpecifier? SmashSound;

    [DataField]
    public SoundSpecifier? CrashSound;

    /// <summary>
    ///     When the hull may damage itself again.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField, AutoPausedField]
    public TimeSpan NextSelfDamage;
}

/// <summary>
///     Raised on a crew vehicle that crashed into a wall, after the crash. A notification.
/// </summary>
/// <param name="Wall">What it crashed into.</param>
/// <param name="ImpactSpeed">How fast, in m/s, it was going into it.</param>
[ByRefEvent]
public readonly record struct KsCrewVehicleCrashedEvent(EntityUid Wall, float ImpactSpeed);
