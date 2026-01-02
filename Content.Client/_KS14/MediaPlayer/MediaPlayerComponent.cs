using Content.Shared._KS14.MediaPlayer;
using Robust.Client.Graphics;

namespace Content.Client._KS14.MediaPlayer;

/// <summary>
///     Marks an entity as a media-player.
/// </summary>
[RegisterComponent]
public sealed partial class MediaPlayerComponent : SharedMediaPlayerComponent
{
    /// <summary>
    ///     The current playing video image-data.
    /// </summary>
    public OwnedTexture Texture = default;

    /// <summary>
    ///     Index (from 0) of the currently playing frame.
    ///         This value is zero if no video is playing.
    /// </summary>
    public int CurrentFrame = 0;
}
