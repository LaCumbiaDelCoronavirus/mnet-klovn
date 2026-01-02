using System.Diagnostics.CodeAnalysis;
using Content.Shared._KS14.MediaPlayer;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.GameStates;
using Robust.Shared.Utility;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Client._KS14.MediaPlayer;

/// <inheritdoc/>
public sealed class MediaPlayerSystem : SharedMediaPlayerSystem
{
    [Dependency] private readonly IClyde _clyde = default!;
    [Dependency] private readonly SpriteSystem _spriteSystem = default!;

    private static readonly Rgba32[] EmptyBuffer = new Rgba32[SharedMediaPlayerComponent.TotalFramePixels];

    private static EntityQuery<SpriteComponent> _spriteQuery = default;

    public override void Initialize()
    {
        base.Initialize();
        _spriteQuery = GetEntityQuery<SpriteComponent>();

        //SubscribeLocalEvent<MediaPlayerComponent, ComponentGetState>(OnMediaPlayerGetState);

        SubscribeLocalEvent<MediaPlayerComponent, ComponentInit>(OnMediaPlayerInit);
        SubscribeLocalEvent<MediaPlayerComponent, ComponentStartup>(OnMediaPlayerStartup);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var mediaPlayerEqe = EntityQueryEnumerator<MediaPlayerComponent>();
        while (mediaPlayerEqe.MoveNext(out var uid, out var mediaPlayerComponent))
        {
            if (!mediaPlayerComponent.IsPlaying)
                continue;

            var currentFrame = (int)((GameTiming.CurTime - mediaPlayerComponent.VideoStartTime).TotalMilliseconds / SharedMediaPlayerComponent.FrameMillisecondLength);
            if (currentFrame == mediaPlayerComponent.CurrentFrame)
                continue;

            mediaPlayerComponent.Texture.SetSubImage(
                Vector2i.Zero,
                SharedMediaPlayerComponent.ScreenSize,
                EmptyBuffer.AsSpan(currentFrame * SharedMediaPlayerComponent.TotalFramePixels, SharedMediaPlayerComponent.TotalFramePixels)
            );
        }
    }

    private bool TryAndAssertCompForPlayer<T>(EntityUid uid, [MaybeNullWhen(false)] out T component) where T : IComponent
    {
        DebugTools.Assert(HasComp<T>(uid), $"Media player is missing '{typeof(T).Name}' during initialisation of MediaPlayerComponent, but one was expected.");
        return TryComp(uid, out component);
    }

    private void OnMediaPlayerInit(Entity<MediaPlayerComponent> entity, ref ComponentInit args)
    {
        entity.Comp.Texture = _clyde.CreateBlankTexture<Rgb24>(SharedMediaPlayerComponent.ScreenSize);
        entity.Comp.Texture.SetSubImage(Vector2i.Zero, SharedMediaPlayerComponent.ScreenSize, new ReadOnlySpan<Rgba32>(EmptyBuffer));
    }

    private void OnMediaPlayerStartup(Entity<MediaPlayerComponent> entity, ref ComponentStartup args)
    {
        DebugTools.Assert(HasComp<SpriteComponent>(entity.Owner), $"Media player is missing 'SpriteComponent' during initialisation of MediaPlayerComponent, but one was expected.");
        _spriteSystem.LayerSetTexture(entity.Owner, MediaPlayerLayers.Media, entity.Comp.Texture);
    }
}
