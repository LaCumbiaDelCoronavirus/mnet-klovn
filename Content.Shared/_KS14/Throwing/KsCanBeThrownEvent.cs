namespace Content.Shared._KS14.Throwing;

/// <summary>
///     Asks whether an entity may be thrown at all, by anyone or anything: a hand, an explosion, a shove. Raised on the
///         entity by <c>ThrowingSystem.TryThrow</c>, once it has a body that could be thrown.
/// </summary>
/// <remarks>
///     Pure: handlers only set <see cref="Cancelled"/>. For things too heavy or too fixed to be thrown that still need a
///         loose physics body.
/// </remarks>
[ByRefEvent]
public record struct KsCanBeThrownEvent(EntityUid? User, bool Cancelled = false);
