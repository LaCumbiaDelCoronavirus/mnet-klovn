using System.Numerics;
using Content.Client._KS14.CrewVehicle.Spike;
using Content.Shared._KS14.Portal;
using Robust.Shared.Graphics;
using Robust.Shared.Map;

namespace Content.Client.Viewport
{
    /// <summary>
    ///     Crew-vehicle backdrop: for a viewer inside a vehicle's interior, the world around the vehicle is drawn first,
    ///         through a stand-in eye carried out of the interior and turned with the hull, and the ordinary pass of the
    ///         viewer's own map then lands on top of it without clearing.
    /// </summary>
    /// <remarks>
    ///     The interior has no background of its own, so the world shows wherever the interior has neither floor nor
    ///         anything else drawn. The viewer's own pass has their FOV on: hard FOV paints opaque black over the
    ///         uncleared target wherever they can't see, so walls hide the world and windows (wall pieces with no
    ///         occluder) show it, with no code per window.
    ///     Only pass one is drawn here. Pass two is whatever <see cref="Draw"/> does next, which on an interior map is the
    ///         ordinary single pass through the viewer's real eye, then the one blit and the screen overlays.
    ///     If the vehicle's map is itself a z-level, only that level is drawn behind; the levels below it are not.
    /// </remarks>
    public sealed partial class ScalingViewport
    {
        private KsCrewVehicleBackdropSpikeSystem? _crewVehicleBackdropSpikeSystem; // SPIKE: remove in milestone 5

        /// <summary>
        ///     Draws the world outside the viewer's vehicle into the viewport, and leaves the viewport set not to clear,
        ///         so the next pass lands on top of it.
        /// </summary>
        /// <remarks>
        ///     Called every frame before the z-level attempt, whether or not there is a backdrop, because it is also what
        ///         puts the viewport's clear colour back after a frame that had one.
        /// </remarks>
        private void DrawCrewVehicleBackdropPass()
        {
            // Last frame's backdrop left this null; every other path starts from the default.
            _viewport!.ClearColor = Color.Black;

            if (_eye is not { } eye ||
                !TryGetCrewVehicleBackdrop(eye, out var backdropMapId, out var anchor, out var vehiclePosition, out var vehicleRotation))
                return;

            // The interior point P lands on V + R(theta)(P - A): a two-sided doorway whose near side is the interior
            //      anchor, unturned, and whose far side is the vehicle. The offset is carried through with the centre,
            //      so the stand-in has none of its own.
            KsPortalTransform.GetPortalEye(
                eye.Position.Position + eye.Offset,
                eye.Rotation,
                anchor,
                Angle.Zero,
                vehiclePosition,
                vehicleRotation,
                out var eyeCentre,
                out var eyeRotation);

            var standInEye = AimStandInEye(
                new MapCoordinates(eyeCentre, backdropMapId),
                Vector2.Zero,
                eyeRotation,
                eye.Scale,
                // Cast from the stand-in's position, which is nowhere the viewer is: the viewer's own pass decides what
                //      they can see.
                drawFov: false,
                drawLight: eye.DrawLight);

            // Cleared explicitly: the z-level passes leave the target uncleared for the deepest level's parallax to cover,
            //      and a backdrop map can have none.
            RenderPass(standInEye, Color.Black);

            // Pass two is the ordinary pass, which must land on this one. Put back at the top of the next frame.
            _viewport.ClearColor = null;
            _viewport.Eye = eye;
        }

        /// <summary>
        ///     Whether the viewer's map has a world behind it, and where that world is.
        /// </summary>
        private bool TryGetCrewVehicleBackdrop(
            IEye eye,
            out MapId backdropMapId,
            out Vector2 anchor,
            out Vector2 vehiclePosition,
            out Angle vehicleRotation)
        {
            backdropMapId = MapId.Nullspace;
            anchor = default;
            vehiclePosition = default;
            vehicleRotation = default;

            // SPIKE start: remove in milestone 5. The real pass asks whether the eye's map carries
            //      KsCrewVehicleInteriorMap and the client knows its vehicle, and reads the anchor and the hull's
            //      transform from there.
            if (_crewVehicleBackdropSpikeSystem == null &&
                _entityManager.EntitySysManager.TryGetEntitySystem<KsCrewVehicleBackdropSpikeSystem>(out var foundSpikeSystem))
                _crewVehicleBackdropSpikeSystem = foundSpikeSystem;

            if (_crewVehicleBackdropSpikeSystem is not { } spikeSystem ||
                spikeSystem.BackdropMap is not { } spikeMapId)
                return false;

            // Only the player's own view: a camera monitor's viewport has an eye somewhere else entirely.
            if (_playerManager.LocalEntity is not { } localUid ||
                !_entityManager.TryGetComponent<TransformComponent>(localUid, out var localTransform) ||
                localTransform.MapID != eye.Position.MapId)
                return false;

            backdropMapId = spikeMapId;
            anchor = spikeSystem.Anchor;
            vehiclePosition = spikeSystem.VehiclePosition;
            vehicleRotation = spikeSystem.VehicleRotation;
            // SPIKE end

            // A map that is not there client-side (deleted, or never sent) has nothing to draw, and the viewer's own map
            //      behind itself is a second copy of everything.
            return backdropMapId != MapId.Nullspace &&
                   backdropMapId != eye.Position.MapId &&
                   _mapSystem.MapExists(backdropMapId);
        }
    }
}
