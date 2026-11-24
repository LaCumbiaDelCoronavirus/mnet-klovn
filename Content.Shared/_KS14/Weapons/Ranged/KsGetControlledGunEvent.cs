using Robust.Shared.Map;

namespace Content.Shared._KS14.Weapons.Ranged;

/// <summary>
///     Raised on a player's attached entity to ask which gun their shoot input fires when it is not one they hold: a
///         gun they control from somewhere else, such as a vehicle's mounted weapon from a seat inside it.
/// </summary>
/// <remarks>
///     Get, pure: handlers only fill the event, and leave it alone once a gun is named. Asked by the shoot request on
///         both sides and by the client's fire input, before the gun in hand.
/// </remarks>
[ByRefEvent]
public record struct KsGetControlledGunEvent
{
    /// <summary>
    ///     What fires the shot, which its projectiles ignore and whose doing its damage is. Null: the player.
    /// </summary>
    public EntityUid? Shooter;

    public EntityUid? Gun;

    /// <summary>
    ///     Where the shot leaves from. Null: the shooter's position.
    /// </summary>
    public EntityCoordinates? Muzzle;

    /// <summary>
    ///     How far from <see cref="Muzzle"/> the shot may be aimed. A point beyond it is brought in to it along the same
    ///         bearing. Null: no limit.
    /// </summary>
    public float? MaxRange;
}
