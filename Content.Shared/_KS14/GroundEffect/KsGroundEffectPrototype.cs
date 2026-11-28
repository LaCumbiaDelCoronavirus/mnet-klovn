using Content.Shared._KS14.Particles;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.GroundEffect;

/// <summary>
///     Something the ground throws up when a wheel, a track or a foot crosses it fast enough: snow, dust, leaves, a
///         splash. A tile names one with <c>ksGroundEffect</c>; a puddle on a tile is <c>KsGroundEffectPuddle</c>,
///         whatever the tile. The client feeds the particles where each <see cref="KsGroundEffectEmitterComponent"/>
///         contact point moved since the last frame, so the count follows the distance covered, not the frame rate.
/// </summary>
[Prototype]
public sealed partial class KsGroundEffectPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    ///     The particles, a <see cref="KsParticleSpace.World"/> prototype with no spawning of its own.
    /// </summary>
    [DataField(required: true)]
    public ProtoId<KsParticlesPrototype> Particles;

    /// <summary>
    ///     Particles per metre a contact point travels at <see cref="FullSpeed"/> and above.
    /// </summary>
    [DataField]
    public float PerMetre = 3f;

    /// <summary>
    ///     Metres per second below which a contact point throws nothing up: a nudged or rolling-to-a-stop hull.
    /// </summary>
    [DataField]
    public float MinSpeed = 0.75f;

    /// <summary>
    ///     Metres per second at which <see cref="PerMetre"/>, and the throw's speed, reach their full value.
    /// </summary>
    [DataField]
    public float FullSpeed = 6f;

    /// <summary>
    ///     The fraction of <see cref="PerMetre"/> thrown up at <see cref="MinSpeed"/>. Between the two speeds it rises
    ///         smoothly, so particles per second grow faster than the speed: little at a crawl, a plume flat out.
    /// </summary>
    [DataField]
    public float SlowFactor = 0.3f;

    /// <summary>
    ///     The fraction of the contact point's velocity each particle is given, backwards.
    /// </summary>
    [DataField]
    public float Throw = 0.25f;
}
