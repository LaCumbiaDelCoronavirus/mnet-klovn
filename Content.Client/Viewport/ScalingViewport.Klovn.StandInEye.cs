using System.Numerics;
using Robust.Client.Graphics;
using Robust.Shared.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.Client.Viewport
{
    /// <summary>
    ///     The stand-in eye that every extra pass into the main viewport is drawn through, and the pass itself:
    ///         the z-level stack, the gaps between z-levels, their light captures, and the crew-vehicle backdrop.
    /// </summary>
    /// <remarks>
    ///     These all draw into the viewport's own render target, one after another, without clearing it between
    ///         passes, so the target ends up holding every pass composited back to front. The portal pre-pass is
    ///         not one of them: it renders into viewports of its own, through its own eye.
    /// </remarks>
    public sealed partial class ScalingViewport
    {
        /// <summary>
        ///     A stand-in for the viewer's own eye, re-aimed by <see cref="AimStandInEye"/> before every pass that
        ///         uses it. Nothing may rely on what an earlier pass left in it.
        /// </summary>
        private readonly Robust.Shared.Graphics.Eye _standInEye = new Robust.Shared.Graphics.Eye()
        {
            DrawFov = false,
            DrawLight = false
        };

        /// <summary>
        ///     Sets every field of the stand-in eye a pass can depend on, so no pass inherits another's.
        /// </summary>
        /// <param name="drawFov">
        ///     The FOV is cast from the eye's own position, so this is only ever true for a pass of the map the
        ///         viewer is actually standing on, seen from where they stand.
        /// </param>
        private IEye AimStandInEye(
            MapCoordinates position,
            Vector2 offset,
            Angle rotation,
            Vector2 scale,
            bool drawFov,
            bool drawLight)
        {
            _standInEye.Position = position;
            _standInEye.Offset = offset;
            _standInEye.Rotation = rotation;
            _standInEye.Scale = scale;
            _standInEye.DrawFov = drawFov;
            _standInEye.DrawLight = drawLight;
            return _standInEye;
        }

        /// <summary>
        ///     Renders one pass into the viewport's target through an eye.
        /// </summary>
        /// <param name="clearColor">
        ///     Null draws over whatever earlier passes left in the target, which is how passes are composited.
        ///         The viewport keeps it, so whoever ran the last pass puts <see cref="Color.Black"/> back.
        /// </param>
        private void RenderPass(IEye eye, Color? clearColor)
        {
            _viewport!.ClearColor = clearColor;
            _viewport.Eye = eye;
            _viewport.Render();
        }
    }
}
