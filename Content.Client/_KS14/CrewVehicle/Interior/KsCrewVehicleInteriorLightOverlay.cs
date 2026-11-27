using System.Numerics;
using Content.Client._KS14.OverlayZIndex;
using Content.Client.Light;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;

namespace Content.Client._KS14.CrewVehicle.Interior;

/// <summary>
///     Bleeds the light falling on the ground outside a crew vehicle into its interior, as if the roof let a share of it
///         through.
/// </summary>
/// <remarks>
///     Drawn into <see cref="BeforeLightTargetOverlay"/>'s enlarged target, under the engine's blur, so it arrives soft.
///         The light map is the one the viewport's backdrop pass left (<see cref="KsCrewVehicleInteriorLightSystem"/>);
///         it covers the visible screen only, so the blur's skirt at the screen edge gets none of it.
///     It ignores where the windows are: the whole cabin is lit alike.
/// </remarks>
public sealed partial class KsCrewVehicleInteriorLightOverlay : Overlay
{
    public override OverlaySpace Space => OverlaySpace.BeforeLighting;

    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;

    /// <summary>
    ///     The z-level light bleed, unmasked: the same "light arriving on top of an ambient" composite.
    /// </summary>
    private static readonly ProtoId<ShaderPrototype> BleedShader = "KsZLevelLightBleed";

    public static readonly int ContentZIndex = BeforeLightingZIndex.CrewVehicleOutsideLight.ToZIndex();

    private KsCrewVehicleInteriorLightSystem? _lightSystem;
    private ShaderInstance? _shader;
    private KsCrewVehicleOutsideLight _outsideLight;

    public KsCrewVehicleInteriorLightOverlay()
    {
        IoCManager.InjectDependencies(this);
        ZIndex = ContentZIndex;
    }

    protected override void DisposeBehavior()
    {
        _shader?.Dispose();
        _shader = null;

        base.DisposeBehavior();
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        _lightSystem ??= _entityManager.System<KsCrewVehicleInteriorLightSystem>();

        return args.Viewport.Eye != null &&
               _lightSystem.TryGetOutsideLight(args.Viewport, args.MapUid, out _outsideLight);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        // Copied out of the ref struct before the lambda below captures anything.
        var viewport = args.Viewport;
        var worldHandle = args.WorldHandle;
        var outsideLight = _outsideLight;

        if (viewport.Eye is not { } eye)
            return;

        var target = OverlayManager.GetOverlay<BeforeLightTargetOverlay>()
            .GetCachedForViewport(viewport)
            .EnlargedLightTarget;

        // The light target is not the viewport's resolution, so world-space geometry drawn into it needs the eye scale
        //      corrected by the difference. Same correction every light overlay makes.
        var lightScale = viewport.LightRenderTarget.Size / (Vector2)viewport.Size;
        var scale = viewport.RenderScale / (Vector2.One / lightScale);
        var localMatrix = target.GetWorldToLocalMatrix(eye, scale);

        // Unique because the parameters are this overlay's alone; the z-level overlay keeps its own.
        _shader ??= _prototypeManager.Index(BleedShader).InstanceUnique();
        var shader = _shader;

        // Nothing masks it: the shader still samples a mask, so it is handed the light itself and told to ignore it.
        shader.SetParameter("MASK_TEXTURE", outsideLight.Light);
        shader.SetParameter("maskWeight", 0f);
        shader.SetParameter("dim", outsideLight.Bleed);
        shader.SetParameter("sharedAmbient", outsideLight.SharedAmbient);

        worldHandle.RenderInRenderTarget(
            target,
            () =>
            {
                worldHandle.SetTransform(localMatrix);
                worldHandle.UseShader(shader);
                worldHandle.DrawTextureRect(outsideLight.Light, outsideLight.WorldBounds);
                worldHandle.UseShader(null);
                worldHandle.SetTransform(Matrix3x2.Identity);
            },
            null);
    }
}
