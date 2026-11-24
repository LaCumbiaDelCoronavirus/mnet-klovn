using Content.Shared._KS14.Construction;
using Content.Shared._KS14.PopupLocalization;
using Content.Shared.Construction;
using Content.Shared.Construction.Prototypes;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Server.Construction;

public sealed partial class ConstructionSystem
{
    /// <summary>
    ///     Whether a construction ghost may start being built, by <see cref="KsCanStartConstructionEvent"/>. On refusal
    ///         tells the user why and acknowledges the ghost, so their client clears it.
    /// </summary>
    private bool KsCanStartConstruction(EntityUid user,
        EntityCoordinates location,
        ConstructionPrototype constructionPrototype,
        int ack,
        ICommonSession session)
    {
        var ev = new KsCanStartConstructionEvent(user, location, constructionPrototype.ID);
        RaiseLocalEvent(ref ev);
        if (!ev.Cancelled)
            return true;

        if (ev.Message is { } message)
            _popup.PopupEntity(KsPopupMessage.Create(message), user, user);

        RaiseNetworkEvent(new AckStructureConstructionMessage(ack), session);
        return false;
    }
}
