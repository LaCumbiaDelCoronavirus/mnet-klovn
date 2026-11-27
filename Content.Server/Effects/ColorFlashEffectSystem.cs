using Content.Shared.Effects;
using Robust.Shared.Player;

namespace Content.Server.Effects;

public sealed partial class ColorFlashEffectSystem : SharedColorFlashEffectSystem
{
    public override void RaiseEffect(Color color, List<EntityUid> entities, Filter filter)
    {
        RaiseNetworkEvent(new ColorFlashEffectEvent(color, GetNetEntityList(entities)), Content.Shared._KS14.Player.KsViewFilter.AddRemoteViewersOf(filter, entities, EntityManager) /* KS14: filter -> plus whoever sees them from another map */);
    }
}
