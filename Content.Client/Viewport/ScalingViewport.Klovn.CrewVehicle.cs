using System.Numerics;
using Content.Shared._KS14.CrewVehicle;
using Content.Shared._KS14.CrewVehicle.Interior;
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
    ///     If the vehicle's map is a z-level, only that level is drawn behind; the levels below it are not. The z-level
    ///         passes are written around the viewer's own eye and blit per pass, so drawing them here is not cheap.
    /// </remarks>
    public sealed partial class ScalingViewport
    {
        private SharedTransformSystem? _crewVehicleTransformSystem;

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
                !TryGetCrewVehicleBackdrop(eye, out var backdropMapId, out var anchor, out var interiorRotation,
                    out var hullPosition, out var hullRotation))
                return;

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

            var standInEye = AimStandInEye(
                new MapCoordinates(eyeCentre, backdropMapId),
                Vector2.Zero,
                eyeRotation,
                eye.Scale,
                // Cast from the stand-in's position, which is nowhere the viewer is: the viewer's own pass decides what
                //      they can see, and its FOV is what blacks out the world behind the interior's walls.
                drawFov: false,
                drawLight: eye.DrawLight);

            // Cleared explicitly: the z-level passes leave the target uncleared for the deepest level's parallax to cover,
            //      and the space around a vehicle might draw nothing at all.
            RenderPass(standInEye, Color.Black);

            // Pass two is the ordinary pass, which must land on this one. Put back at the top of the next frame.
            _viewport.ClearColor = null;
            _viewport.Eye = eye;
        }

        /// <summary>
        ///     Whether the eye is on a crew vehicle's interior map whose vehicle this client knows, and where that vehicle
        ///         and the interior's anchor are.
        /// </summary>
        /// <param name="anchor">The anchor on the interior map, not on its grid.</param>
        /// <param name="interiorRotation">How far the interior's grid is turned on its map.</param>
        private bool TryGetCrewVehicleBackdrop(
            IEye eye,
            out MapId backdropMapId,
            out Vector2 anchor,
            out Angle interiorRotation,
            out Vector2 hullPosition,
            out Angle hullRotation)
        {
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
                vehicleTransform.MapID == eye.Position.MapId)
                return false;

            _crewVehicleTransformSystem ??= _entityManager.System<SharedTransformSystem>();
            (hullPosition, hullRotation) = _crewVehicleTransformSystem.GetWorldPositionRotation(vehicleTransform);
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
