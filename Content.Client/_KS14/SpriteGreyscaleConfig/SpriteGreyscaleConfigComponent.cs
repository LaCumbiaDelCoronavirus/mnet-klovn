using Content.Shared._KS14.SpriteGreyscaleConfig;
using Robust.Shared.Prototypes;

namespace Content.Client._KS14.SpriteGreyscaleConfig;

/// <summary>
///     Used for generating sprite data while not needing to spam yaml everywhere.
/// </summary>
[RegisterComponent]
[ComponentProtoName("GreyscaleConfig")]
public sealed partial class SpriteGreyscaleConfigComponent : Component
{
    [DataField("id", required: true)]
    public ProtoId<SpriteGreyscaleConfigPrototype> GreyscaleConfigId;

    /// <summary>
    ///     Colors to modulate (multiply, basically) each layer
    ///         with the same key with. Can have multiple such layers.
    /// </summary>
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public Dictionary<string, Color> Colors = [];
}
