using Robust.Shared.GameStates;
using Robust.Shared.Timing;
using DependencyAttribute = Robust.Shared.IoC.DependencyAttribute;

namespace Content.Shared._KS14.MediaPlayer;

/// <inheritdoc/>
public abstract class SharedMediaPlayerSystem : EntitySystem
{
    [Dependency] protected readonly IGameTiming GameTiming = default!;

    // Entity<T> doesn't support component inheritance so it isn't used here
    protected static void OnMediaPlayerGetState(EntityUid uid, SharedMediaPlayerComponent component, ref ComponentGetState args)
    {
        args.State = new MediaPlayerComponentState()
        {
            IsPlaying = component.IsPlaying,
            PlayingVideoLength = component.PlayingVideoLength,
            VideoStartTime = component.VideoStartTime
        };
    }
}
