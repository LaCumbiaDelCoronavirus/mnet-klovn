using System.Numerics;
using Content.Shared._KS14.CrewVehicle.Mounts;
using Content.Shared._KS14.CrewVehicle.Turret;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Graphics.RSI;

namespace Content.Client._KS14.CrewVehicle.Turret;

/// <summary>
///     Keeps a freely turning turret's sprite on the pivot its hull's sprite draws for it
///         (<see cref="KsCrewVehicleTurretSpriteComponent"/>). A turret mounted on another turret's weapon rides that
///         turret's sprite instead: its own sprite is moved by exactly what its parent's was.
/// </summary>
/// <remarks>
///     Which of the hull's four drawings is shown depends on the hull's rotation on screen, the eye's included, and the
///         eye can turn in any frame, so this is a frame update. It does the work only for a turret in view whose hull
///         drawing, hull rotation or own rotation changed since its offset was last set
///         (<see cref="KsCrewVehicleTurretSpriteCacheComponent"/>): a hull standing still under a still eye costs a
///         comparison a frame.
/// </remarks>
public sealed partial class KsCrewVehicleTurretSpriteSystem : EntitySystem
{
    /// <summary>
    ///     How far outside the view a turret's offset is still kept up, in tiles: a turret sprite is drawn some way from
    ///         its entity.
    /// </summary>
    private const float ViewMargin = 3f;

    [Dependency] private IEyeManager _eyeManager = default!;
    [Dependency] private SpriteSystem _spriteSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var eye = _eyeManager.CurrentEye;
        var eyeRotation = eye.Rotation;
        var view = _eyeManager.GetWorldViewport().Enlarged(ViewMargin);
        var query = EntityQueryEnumerator<KsCrewVehicleTurretSpriteComponent, KsCrewVehicleWeaponComponent, SpriteComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var turretSpriteComponent, out var weaponComponent, out var spriteComponent, out var transformComponent))
        {
            if (weaponComponent.Vehicle is not { } hullUid ||
                transformComponent.MapID != eye.Position.MapId)
                continue;

            if (transformComponent.ParentUid != hullUid)
            {
                FollowParentSprite((uid, spriteComponent, transformComponent));
                continue;
            }

            var (hullPosition, hullRotation) = _transformSystem.GetWorldPositionRotation(hullUid);
            if (!view.Contains(hullPosition))
                continue;

            // The hull's sprite is drawn as the direction nearest its rotation on screen, turned by what is left over.
            var screenAngle = (hullRotation + eyeRotation).Reduced().FlipPositive();
            var direction = SpriteComponent.Layer.GetDirection(RsiDirectionType.Dir4, screenAngle) switch
            {
                RsiDirection.North => Direction.North,
                RsiDirection.East => Direction.East,
                RsiDirection.West => Direction.West,
                _ => Direction.South,
            };

            var cacheComponent = EnsureComp<KsCrewVehicleTurretSpriteCacheComponent>(uid);
            if (cacheComponent.Direction == direction &&
                cacheComponent.HullRotation.EqualsApprox(hullRotation) &&
                cacheComponent.LocalRotation.EqualsApprox(transformComponent.LocalRotation) &&
                cacheComponent.LocalPosition == transformComponent.LocalPosition)
                continue;

            cacheComponent.Direction = direction;
            cacheComponent.HullRotation = hullRotation;
            cacheComponent.LocalRotation = transformComponent.LocalRotation;
            cacheComponent.LocalPosition = transformComponent.LocalPosition;

            if (!turretSpriteComponent.PivotOffsets.TryGetValue(direction, out var pivot))
                continue;

            // From the weapon to the pivot, in the world, then in the weapon's own turning frame, which its sprite's
            //      offset is in.
            var toPivot = (hullRotation - direction.ToAngle()).RotateVec(pivot) - hullRotation.RotateVec(transformComponent.LocalPosition);
            var weaponRotation = hullRotation + transformComponent.LocalRotation;
            _spriteSystem.SetOffset((uid, spriteComponent), (-weaponRotation).RotateVec(toPivot));
        }
    }

    /// <summary>
    ///     A weapon mounted on another weapon whose sprite is moved onto its pivot is drawn moved by the same amount in the
    ///         world, so it stays where it sits on that weapon's sprite. A frame behind its parent at most.
    /// </summary>
    private void FollowParentSprite(Entity<SpriteComponent, TransformComponent> weapon)
    {
        var parentUid = weapon.Comp2.ParentUid;
        if (!HasComp<KsCrewVehicleTurretSpriteComponent>(parentUid) || !TryComp<SpriteComponent>(parentUid, out var parentSpriteComponent))
            return;

        var parentRotation = _transformSystem.GetWorldRotation(parentUid);
        var weaponRotation = parentRotation + weapon.Comp2.LocalRotation;
        var worldShift = parentRotation.RotateVec(parentSpriteComponent.Offset);
        _spriteSystem.SetOffset((weapon.Owner, weapon.Comp1), (-weaponRotation).RotateVec(worldShift));
    }
}

/// <summary>
///     What a turret's sprite offset was last worked out from, so that it is worked out again only when one of them
///         changes. Client only.
/// </summary>
[RegisterComponent]
public sealed partial class KsCrewVehicleTurretSpriteCacheComponent : Component
{
    [ViewVariables]
    public Direction? Direction;

    [ViewVariables]
    public Angle HullRotation;

    [ViewVariables]
    public Angle LocalRotation;

    [ViewVariables]
    public Vector2 LocalPosition;
}
