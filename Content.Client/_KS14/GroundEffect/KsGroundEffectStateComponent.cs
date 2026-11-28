using System.Numerics;
using Content.Shared._KS14.GroundEffect;
using Robust.Shared.Map;

namespace Content.Client._KS14.GroundEffect;

/// <summary>
///     Where each contact point of a <see cref="KsGroundEffectEmitterComponent"/> was last frame, how fast it goes and
///         what it is on. One per contact point, in the parent's (the grid's) frame.
/// </summary>
public struct KsGroundContact
{
    public Vector2 PrevLocal;

    /// <summary>
    ///     Metres per second, smoothed over a few frames.
    /// </summary>
    public float Speed;

    /// <summary>
    ///     Fractional particles owed.
    /// </summary>
    public float Accumulator;

    /// <summary>
    ///     Seconds until <see cref="Effect"/> is looked up again on the same tile, for a puddle that came or went.
    /// </summary>
    public float RetileTimer;

    public Vector2i Tile;

    /// <summary>
    ///     Index into the system's effects, or -1 for none.
    /// </summary>
    public short Effect;

    public Color Tint;

    public bool Valid;
}

/// <summary>
///     The client's running state for a <see cref="KsGroundEffectEmitterComponent"/>: added beside it, arrays sized
///         once, never networked or saved.
/// </summary>
[RegisterComponent]
[Access(typeof(KsGroundEffectSystem))]
public sealed partial class KsGroundEffectStateComponent : Component
{
    public KsGroundContact[] Contacts = Array.Empty<KsGroundContact>();

    /// <summary>
    ///     The particle emitter for each effect this entity has thrown up, by effect index, spawned the first time.
    /// </summary>
    public EntityUid?[] Emitters = Array.Empty<EntityUid?>();

    /// <summary>
    ///     Seconds since each emitter was last fed.
    /// </summary>
    public float[] IdleTime = Array.Empty<float>();

    [ViewVariables]
    public int EmitterCount;

    [ViewVariables]
    public MapId PrevMap = MapId.Nullspace;

    [ViewVariables]
    public EntityUid PrevParent;

    [ViewVariables]
    public Vector2 PrevLocalPosition;

    [ViewVariables]
    public Angle PrevLocalRotation;

    /// <summary>
    ///     Seconds since the entity last moved or turned. A frame with no movement between two network states is not a
    ///         stop: the next movement is spread over the time since.
    /// </summary>
    [ViewVariables]
    public float SinceMoved;

    /// <summary>
    ///     Whether <see cref="PrevLocalPosition"/> and the contacts hold last frame's values.
    /// </summary>
    [ViewVariables]
    public bool Valid;

    /// <summary>
    ///     Whether the contacts' speeds were zeroed after standing still.
    /// </summary>
    [ViewVariables]
    public bool Settled;
}
