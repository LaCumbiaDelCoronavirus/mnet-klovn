using Content.Shared._KS14.GroundEffect;
using Robust.Shared.Prototypes;

namespace Content.Shared.Maps;

// ContentTileDefinition is already partial upstream, so nothing had to be done to it to hang this here.
public sealed partial class ContentTileDefinition
{
    /// <summary>
    ///     What a wheel, a track or a running foot throws up from this tile, if anything. Set from
    ///         <c>Resources/Prototypes/_KS14/Tiles/Partials/ground_effects.yml</c>, so no upstream tile file is edited.
    /// </summary>
    [DataField]
    public ProtoId<KsGroundEffectPrototype>? KsGroundEffect;
}
