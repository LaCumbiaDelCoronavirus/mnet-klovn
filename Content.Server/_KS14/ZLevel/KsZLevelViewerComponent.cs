using Robust.Shared.Player;

namespace Content.Server._KS14.ZLevel;

/// <summary>
///     On anything a player sees from: their attached entity, or an entity their session is view-subscribed to (an
///         eye target, a vehicle's outside viewer, a camera). While it is on a z-level, the levels below it, and above
///         if asked for, are sent to those sessions as they are to a mob standing there.
/// </summary>
[RegisterComponent]
[Access(typeof(KsZLevelPvsSystem))]
public sealed partial class KsZLevelViewerComponent : Component
{
    /// <summary>
    ///     The sessions that see from this entity. The component goes when the last one stops.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public HashSet<ICommonSession> Sessions = new();

    /// <summary>
    ///     The entities loading the z-levels below for this player, one a level, the nearest level first. As many as
    ///         there are levels below, up to <see cref="Content.Shared._KS14.CCVar.KsCCVars.ZLevelPvsLevelsBelow"/>.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public List<EntityUid> BelowViewSubscriberUids = [];

    /// <summary>
    ///     The same again for the z-level directly above, which is only ever sent so that light can fall from
    ///         it - nothing up there is drawn.
    /// </summary>
    /// <remarks>
    ///     Kept separate rather than folded into the list because the two have different lifetimes: the ones
    ///         below follow the player around the whole round, and this one appears and disappears with a
    ///         cvar.
    /// </remarks>
    [ViewVariables(VVAccess.ReadOnly)]
    public EntityUid AboveViewSubscriberUid;

    [ViewVariables(VVAccess.ReadOnly)]
    public bool AboveActive;
}
