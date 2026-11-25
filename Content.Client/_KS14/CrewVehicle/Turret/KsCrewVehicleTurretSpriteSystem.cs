using Content.Shared._KS14.CrewVehicle.Mounts;
using Content.Shared._KS14.CrewVehicle.Turret;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Graphics.RSI;

namespace Content.Client._KS14.CrewVehicle.Turret;

/// <summary>
///     Keeps a freely turning turret's sprite on the pivot its hull's sprite draws for it
///         (<see cref="KsCrewVehicleTurretSpriteComponent"/>). Which of the hull's four drawings is shown depends on the
///         hull's rotation on screen, the eye's included, so this runs each frame, for the few weapons that have one.
/// </summary>
public sealed partial class KsCrewVehicleTurretSpriteSystem : EntitySystem
{
    [Dependency] private IEyeManager _eyeManager = default!;
    [Dependency] private SpriteSystem _spriteSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var eyeRotation = _eyeManager.CurrentEye.Rotation;
        var query = EntityQueryEnumerator<KsCrewVehicleTurretSpriteComponent, KsCrewVehicleWeaponComponent, SpriteComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var turretSpriteComponent, out var weaponComponent, out var spriteComponent, out var transformComponent))
        {
            if (weaponComponent.Vehicle is not { } hullUid || transformComponent.ParentUid != hullUid)
                continue;

            var hullRotation = _transformSystem.GetWorldRotation(hullUid);

            // The hull's sprite is drawn as the direction nearest its rotation on screen, turned by what is left over.
            var screenAngle = (hullRotation + eyeRotation).Reduced().FlipPositive();
            var direction = SpriteComponent.Layer.GetDirection(RsiDirectionType.Dir4, screenAngle) switch
            {
                RsiDirection.North => Direction.North,
                RsiDirection.East => Direction.East,
                RsiDirection.West => Direction.West,
                _ => Direction.South,
            };

            if (!turretSpriteComponent.PivotOffsets.TryGetValue(direction, out var pivot))
                continue;

            // From the weapon to the pivot, in the world, then in the weapon's own turning frame, which its sprite's
            //      offset is in.
            var toPivot = (hullRotation - direction.ToAngle()).RotateVec(pivot) - hullRotation.RotateVec(transformComponent.LocalPosition);
            var weaponRotation = hullRotation + transformComponent.LocalRotation;
            _spriteSystem.SetOffset((uid, spriteComponent), (-weaponRotation).RotateVec(toPivot));
        }
    }
}
