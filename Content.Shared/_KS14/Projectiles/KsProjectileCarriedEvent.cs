namespace Content.Shared._KS14.Projectiles;

/// <summary>
///     Broadcast when a projectile goes on as a fresh entity elsewhere (into a crew vehicle's interior, or out of it), just
///         after <paramref name="Copy"/> is spawned and before <paramref name="Original"/> goes: whatever a system stamped
///         on the round as it was fired, it copies onto the copy here.
/// </summary>
/// <remarks>
///     The copy is not fired by anyone: no <c>PlayerShotProjectileEvent</c> is raised for it, so nothing re-stamps it.
/// </remarks>
[ByRefEvent]
public readonly record struct KsProjectileCarriedEvent(EntityUid Original, EntityUid Copy);
