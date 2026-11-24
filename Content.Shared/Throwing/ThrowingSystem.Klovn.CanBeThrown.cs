using Content.Shared._KS14.Throwing;

namespace Content.Shared.Throwing;

public sealed partial class ThrowingSystem
{
    /// <summary>
    ///     Whether <paramref name="uid"/> lets itself be thrown, per <see cref="KsCanBeThrownEvent"/>.
    /// </summary>
    public bool KsCanBeThrown(EntityUid uid, EntityUid? user = null)
    {
        var ev = new KsCanBeThrownEvent(user);
        RaiseLocalEvent(uid, ref ev);
        return !ev.Cancelled;
    }
}
