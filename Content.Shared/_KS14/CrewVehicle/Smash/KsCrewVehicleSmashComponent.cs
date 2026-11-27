using Content.Shared.Damage;
using Content.Shared.Whitelist;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.GameStates;

namespace Content.Shared._KS14.CrewVehicle.Smash;

/// <summary>
///     A crew vehicle that, fast enough, breaks through whatever hard thing is in its way, or crashes into it. Slower,
///         either is an ordinary bump.
/// </summary>
/// <remarks>
///     Anything hard that isn't loose (loose bodies are pushed, mobs run over) takes <see cref="SmashDamage"/>: if that
///         breaks it, the hull smashes through; if not, it is crashed into, and a sturdy thing takes several hits. Only
///         <see cref="Walls"/> are told apart, being crashed into at once, for <see cref="WallDamage"/>.
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
    ///     What the hull crashes into: damaged and stopped dead, it stays put.
    /// </summary>
    [DataField]
    public EntityWhitelist? Walls;

    /// <summary>
    ///     Dealt to every hard obstacle that isn't a wall each time the hull hits it fast enough: it breaks, through its
    ///         own <c>Destructible</c>, once that adds up to enough.
    /// </summary>
    [DataField(required: true)]
    public DamageSpecifier SmashDamage = default!;

    /// <summary>
    ///     The fraction of its speed the hull keeps on smashing through.
    /// </summary>
    [DataField]
    public float SmashSpeedRetained = 0.6f;

    /// <summary>
    ///     How long the hull passes through what it smashed, should it still stand, before it is solid again.
    /// </summary>
    [DataField]
    public TimeSpan SmashedPassTime = TimeSpan.FromSeconds(1);

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
    ///     A one-shot effect spawned where the hull smashes through something.
    /// </summary>
    [DataField]
    public EntProtoId? SmashEffect;

    /// <summary>
    ///     A one-shot effect spawned where the hull crashes into a wall.
    /// </summary>
    [DataField]
    public EntProtoId? CrashEffect;

    /// <summary>
    ///     When the hull may damage itself again.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField, AutoPausedField]
    public TimeSpan NextSelfDamage;
}

/// <summary>
///     Raised on a crew vehicle that crashed into something, after the crash. A notification.
/// </summary>
/// <param name="Obstacle">What it crashed into.</param>
/// <param name="ImpactSpeed">How fast, in m/s, it was going into it.</param>
[ByRefEvent]
public readonly record struct KsCrewVehicleCrashedEvent(EntityUid Obstacle, float ImpactSpeed);
