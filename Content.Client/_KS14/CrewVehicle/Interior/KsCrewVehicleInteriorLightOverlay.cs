using System.Numerics;
using Content.Client._KS14.OverlayZIndex;
using Content.Client.Light;
using Content.Shared._KS14.CrewVehicle.Interior;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;

namespace Content.Client._KS14.CrewVehicle.Interior;

/// <summary>
///     Lights a crew vehicle's interior: its cabin's own dim ambient light, and the light falling on the ground outside,
///         bled in as if the roof let a share of it through.
/// </summary>
/// <remarks>
///     Drawn into <see cref="BeforeLightTargetOverlay"/>'s enlarged target, under the engine's blur, so it arrives soft.
///     The ambient (<see cref="KsCrewVehicleInteriorMapComponent.CabinAmbient"/>) is added over the whole target, where a
///         <c>MapLight</c> would have cleared it to the same colour: the interior map has none, so that nothing else on
///         it is lit by one.
///     The outside light map is the one the viewport's backdrop pass left (<see cref="KsCrewVehicleInteriorLightSystem"/>);
///         it covers the visible screen only, so the blur's skirt at the screen edge gets none of it. It ignores where
///         the windows are: the whole cabin is lit alike.
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

    private static readonly ProtoId<ShaderPrototype> AmbientShader = "KsCrewVehicleCabinAmbient";

    public static readonly int ContentZIndex = BeforeLightingZIndex.CrewVehicleOutsideLight.ToZIndex();

    private KsCrewVehicleInteriorLightSystem? _lightSystem;
    private ShaderInstance? _shader;
    private ShaderInstance? _ambientShader;
    private KsCrewVehicleOutsideLight? _outsideLight;
    private Color _cabinAmbient;

    public KsCrewVehicleInteriorLightOverlay()
    {
        IoCManager.InjectDependencies(this);
        ZIndex = ContentZIndex;
    }

    protected override void DisposeBehavior()
    {
        _shader?.Dispose();
        _shader = null;
        _ambientShader?.Dispose();
        _ambientShader = null;

        base.DisposeBehavior();
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        _lightSystem ??= _entityManager.System<KsCrewVehicleInteriorLightSystem>();

        if (args.Viewport.Eye == null)
            return false;

        _outsideLight = _lightSystem.TryGetOutsideLight(args.Viewport, args.MapUid, out var outsideLight)
            ? outsideLight
            : null;

        _cabinAmbient = _entityManager.TryGetComponent<KsCrewVehicleInteriorMapComponent>(args.MapUid, out var interiorMapComponent)
            ? interiorMapComponent.CabinAmbient
            : Color.Black;

        return _outsideLight != null || HasLight(_cabinAmbient);
    }

    private static bool HasLight(Color color)
    {
        return color.R > 0f || color.G > 0f || color.B > 0f;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        // Copied out of the ref struct before the lambda below captures anything.
        var viewport = args.Viewport;
        var worldHandle = args.WorldHandle;
        var outsideLight = _outsideLight;
        var cabinAmbient = _cabinAmbient;

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

        ShaderInstance? ambientShader = null;
        if (HasLight(cabinAmbient))
        {
            _ambientShader ??= _prototypeManager.Index(AmbientShader).InstanceUnique();
            ambientShader = _ambientShader;
            ambientShader.SetParameter("ambient", new Vector3(cabinAmbient.R, cabinAmbient.G, cabinAmbient.B));
        }

        ShaderInstance? bleedShader = null;
        if (outsideLight is { } light)
        {
            // Unique because the parameters are this overlay's alone; the z-level overlay keeps its own.
            _shader ??= _prototypeManager.Index(BleedShader).InstanceUnique();
            bleedShader = _shader;

            // Nothing masks it: the shader still samples a mask, so it is handed the light itself and told to ignore it.
            bleedShader.SetParameter("MASK_TEXTURE", light.Light);
            bleedShader.SetParameter("maskWeight", 0f);
            bleedShader.SetParameter("dim", light.Bleed);
            bleedShader.SetParameter("sharedAmbient", light.SharedAmbient);
        }

        var targetSize = (Vector2)target.Size;
        worldHandle.RenderInRenderTarget(
            target,
            () =>
            {
                if (ambientShader != null)
                {
                    // The whole target, skirt included, in its own pixels.
                    worldHandle.SetTransform(Matrix3x2.Identity);
                    worldHandle.UseShader(ambientShader);
                    worldHandle.DrawRect(new Box2(Vector2.Zero, targetSize), Color.White);
                }

                if (bleedShader != null && outsideLight is { } bled)
                {
                    worldHandle.SetTransform(localMatrix);
                    worldHandle.UseShader(bleedShader);
                    worldHandle.DrawTextureRect(bled.Light, bled.WorldBounds);
                    worldHandle.SetTransform(Matrix3x2.Identity);
                }

                worldHandle.UseShader(null);
            },
            null);
    }
}
