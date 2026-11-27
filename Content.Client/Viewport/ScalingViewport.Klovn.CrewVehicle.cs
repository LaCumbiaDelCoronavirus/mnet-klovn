using System.Numerics;
using Content.Client._KS14.CrewVehicle.Interior;
using Content.Shared._KS14.CrewVehicle;
using Content.Shared._KS14.CrewVehicle.Interior;
using Content.Shared._KS14.ZLevel.Light;
using Robust.Client.Graphics;
using Robust.Shared.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Client.Viewport
{
    /// <summary>
    ///     Crew-vehicle backdrop: for a viewer whose eye is inside a vehicle's interior, the world around the vehicle is
    ///         drawn first, through a stand-in eye carried out of the interior and turned with the hull, and the ordinary
    ///         pass of the viewer's own map then lands on top of it without clearing.
    /// </summary>
    /// <remarks>
    ///     The interior has no background of its own (its map's parallax is blank), so the world shows wherever the
    ///         interior has neither floor nor anything else drawn. The viewer's own pass has their FOV on, and hard FOV
    ///         paints opaque black over the uncleared target wherever they can't see, so walls hide the world and windows
    ///         (wall pieces with no occluder) show it, with no code per window.
    ///     Only pass one is drawn here. Pass two is whatever <see cref="Draw"/> does next, which on an interior map is the
    ///         ordinary single pass through the viewer's real eye, then the one blit and the screen overlays.
    ///     Crew in a view seat have their eye on the vehicle's own map, so they never reach this and pay nothing for it.
    ///     If the vehicle's map is a z-level, pass one is that level's whole stack, the levels below included, drawn as
    ///         for a viewer standing at the stand-in eye, and left in the target for pass two to blit.
    ///     The outside's light map is copied out after pass one, and <see cref="KsCrewVehicleInteriorLightOverlay"/> bleeds
    ///         it into pass two's: the engine clears the light target at the top of every pass, so nothing else carries
    ///         it across.
    /// </remarks>
    public sealed partial class ScalingViewport
    {
        private SharedTransformSystem? _crewVehicleTransformSystem;
        private KsCrewVehicleInteriorLightSystem? _crewVehicleLightSystem;

        /// <summary>
        ///     The outside's light map as the backdrop left it, at the light target's resolution.
        /// </summary>
        private IRenderTexture? _crewVehicleOutsideLight;

        /// <summary>
        ///     Puts the viewport's clear colour back after a frame that had a backdrop, and forgets this viewport's outside
        ///         light from that frame.
        /// </summary>
        /// <remarks>
        ///     Called every frame before <see cref="TryDrawZLevels"/>, whether or not there is a backdrop. The backdrop
        ///         itself is drawn by <see cref="TryDrawCrewVehicleBackdrop"/>, at the top of <see cref="TryDrawZLevels"/>,
        ///         the first call in <see cref="Draw"/> that has the render handle the light copy needs.
        /// </remarks>
        private void DrawCrewVehicleBackdropPass()
        {
            // Last frame's backdrop left this null; every other path starts from the default.
            _viewport!.ClearColor = Color.Black;

            _crewVehicleLightSystem ??= _entityManager.System<KsCrewVehicleInteriorLightSystem>();
            _crewVehicleLightSystem.ClearOutsideLight(_viewport);
        }

        /// <summary>
        ///     Draws the world outside the viewer's vehicle into the viewport, and leaves the viewport set not to clear,
        ///         so the next pass lands on top of it.
        /// </summary>
        private void TryDrawCrewVehicleBackdrop(IRenderHandle handle, UIBox2i drawBox, UIBox2i drawBoxGlobal)
        {
            if (_eye is not { } eye ||
                !TryGetCrewVehicleBackdrop(eye, out var interiorMap, out var backdropMapUid, out var backdropMapId,
                    out var anchor, out var interiorRotation, out var hullPosition, out var hullRotation))
            {
                ReleaseCrewVehicleOutsideLight();
                return;
            }

            // The offset is carried through with the centre, so the stand-in has none of its own.
            KsCrewVehicleInteriorTransform.GetBackdropEye(
                eye.Position.Position + eye.Offset,
                eye.Rotation,
                anchor,
                hullPosition,
                hullRotation,
                out var eyeCentre,
                out var eyeRotation,
                interiorRotation);

            // The stack as it would be drawn for someone standing at the stand-in, without their FOV or their fall. FOV
            //      would be cast from the stand-in's position, which is nowhere the viewer is: the viewer's own pass
            //      decides what they can see, and its FOV is what blacks out the world behind the interior's walls.
            // Cleared explicitly: the z-level passes leave the target uncleared for the deepest level's parallax to
            //      cover, and the space around a vehicle might draw nothing at all.
            var view = new ZLevelView(
                backdropMapUid,
                backdropMapId,
                eyeCentre,
                Vector2.Zero,
                eyeRotation,
                eye.Scale,
                DrawFov: false,
                eye.DrawLight,
                OwnEye: null,
                TransitHeight: 0f,
                Composite: false);

            if (!TryDrawZLevelStack(handle, drawBox, drawBoxGlobal, in view, firstPassClear: Color.Black))
            {
                var standInEye = AimStandInEye(
                    new MapCoordinates(eyeCentre, backdropMapId),
                    Vector2.Zero,
                    eyeRotation,
                    eye.Scale,
                    drawFov: false,
                    drawLight: eye.DrawLight);

                RenderPass(standInEye, Color.Black);
            }

            // Either way the last pass was the vehicle's own level, through the stand-in eye: the stack draws its top
            //      level last, and a stack drawn under another pass skips the gaps that would follow it.
            CaptureCrewVehicleOutsideLight(handle, eye, interiorMap, backdropMapUid);

            // Pass two is the ordinary pass, which must land on this one. Put back at the top of the next frame.
            _viewport!.ClearColor = null;
            _viewport.Eye = eye;
        }

        /// <summary>
        ///     Copies the light map the backdrop's last pass left in the viewport, and hands it to the overlay that bleeds
        ///         it into the interior.
        /// </summary>
        /// <remarks>
        ///     The stand-in eye is the interior eye carried through the interior's mapping, at the same scale and in the
        ///         same viewport, so a light-map pixel of the backdrop lies under the same screen pixel in the interior
        ///         pass. Placing the copy over the interior eye's own world bounds is all the transform it needs.
        /// </remarks>
        private void CaptureCrewVehicleOutsideLight(
            IRenderHandle handle,
            IEye interiorEye,
            Entity<KsCrewVehicleInteriorMapComponent> interiorMap,
            EntityUid outsideMapUid)
        {
            var bleed = interiorMap.Comp.OutsideLightBleed;
            if (bleed <= 0f || !interiorEye.DrawLight)
            {
                ReleaseCrewVehicleOutsideLight();
                return;
            }

            var size = _viewport!.LightRenderTarget.Size;
            if (_crewVehicleOutsideLight == null || _crewVehicleOutsideLight.Size != size)
            {
                _crewVehicleOutsideLight?.Dispose();

                // The engine's own light format, so the range of a bright light survives the copy.
                _crewVehicleOutsideLight = _clyde.CreateLightRenderTarget(size, "ks-crew-vehicle-outside-light");
            }

            CopyIntoCapture(handle, _viewport.LightRenderTarget.Texture, _crewVehicleOutsideLight,
                GetEyeWorldBounds(_standInEye));

            // The light map holds the outside's ambient light, and the interior has its own: only what is over the
            //      ambient the two share is carried in.
            var sharedAmbient = KsZLevelLightLeak.GetSharedAmbient(
                GetCrewVehicleMapAmbient(outsideMapUid),
                GetCrewVehicleMapAmbient(interiorMap.Owner));

            _crewVehicleLightSystem!.SetOutsideLight(new KsCrewVehicleOutsideLight(
                _viewport,
                interiorMap.Owner,
                _crewVehicleOutsideLight.Texture,
                GetEyeWorldBounds(interiorEye),
                bleed,
                sharedAmbient));
        }

        private Color GetCrewVehicleMapAmbient(EntityUid mapUid)
        {
            return _entityManager.GetComponentOrNull<MapLightComponent>(mapUid)?.AmbientLightColor
                   ?? MapLightComponent.DefaultColor;
        }

        /// <summary>
        ///     Lets go of the outside light copy: there is no backdrop, it is not wanted, or the viewport it is sized
        ///         against is being regenerated or thrown away.
        /// </summary>
        private void ReleaseCrewVehicleOutsideLight()
        {
            if (_viewport != null)
                _crewVehicleLightSystem?.ClearOutsideLight(_viewport);

            _crewVehicleOutsideLight?.Dispose();
            _crewVehicleOutsideLight = null;
        }

        /// <summary>
        ///     Whether the eye is on a crew vehicle's interior map whose vehicle this client knows, and where that vehicle
        ///         and the interior's anchor are.
        /// </summary>
        /// <param name="anchor">The anchor on the interior map, not on its grid.</param>
        /// <param name="interiorRotation">How far the interior's grid is turned on its map.</param>
        private bool TryGetCrewVehicleBackdrop(
            IEye eye,
            out Entity<KsCrewVehicleInteriorMapComponent> interiorMap,
            out EntityUid backdropMapUid,
            out MapId backdropMapId,
            out Vector2 anchor,
            out Angle interiorRotation,
            out Vector2 hullPosition,
            out Angle hullRotation)
        {
            interiorMap = default;
            backdropMapUid = EntityUid.Invalid;
            backdropMapId = MapId.Nullspace;
            anchor = default;
            interiorRotation = default;
            hullPosition = default;
            hullRotation = default;

            if (!_mapSystem.TryGetMap(eye.Position.MapId, out var interiorMapUid) ||
                !_entityManager.TryGetComponent<KsCrewVehicleInteriorMapComponent>(interiorMapUid, out var interiorMapComponent))
                return false;

            // The vehicle is only drawn from while it is really here: in PVS (a vehicle out of it is detached, in
            //      nullspace) and on a map other than its own interior's.
            var vehicleUid = interiorMapComponent.Vehicle;
            if (!vehicleUid.IsValid() ||
                !_entityManager.TryGetComponent<TransformComponent>(vehicleUid, out var vehicleTransform) ||
                !_entityManager.TryGetComponent<MetaDataComponent>(vehicleUid, out var vehicleMetaData) ||
                (vehicleMetaData.Flags & MetaDataFlags.Detached) != 0 ||
                vehicleTransform.MapID == MapId.Nullspace ||
                vehicleTransform.MapID == eye.Position.MapId ||
                vehicleTransform.MapUid is not { } vehicleMapUid)
                return false;

            _crewVehicleTransformSystem ??= _entityManager.System<SharedTransformSystem>();
            (hullPosition, hullRotation) = _crewVehicleTransformSystem.GetWorldPositionRotation(vehicleTransform);
            interiorMap = (interiorMapUid.Value, interiorMapComponent);
            backdropMapUid = vehicleMapUid;
            backdropMapId = vehicleTransform.MapID;

            // The anchor is in the interior grid's coordinates. An interior is loaded unrotated, usually at the map's
            //      origin, but nothing makes it so; with no grid at all, its coordinates are taken as the map's.
            anchor = interiorMapComponent.Anchor;
            var mapChildren = _entityManager.GetComponent<TransformComponent>(interiorMapUid.Value).ChildEnumerator;
            while (mapChildren.MoveNext(out var childUid))
            {
                if (!_entityManager.HasComponent<MapGridComponent>(childUid))
                    continue;

                var (gridPosition, gridRotation) = _crewVehicleTransformSystem.GetWorldPositionRotation(childUid);
                anchor = gridPosition + gridRotation.RotateVec(interiorMapComponent.Anchor);
                interiorRotation = gridRotation;
                break;
            }

            return true;
        }
    }
}
