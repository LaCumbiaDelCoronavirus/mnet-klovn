using System.Numerics;

namespace Content.Shared._KS14.GroundEffect;

/// <summary>
///     Makes an entity throw up the ground it crosses (<see cref="KsGroundEffectPrototype"/>): snow under a tank's
///         tracks, dust behind a humvee's wheels, a running mob's splash through a puddle. Data only: the client reads
///         it and draws the particles, and nothing is networked. Shared so that server prototypes can carry it.
/// </summary>
[RegisterComponent]
public sealed partial class KsGroundEffectEmitterComponent : Component
{
    /// <summary>
    ///     Where the running gear meets the ground, in the entity's own frame (facing south at rotation 0, so the
    ///         front is local -Y): one point per wheel, the ends of each track, the middle of a mob.
    /// </summary>
    [DataField]
    public List<Vector2> ContactPoints = new() { Vector2.Zero };

    /// <summary>
    ///     Multiplies how many particles every effect throws up for this entity: a heavy tank more than a humvee, a
    ///         mob far less.
    /// </summary>
    [DataField]
    public float Amount = 1f;

    /// <summary>
    ///     Metres per second below which this entity throws up nothing, on top of each effect's own minimum: a mob
    ///         that only kicks up the ground at a run.
    /// </summary>
    [DataField]
    public float MinSpeed;
}
