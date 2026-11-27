using Content.Shared._KS14.NPC.Factions;
using Content.Shared.NPC.Components;

namespace Content.Shared.NPC.Systems;

public sealed partial class NpcFactionSystem
{
    /// <summary>
    ///     Raises <see cref="KsNpcFactionsRefreshedEvent"/> on <paramref name="ent"/>, its factions just worked out again.
    /// </summary>
    private void KsRaiseFactionsRefreshed(Entity<NpcFactionMemberComponent> ent)
    {
        var ev = new KsNpcFactionsRefreshedEvent();
        RaiseLocalEvent(ent, ref ev);
    }
}
