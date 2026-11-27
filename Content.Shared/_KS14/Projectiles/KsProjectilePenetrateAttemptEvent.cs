using System.Numerics;
using Robust.Shared.Map;

namespace Content.Shared._KS14.Projectiles;

/// <summary>
///     Raised on what a projectile has just penetrated, once per real hit (also on a client's kept copy of its own shot),
///         right before the round flies on. Cancel to stop it here: a handler that does may carry it on elsewhere.
/// </summary>
/// <param name="Projectile">The round going through.</param>
/// <param name="WorldVelocity">Its velocity on its map as it hit.</param>
/// <param name="Point">Where it was as it hit.</param>
[ByRefEvent]
public record struct KsProjectilePenetrateAttemptEvent(EntityUid Projectile, Vector2 WorldVelocity, MapCoordinates Point, bool Cancelled = false);
