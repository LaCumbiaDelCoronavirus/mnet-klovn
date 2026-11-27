using Content.Shared._KS14.Player;
using Content.Shared._KS14.PopupLocalization;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Shared.Popups;

public abstract partial class SharedPopupSystem
{
    [Dependency] private ISharedPlayerManager _ksPlayerManager = default!;
    [Dependency] private IConfigurationManager _ksConfigurationManager = default!;

    /// <summary>
    ///     Everyone who can see <paramref name="uid"/>, from wherever they look: someone in a vehicle's view seat sees
    ///         what happens outside although their body is inside (<see cref="KsViewFilter"/>).
    /// </summary>
    protected Filter ViewersOf(EntityUid uid)
        => KsViewFilter.View(uid, EntityManager, playerManager: _ksPlayerManager, configurationManager: _ksConfigurationManager);

    /// <inheritdoc cref="ViewersOf(EntityUid)"/>
    protected Filter ViewersOf(EntityCoordinates coordinates)
        => KsViewFilter.View(coordinates, EntityManager, playerManager: _ksPlayerManager, configurationManager: _ksConfigurationManager);

    /// <summary>
    ///     Everyone who can see <paramref name="uid"/> but whoever is attached to it.
    /// </summary>
    protected Filter ViewersExcept(EntityUid uid)
        => KsViewFilter.ViewExcept(uid, EntityManager, playerManager: _ksPlayerManager, configurationManager: _ksConfigurationManager);

    protected abstract void PopupLocalized(KsPopupMessage message, KsPopupKind kind, EntityUid? entity,
        EntityCoordinates coordinates, Filter filter, bool recordReplay, PopupType type, int predictionKey = 0);

    protected virtual Filter RecipientFilter(EntityUid? recipient) => TryComp<ActorComponent>(recipient, out var actor)
        ? Filter.SinglePlayer(actor.PlayerSession) : Filter.Empty();

    public void PopupEntity(KsPopupMessage message, EntityUid uid, PopupType type = PopupType.Small)
        => PopupEntity(message, uid, ViewersOf(uid), true, type);

    public void PopupEntity(KsPopupMessage message, EntityUid uid, EntityUid? recipient, PopupType type = PopupType.Small)
        => PopupEntity(message, uid, RecipientFilter(recipient), false, type);

    public void PopupEntity(KsPopupMessage message, EntityUid uid, ICommonSession recipient, PopupType type = PopupType.Small)
        => PopupEntity(message, uid, Filter.SinglePlayer(recipient), false, type);

    public void PopupEntity(KsPopupMessage message, EntityUid uid, Filter filter, bool recordReplay, PopupType type = PopupType.Small)
        => PopupLocalized(message, KsPopupKind.Entity, uid, default, filter, recordReplay, type);

    public void PopupEntity(KsPopupMessage recipientMessage, KsPopupMessage othersMessage, EntityUid uid,
        EntityUid? recipient, PopupType type = PopupType.Small)
    {
        if (recipient is { } recipientUid)
        {
            PopupEntity(othersMessage, uid, ViewersExcept(recipientUid), true, type);
            PopupEntity(recipientMessage, uid, recipientUid, type);
        }
        else
            PopupEntity(othersMessage, uid, type);
    }

    public void PopupEntity(KsPopupMessage recipientMessage, string? othersMessage, EntityUid uid,
        EntityUid? recipient, PopupType type = PopupType.Small)
    {
        if (recipient is { } recipientUid)
        {
            PopupEntity(othersMessage, uid, ViewersExcept(recipientUid), true, type);
            PopupEntity(recipientMessage, uid, recipientUid, type);
        }
        else
            PopupEntity(othersMessage, uid, type);
    }

    public void PopupCursor(KsPopupMessage message, EntityUid? recipient, PopupType type = PopupType.Small)
        => PopupCursor(message, RecipientFilter(recipient), false, type);

    public void PopupCursor(KsPopupMessage message, ICommonSession recipient, PopupType type = PopupType.Small)
        => PopupCursor(message, Filter.SinglePlayer(recipient), false, type);

    public void PopupCursor(KsPopupMessage message, Filter filter, bool recordReplay, PopupType type = PopupType.Small)
        => PopupLocalized(message, KsPopupKind.Cursor, null, default, filter, recordReplay, type);

    public void PopupCoordinates(KsPopupMessage message, EntityCoordinates coordinates,
        PopupType type = PopupType.Small, int predictionKey = 0)
        => PopupCoordinates(message, coordinates, ViewersOf(coordinates), true, type, predictionKey);

    public void PopupCoordinates(KsPopupMessage message, EntityCoordinates coordinates, EntityUid? recipient,
        PopupType type = PopupType.Small, int predictionKey = 0)
        => PopupCoordinates(message, coordinates, RecipientFilter(recipient), false, type, predictionKey);

    public void PopupCoordinates(KsPopupMessage message, EntityCoordinates coordinates, ICommonSession recipient,
        PopupType type = PopupType.Small, int predictionKey = 0)
        => PopupCoordinates(message, coordinates, Filter.SinglePlayer(recipient), false, type, predictionKey);

    public void PopupCoordinates(KsPopupMessage message, EntityCoordinates coordinates, Filter filter,
        bool recordReplay, PopupType type = PopupType.Small, int predictionKey = 0)
        => PopupLocalized(message, KsPopupKind.Coordinates, null, coordinates, filter, recordReplay, type, predictionKey);

    public void PopupClient(KsPopupMessage message, EntityUid? recipient, PopupType type = PopupType.Small)
    {
        if (recipient is { } uid)
            PopupEntity(message, uid, recipient, type);
    }

    public void PopupClient(KsPopupMessage message, EntityUid uid, EntityUid? recipient, PopupType type = PopupType.Small)
        => PopupEntity(message, uid, recipient, type);

    public void PopupClient(KsPopupMessage message, EntityCoordinates coordinates, EntityUid? recipient, PopupType type = PopupType.Small)
        => PopupCoordinates(message, coordinates, recipient, type);

    public void PopupPredicted(KsPopupMessage message, EntityUid uid, EntityUid? recipient, PopupType type = PopupType.Small)
        => PopupEntity(message, uid, type);

    public void PopupPredicted(KsPopupMessage message, EntityUid uid, EntityUid? recipient, Filter filter,
        bool recordReplay, PopupType type = PopupType.Small)
        => PopupEntity(message, uid, filter, recordReplay, type);

    public void PopupPredicted(KsPopupMessage recipientMessage, KsPopupMessage othersMessage, EntityUid uid,
        EntityUid? recipient, PopupType type = PopupType.Small)
        => PopupEntity(recipientMessage, othersMessage, uid, recipient, type);

    public void PopupPredicted(KsPopupMessage recipientMessage, string? othersMessage, EntityUid uid,
        EntityUid? recipient, PopupType type = PopupType.Small)
        => PopupEntity(recipientMessage, othersMessage, uid, recipient, type);

    public void PopupPredictedCursor(KsPopupMessage message, EntityUid recipient, PopupType type = PopupType.Small)
        => PopupCursor(message, recipient, type);

    public void PopupPredictedCursor(KsPopupMessage message, ICommonSession recipient, PopupType type = PopupType.Small)
        => PopupCursor(message, recipient, type);

    public void PopupPredictedCoordinates(KsPopupMessage message, EntityCoordinates coordinates,
        EntityUid? recipient, PopupType type = PopupType.Small)
        => PopupCoordinates(message, coordinates, type);
}
