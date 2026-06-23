using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Validation;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Serialization.TypeSerializers.Interfaces;
using Robust.Shared.Utility;

namespace Content.Shared._KS14.SpriteGreyscaleConfig;

// Its basically SoundSpecifier

[TypeSerializer]
public sealed class GreyscaleConfigTypeSerializer : ITypeReader<GreyscaleConfigDatum, MappingDataNode>
{
    private static Type GetType(MappingDataNode node)
    {
        if (node.Has(GreyscaleConfigSpriteDatum.Node))
            return typeof(GreyscaleConfigSpriteDatum);

        return typeof(GreyscaleConfigPresetDatum);
    }

    public GreyscaleConfigDatum Read(ISerializationManager serializationManager, MappingDataNode node,
        IDependencyCollection dependencies, SerializationHookContext hookCtx, ISerializationContext? context = null,
        ISerializationManager.InstantiationDelegate<GreyscaleConfigDatum>? instanceProvider = null)
    {
        var type = GetType(node);
        return (GreyscaleConfigDatum)serializationManager.Read(type, node, hookCtx, context)!;
    }

    public ValidationNode Validate(ISerializationManager serializationManager, MappingDataNode node,
        IDependencyCollection dependencies, ISerializationContext? context = null)
    {
        if (node.Has(GreyscaleConfigPresetDatum.Node) && node.Has(GreyscaleConfigSpriteDatum.Node))
            return new ErrorNode(node, "You can only specify either GreyscaleConfigPresetDatum or GreyscaleConfigSpriteDatum, not both.");

        if (!node.Has(GreyscaleConfigSpriteDatum.Node) && !node.Has(GreyscaleConfigPresetDatum.Node))
            return new ErrorNode(node, "You must specify either GreyscaleConfigPresetDatum or GreyscaleConfigSpriteDatum, but not both.");

        return serializationManager.ValidateNode(GetType(node), node, context);
    }

    public ValidationNode Validate(ISerializationManager serializationManager, ValueDataNode node,
        IDependencyCollection dependencies, ISerializationContext? context = null)
    {
        if (serializationManager.ValidateNode<ResPath>(node, context) is not ErrorNode)
            return new ValidatedValueNode(node);

        return new ErrorNode(node, "SoundSpecifier value is not a valid resource path!");
    }
}
