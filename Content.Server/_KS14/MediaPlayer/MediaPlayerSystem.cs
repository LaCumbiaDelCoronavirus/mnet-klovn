using Content.Shared._KS14.MediaPlayer;
using Robust.Shared.GameStates;

namespace Content.Server._KS14.MediaPlayer;

/// <inheritdoc/>
public sealed class MediaPlayerSystem : SharedMediaPlayerSystem
{
    public override void Initialize()
    {
        base.Initialize();

        //SubscribeLocalEvent<MediaPlayerComponent, ComponentGetState>(OnMediaPlayerGetState);
    }

}
