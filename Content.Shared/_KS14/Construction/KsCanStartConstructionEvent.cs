using Content.Shared.Construction.Prototypes;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Construction;

/// <summary>
///     Asks whether <see cref="User"/> may start building <see cref="Prototype"/> at <see cref="Location"/>, from a
///         construction ghost. Broadcast by the server before it takes the materials.
/// </summary>
/// <remarks>
///     Pure: handlers only set <see cref="Cancelled"/> and <see cref="Message"/>.
/// </remarks>
[ByRefEvent]
public record struct KsCanStartConstructionEvent(
    EntityUid User,
    EntityCoordinates Location,
    ProtoId<ConstructionPrototype> Prototype)
{
    public bool Cancelled;

    /// <summary>
    ///     Shown to the user when <see cref="Cancelled"/>; nothing when null.
    /// </summary>
    public LocId? Message;
}
