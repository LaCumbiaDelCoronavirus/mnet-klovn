using System.Linq;
using System.Numerics;
using Content.Client.Graphics;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._KS14.Rift;

public sealed class KsRiftOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> StencilMaskId = "StencilMask";
    private static readonly ProtoId<ShaderPrototype> StencilEqualDrawId = "StencilEqualDraw";

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    [Dependency] private readonly EntityManager _entityManager = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly IClyde _clyde = default!;
    [Dependency] private readonly KsRiftSystem _riftSystem = default!;

    private readonly OverlayResourceCache<CachedResources> _resources = new();
    private List<Entity<MapGridComponent>> _grids = [];

    private readonly ShaderInstance _stencilMaskShader;
    private readonly ShaderInstance _stencilEqualDrawShader;
    private bool _currentlyDrawing = false;

    private Robust.Shared.Graphics.Eye _rEye = new();

    public KsRiftOverlay(IDependencyCollection dependencyCollection)
    {
        dependencyCollection.InjectDependencies(this, oneOff: true);

        _stencilMaskShader = _prototypeManager.Index(StencilMaskId).InstanceUnique();
        _stencilEqualDrawShader = _prototypeManager.Index(StencilEqualDrawId).InstanceUnique();
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (_currentlyDrawing)
            return false;

        if (args.Viewport.Eye == null ||
            !_entityManager.EntityQuery<KsRiftComponent>().Any())
            return false;

        return true;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (args.Viewport.Eye is not { } eye)
            return;

        var viewport = args.Viewport;
        var resources = _resources.GetForViewport(viewport, static _ => new CachedResources());
        var target = viewport.RenderTarget;

        if (resources.RViewport?.Size != target.Size)
        {
            resources.RViewport?.Dispose();
            resources.RViewport = _clyde.CreateViewport(target.Size, "rift-inner-viewport");
            resources.RViewport.Eye = _rEye;
        }
        var rViewport = resources.RViewport!;

        if (resources.StencilTarget?.Texture.Size != target.Size)
        {
            resources.StencilTarget?.Dispose();
            resources.StencilTarget = _clyde.CreateRenderTarget(target.Size, new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb), name: "rift-stencil-target");
        }

        var worldHandle = args.WorldHandle;
        var renderHandle = args.RenderHandle;

        var worldBounds = args.WorldBounds;

        var mapId = args.MapId;

        _grids.Clear();
        _mapManager.FindGridsIntersecting(mapId, worldBounds, ref _grids, approx: true);
        if (_grids.Count == 0)
            return;

        if (_currentlyDrawing)
            throw new InvalidOperationException("f");

        var scale = viewport.RenderScale / (Vector2.One / viewport.RenderTarget.Size / (Vector2)viewport.Size);
        var eyeRotation = args.Viewport.Eye?.Rotation ?? new();

        var transformQuery = _entityManager.TransformQuery;
        var curTime = _gameTiming.CurTime;

        // RIFTS START
        var worldToViewportMatrix = viewport.GetWorldToLocalMatrix();
        _rEye.DrawLight = eye.DrawLight;
        _rEye.DrawFov = eye.DrawFov;
        _rEye.Offset = eye.Offset;
        _rEye.Scale = eye.Scale;

        var rScaleHalf = viewport.RenderScale / (Vector2.One / (viewport.RenderTarget.Size / (Vector2)viewport.Size)) * 0.5f;
        foreach (var ((riftUid, riftComponent, riftTransformComponent), (riftWorldPosition, riftWorldRotation)) in _riftSystem.GetVisibleRiftsEnumerator(eye, rViewport))
        {
            // Deduce eye rotation relative to grid (or map if no grid)
            var localEyeRotation = eye.Rotation - (riftWorldRotation - riftTransformComponent.LocalRotation) + riftComponent.RotationOffset;

            var riftViewportPosition = Vector2.Transform(riftWorldPosition, worldToViewportMatrix);
            var renderScaledBoundingBox = new Box2(riftComponent.BoundingBox.BottomLeft * rScaleHalf, riftComponent.BoundingBox.TopRight * rScaleHalf);

            worldHandle.RenderInRenderTarget(resources.StencilTarget, () =>
            {
                var box2Rotated = new Box2Rotated(renderScaledBoundingBox.Translated(riftViewportPosition), eye.Rotation - localEyeRotation, riftViewportPosition);
                worldHandle.DrawTextureRectRegion(Texture.White, box2Rotated);
            }, Color.Transparent);

            var offset = eye.Position.Position - riftWorldPosition;
            // i don't know how to fix this being wonky so i just do this
            _rEye.Position = new(riftWorldPosition + offset + (-localEyeRotation).RotateVec(riftComponent.RenderOffset) /* offset ts */, eye!.Position.MapId);
            _rEye.Rotation = riftWorldRotation + localEyeRotation;
            // this gets affected by shaders, watch out
            _currentlyDrawing = true;
            rViewport.Render();
            _currentlyDrawing = false;

            worldHandle.UseShader(_stencilMaskShader);
            worldHandle.DrawTextureRect(resources.StencilTarget!.Texture, worldBounds);

            worldHandle.UseShader(_stencilEqualDrawShader);
            worldHandle.DrawTextureRect(rViewport.RenderTarget.Texture, worldBounds);
            worldHandle.UseShader(null); // as viewport.Render gets fucked by shaders we reset shaders here and now
        }
        // RIFTS END

        worldHandle.SetTransform(Matrix3x2.Identity);
    }

    private sealed class CachedResources : IDisposable
    {
        public IClydeViewport? RViewport = null;
        public IRenderTexture? StencilTarget = null;

        public void Dispose()
        {
            StencilTarget?.Dispose();
        }
    }
}
