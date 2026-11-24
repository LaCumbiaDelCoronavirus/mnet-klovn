using System.Numerics;

namespace Content.Shared._KS14.Movement;

/// <summary>
///     Raised on a mover entity to update wish-dir if necessary.
/// </summary>
/// <param name="FrameTime">
///     The length of this movement step, in seconds: a physics substep, which can be shorter than a tick.
/// </param>
[ByRefEvent]
public record struct UpdateWishDirEvent(Vector2 WishDir, float FrameTime);
