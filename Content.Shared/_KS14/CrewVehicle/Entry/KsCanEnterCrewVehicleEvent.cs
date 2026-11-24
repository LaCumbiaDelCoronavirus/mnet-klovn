namespace Content.Shared._KS14.CrewVehicle.Entry;

/// <summary>
///     Asks whether <see cref="User"/> may get into the crew vehicle it is raised on. Raised when they try, and again
///         when their do-after ends. Not raised for ghosts.
/// </summary>
/// <remarks>
///     Pure: handlers only set <see cref="Cancelled"/> and <see cref="Message"/>.
/// </remarks>
[ByRefEvent]
public record struct KsCanEnterCrewVehicleEvent(EntityUid User)
{
    public bool Cancelled;

    /// <summary>
    ///     Shown to the user when <see cref="Cancelled"/>; nothing when null.
    /// </summary>
    public LocId? Message;
}
