using Content.Shared._KS14.SpriteGreyscaleConfig;
using Robust.Client.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Client._KS14.SpriteGreyscaleConfig;

/*
    Greyscale configs are based off of the ones /tg/station has and are used
        to solve the same problem: having alot of the same icon states, but many
        different variations needed (colors, etc.).
*/

/// <inheritdoc/>
public sealed class SpriteGreyscaleConfigSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly SpriteSystem _spriteSystem = default!;

    private const int MaxIterations = 512;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SpriteGreyscaleConfigComponent, ComponentStartup>(OnStartup);
    }

    private void OnStartup(Entity<SpriteGreyscaleConfigComponent> entity, ref ComponentStartup args)
    {
        if (!_prototypeManager.TryIndex(entity.Comp.GreyscaleConfigId, out var configPrototype))
        {
            Log.Error($"Entity {ToPrettyString(entity)} specified a greyscale config prototype with invalid ID: {entity.Comp.GreyscaleConfigId}");
            return;
        }

        // throw if no spritecomponent because it's required
        var spriteComponent = Comp<SpriteComponent>(entity);
        var colorMap = entity.Comp.Colors;

        var iterations = 0;
        foreach (var datum in configPrototype.Data)
            AddData((entity, spriteComponent), datum, colorMap, ref iterations);
    }

    private void AddData(Entity<SpriteComponent> entity, GreyscaleConfigDatum data, Dictionary<string, Color> colorMap, ref int iterations)
    {
        if (++iterations == MaxIterations)
        {
            var exception = new InvalidOperationException($"Entity {ToPrettyString(entity)} hit maximum number of iterations ({MaxIterations}). This occurs from a GreyscaleConfigPresetDatum referring to another GreyscaleConfigPresetDatum that eventually references the former.");

            Log.Fatal(exception.Message);
            throw exception;
        }

        switch (data)
        {
            case GreyscaleConfigSpriteDatum spriteDatum:
                var index = _spriteSystem.AddLayer(entity!, spriteDatum.LayerData, null);

                if (spriteDatum.ConfigMapping is not { } configMapping)
                    return;

                if (colorMap.TryGetValue(configMapping, out var mappedColor))
                {
                    if (spriteDatum.LayerData.Color is { } configColor)
                        _spriteSystem.LayerSetColor(entity!, index, configColor * mappedColor);
                    else
                        _spriteSystem.LayerSetColor(entity!, index, mappedColor);
                }
                return;
            case GreyscaleConfigPresetDatum presetDatum:
                if (!_prototypeManager.TryIndex(presetDatum.PresetId, out var presetPrototype))
                {
                    Log.Error($"A preset datum inside entity {ToPrettyString(entity)} specified a greyscale config prototype with invalid ID: {presetDatum.PresetId}");
                    return;
                }

                foreach (var innerDatum in presetPrototype.Data)
                    AddData(entity, innerDatum, colorMap, ref iterations);

                return;
        }
    }
}
