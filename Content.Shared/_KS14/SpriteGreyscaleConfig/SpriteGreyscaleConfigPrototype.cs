using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.SpriteGreyscaleConfig;

[Prototype("greyscaleConfig")]
public sealed partial class SpriteGreyscaleConfigPrototype : IPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public List<GreyscaleConfigDatum> Data = [];
}

[ImplicitDataDefinitionForInheritors]
[Serializable, NetSerializable]
public abstract partial class GreyscaleConfigDatum;

[Serializable, NetSerializable]
public sealed partial class GreyscaleConfigSpriteDatum : GreyscaleConfigDatum
{
    public const string Node = "layer";

    [DataField(Node, required: true)]
    public PrototypeLayerData LayerData = default!;

    /// <summary>
    ///     When corresponding with an ID in <see cref="SpriteGreyscaleConfigComponent.Colors"/>,
    ///         this layers color will be modulated by the corresponding color.
    /// </summary>
    [DataField("id")]
    public string? ConfigMapping = null;

    private GreyscaleConfigSpriteDatum() { }
}

[Serializable, NetSerializable]
public sealed partial class GreyscaleConfigPresetDatum : GreyscaleConfigDatum
{
    // for serialisation
    public const string Node = "presetId";

    [DataField(Node, required: true)]
    public ProtoId<SpriteGreyscaleConfigPrototype> PresetId;

    private GreyscaleConfigPresetDatum() { }

    // Cant use primary construction because of serialisation generator shitcode (tbf nophono would want to maintain it anyway)
    public GreyscaleConfigPresetDatum(string presetId)
    {
        PresetId = presetId;
    }
}
