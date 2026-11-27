using System.Numerics;
using Robust.Client.Graphics;

namespace Content.Client._KS14.CrewVehicle.Interior;

/// <summary>
///     The outside's light map as one viewport's crew-vehicle backdrop left it, for the interior pass that follows.
/// </summary>
/// <param name="Viewport">The viewport that drew the backdrop; no other may use it.</param>
/// <param name="InteriorMapUid">The interior map whose pass it lights.</param>
/// <param name="Light">The copied light map, at the light target's resolution.</param>
/// <param name="WorldBounds">Where it lies on the interior map: the interior eye's own view.</param>
/// <param name="Bleed">How much of it gets in, from <c>KsCrewVehicleInteriorMapComponent.OutsideLightBleed</c>.</param>
/// <param name="SharedAmbient">The ambient light the outside and the interior have in common, in the light map's units.</param>
public readonly record struct KsCrewVehicleOutsideLight(
    IClydeViewport Viewport,
    EntityUid InteriorMapUid,
    Texture Light,
    Box2Rotated WorldBounds,
    float Bleed,
    Vector3 SharedAmbient);

/// <summary>
///     Carries the outside light from the viewport that copies it to <see cref="KsCrewVehicleInteriorLightOverlay"/>, which
///         bleeds it into the interior.
/// </summary>
/// <remarks>
///     The copy belongs to the <c>ScalingViewport</c>, the only thing that drives the backdrop pass and knows when the
///         copy is stale. This holds one viewport's at a time, which is enough because a control draws its backdrop and
///         its interior pass together, before the next control draws.
/// </remarks>
public sealed partial class KsCrewVehicleInteriorLightSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlayManager = default!;

    private KsCrewVehicleOutsideLight? _outsideLight;

    public override void Initialize()
    {
        base.Initialize();

        _overlayManager.AddOverlay(new KsCrewVehicleInteriorLightOverlay());
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _outsideLight = null;
        _overlayManager.RemoveOverlay<KsCrewVehicleInteriorLightOverlay>();
    }

    /// <summary>
    ///     Hands over the outside light a viewport has just copied.
    /// </summary>
    public void SetOutsideLight(KsCrewVehicleOutsideLight outsideLight)
    {
        _outsideLight = outsideLight;
    }

    /// <summary>
    ///     Forgets a viewport's outside light, leaving another viewport's alone.
    /// </summary>
    public void ClearOutsideLight(IClydeViewport viewport)
    {
        if (_outsideLight is { } outsideLight && outsideLight.Viewport == viewport)
            _outsideLight = null;
    }

    /// <summary>
    ///     The outside light for a pass of this interior map in this viewport, if its backdrop left one.
    /// </summary>
    public bool TryGetOutsideLight(IClydeViewport viewport, EntityUid mapUid, out KsCrewVehicleOutsideLight outsideLight)
    {
        if (_outsideLight is { } light && light.Viewport == viewport && light.InteriorMapUid == mapUid)
        {
            outsideLight = light;
            return true;
        }

        outsideLight = default;
        return false;
    }
}
