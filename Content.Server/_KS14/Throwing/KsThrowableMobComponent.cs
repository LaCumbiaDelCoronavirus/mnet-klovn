using Content.Shared._KS14.Throwing;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Throwing;

/// <summary>
///     A mob that big enough forces send flying: an explosion, a vehicle driven into it. Stock throws pass mobs by, as
///         they move only loose (dynamic) bodies. How far and how fast depends on the force, and whatever the mob then
///         slams into knocks it over and hurts it, as it does someone thrown out of a grab (<see cref="KsThrownSlamSystem"/>).
/// </summary>
[RegisterComponent, Access(typeof(KsMobThrowSystem))]
public sealed partial class KsThrowableMobComponent : Component
{
    /// <summary>
    ///     The weakest force that lifts the mob at all. An explosion's throw force on a tile is ten times the square root
    ///         of its intensity there, so 15 is an intensity of 2.25.
    /// </summary>
    [DataField]
    public float MinThrowForce = 15f;

    /// <summary>
    ///     Throw speed, in metres per second, for each point of force.
    /// </summary>
    [DataField]
    public float SpeedPerForce = 0.4f;

    [DataField]
    public float MaxSpeed = 20f;

    /// <summary>
    ///     How far the mob is thrown, in metres, for each point of force.
    /// </summary>
    [DataField]
    public float DistancePerForce = 0.15f;

    [DataField]
    public float MaxDistance = 8f;

    /// <summary>
    ///     How long the throw takes the mob off its feet for. Null leaves it standing as it flies.
    /// </summary>
    [DataField]
    public TimeSpan? KnockdownTime = TimeSpan.FromSeconds(2);

    /// <summary>
    ///     What happens when the mob slams into something.
    /// </summary>
    [DataField]
    public ProtoId<KsThrownSlamPrototype> Slam = "KsThrownSlam";
}
