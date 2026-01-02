using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Shared._KS14.MediaPlayer;

/// <summary>
///     Marks an entity as a media-player.
///         These are very resource intensive.
/// </summary>
[NetworkedComponent]
public abstract partial class SharedMediaPlayerComponent : Component
{
    /// <summary>
    ///     Frames per second.
    /// </summary>
    public const int Framerate = 5;

    /// <summary>
    ///     Length of one frame in milliseconds.
    /// </summary>
    public const double FrameMillisecondLength = 1000d / Framerate;

    /// <summary>
    ///     Number of frames in one
    ///         chunk of video data.
    /// </summary>
    public const int ChunkLength = Framerate * 15; // 15sec of video data per chunk; ~5.5 kB at 30fps

    public const int FramePixelLength = 64;

    /// <summary>
    ///     Total number of pixels in 1(one) frame.
    /// </summary>
    public const int TotalFramePixels = FramePixelLength * FramePixelLength;

    /// <summary>
    ///     Size of the screen playing the video.
    /// </summary>
    public static readonly Vector2i ScreenSize = new(FramePixelLength, FramePixelLength);

    /// <summary>
    ///     Whether a video is currently playing. Networked.
    /// </summary>
    public bool IsPlaying = false;

    /// <summary>
    ///     Length of the currently playing video. Networked.
    ///         This value is undetermined if no video is playing.
    /// </summary>
    public TimeSpan PlayingVideoLength = TimeSpan.Zero;

    /// <summary>
    ///     Simulation-time at which the currently-playing video started playing at.
    ///         Networked. This value is undetermined if no video is playing.
    /// </summary>
    public TimeSpan VideoStartTime = TimeSpan.Zero;

    #region Chunk data

    /// <summary>
    ///     Data that is currently being played.
    /// </summary>
    public Stack<MediaChunkData> Chunks = new();

    #endregion
}

[Serializable, NetSerializable]
public enum MediaPlayerLayers : byte { Media }


/// <summary>
///     Holds data about a chunk of media data.
/// </summary>
/// <param name="Pixels">Pixels of every frame ever, to play.</param>
/// <param name="StartFrame">Frame at which this chunk should start playing at.</param>
[Serializable, NetSerializable]
public record struct MediaChunkData(Rgb24[] Pixels, int StartFrame);

[Serializable, NetSerializable]
public sealed class MediaPlayerComponentState : ComponentState
{
    public bool IsPlaying = false;

    public TimeSpan PlayingVideoLength = TimeSpan.Zero;

    public TimeSpan VideoStartTime = TimeSpan.Zero;

    /// <summary>
    ///     If not-null, this will be
    /// </summary>
    public Rgb24[]? CurrentChunkData = null;
}
