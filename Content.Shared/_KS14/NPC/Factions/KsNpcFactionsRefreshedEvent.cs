using Content.Shared.NPC.Components;

namespace Content.Shared._KS14.NPC.Factions;

/// <summary>
///     Raised on an entity whenever its <see cref="NpcFactionMemberComponent"/>'s factions are worked out again: on
///         startup, on every change through <c>NpcFactionSystem</c>, and on a reload of the faction prototypes.
/// </summary>
/// <remarks>
///     It can be raised from inside a walk over every faction member (the reload), so handlers must not add or remove
///         components.
/// </remarks>
[ByRefEvent]
public readonly record struct KsNpcFactionsRefreshedEvent;
