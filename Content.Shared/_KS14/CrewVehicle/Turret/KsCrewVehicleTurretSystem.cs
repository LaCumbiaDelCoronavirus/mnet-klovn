using Content.Shared._KS14.CrewVehicle.Gunner;
using Content.Shared._KS14.CrewVehicle.Mounts;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.CrewVehicle.Turret;

/// <summary>
///     Turns a mounted turret toward where its gunner aims, at its traverse rate, and holds its fire until it points
///         there. The gunner's client sends the aim (<see cref="KsCrewVehicleAimEvent"/>) and predicts the turning.
/// </summary>
/// <remarks>
///     The target is kept relative to what the turret is mounted on (the hull, or another turret it rides), so a hull
///         that turns carries its turrets round with it until the gunner's client sends a new aim. Only a turret that is
///         turning is updated. A coaxial weapon (<see cref="KsCrewVehicleCoaxialComponent"/>) has no traverse of its own:
///         aiming it aims the turret it is fixed to (<see cref="GetAimedTurret"/>).
/// </remarks>
public sealed partial class KsCrewVehicleTurretSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private ISharedPlayerManager _playerManager = default!;
    [Dependency] private SharedKsCrewVehicleMountSystem _mountSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    [Dependency] private EntityQuery<KsCrewVehicleCoaxialComponent> _coaxialQuery = default!;
    [Dependency] private EntityQuery<KsCrewVehicleGunnerComponent> _gunnerQuery = default!;
    [Dependency] private EntityQuery<KsCrewVehicleTurretComponent> _turretQuery = default!;
    [Dependency] private EntityQuery<KsCrewVehicleWeaponComponent> _weaponQuery = default!;

    /// <summary>
    ///     The turret that aiming <paramref name="weaponUid"/> turns: the weapon itself if it is a turret, the turret it is
    ///         fixed to if it is coaxial, else null.
    /// </summary>
    public EntityUid? GetAimedTurret(EntityUid? weaponUid)
    {
        if (weaponUid is not { } uid)
            return null;

        if (_turretQuery.HasComp(uid))
            return uid;

        if (!_coaxialQuery.HasComp(uid))
            return null;

        var parentUid = Transform(uid).ParentUid;
        return _turretQuery.HasComp(parentUid) && _weaponQuery.HasComp(parentUid) ? parentUid : null;
    }

    /// <summary>
    ///     A gunner's aim, taken only for the turret their selected weapon aims, at a real point on its map. Anything else
    ///         is dropped without a word: it is the client's to send.
    /// </summary>
    [EventSubscription]
    private void OnAim(KsCrewVehicleAimEvent msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } playerUid ||
            !_gunnerQuery.TryComp(playerUid, out var gunnerComponent) ||
            !TryGetEntity(msg.Weapon, out var weaponUid) ||
            GetAimedTurret(gunnerComponent.Selected) != weaponUid ||
            !_weaponQuery.TryComp(weaponUid, out var weaponComponent))
            return;

        TryAim((weaponUid.Value, weaponComponent), GetCoordinates(msg.Target));
    }

    /// <summary>
    ///     Sets the turret turning toward <paramref name="target"/>, kept inside its fire arc. False if the point is not
    ///         one, is on another map, or the turret already took an aim this tick.
    /// </summary>
    public bool TryAim(Entity<KsCrewVehicleWeaponComponent> weapon, EntityCoordinates target)
    {
        if (weapon.Comp.Vehicle is not { } vehicleUid || !_transformSystem.IsValid(target))
            return false;

        var transformComponent = Transform(weapon.Owner);
        var targetMap = _transformSystem.ToMapCoordinates(target);
        if (targetMap.MapId != transformComponent.MapID)
            return false;

        var direction = targetMap.Position - _transformSystem.GetWorldPosition(transformComponent);
        if (direction.LengthSquared() < 0.0001f || !float.IsFinite(direction.X) || !float.IsFinite(direction.Y))
            return false;

        var targetAngle = direction.ToWorldAngle() - _mountSystem.GetMountedOnWorldRotation(weapon.Owner, vehicleUid);
        if (weapon.Comp.FireArc is { } fireArc)
        {
            var rest = _mountSystem.GetRestRotation(weapon);
            var halfArc = fireArc.Theta / 2;
            var offset = Math.Clamp(Angle.ShortestDistance(rest, targetAngle).Theta, -halfArc, halfArc);
            targetAngle = rest + new Angle(offset);
        }

        targetAngle = targetAngle.Reduced();
        var aimComponent = EnsureComp<KsCrewVehicleTurretAimComponent>(weapon.Owner);
        if (aimComponent.LastAimTick >= _gameTiming.CurTick)
            return false;

        aimComponent.LastAimTick = _gameTiming.CurTick;
        aimComponent.TargetAngle = targetAngle;
        Dirty(weapon.Owner, aimComponent);

        if (!transformComponent.LocalRotation.EqualsApprox(targetAngle))
            EnsureComp<KsCrewVehicleTurretTraversingComponent>(weapon.Owner);

        return true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // A client turns only the turret its own player's selected weapon aims, which it predicts; the rest are the
        //      server's, shown as the server sends them.
        var localUid = _playerManager.LocalEntity;
        var localSelectedUid = localUid != null && _gunnerQuery.TryComp(localUid, out var localGunner)
            ? GetAimedTurret(localGunner.Selected)
            : null;

        var query = EntityQueryEnumerator<KsCrewVehicleTurretTraversingComponent, KsCrewVehicleTurretAimComponent, KsCrewVehicleTurretComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var traversingComponent, out var aimComponent, out var turretComponent, out var transformComponent))
        {
            if (traversingComponent.LifeStage > ComponentLifeStage.Running ||
                localUid != null && localSelectedUid != uid)
                continue;

            var delta = Angle.ShortestDistance(transformComponent.LocalRotation, aimComponent.TargetAngle).Theta;
            var step = turretComponent.TraverseRate.Theta * frameTime;
            if (Math.Abs(delta) <= step)
            {
                _transformSystem.SetLocalRotation(uid, aimComponent.TargetAngle, transformComponent);

                // Predicted on the client too: a state reset that puts the turret back where the server has it, still
                //      turning, brings the marker back with it (KsCrewVehicleMultiGunnerTest.PredictedTurnHoldsUntilTheServerArrives).
                RemCompDeferred<KsCrewVehicleTurretTraversingComponent>(uid);
                continue;
            }

            _transformSystem.SetLocalRotation(uid, transformComponent.LocalRotation + new Angle(Math.Sign(delta) * step), transformComponent);
        }
    }

    /// <summary>
    ///     A turret fires only once it points where it is aimed. Refused before anything is spent, so a held trigger
    ///         fires the tick it gets there.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnShotAttempted(Entity<KsCrewVehicleTurretComponent> turret, ref ShotAttemptedEvent args)
    {
        HoldFireUntilAligned(turret.Owner, turret.Comp.FireTolerance, ref args);
    }

    /// <summary>
    ///     A coaxial weapon fires only once the turret it is fixed to points at the target, within the weapon's own
    ///         tolerance. Measured from the turret, not the weapon beside it: from a weapon off the turret's axis, a target
    ///         close by is never straight ahead, and it would never fire at it. Its round flies from its own muzzle to the
    ///         point aimed at all the same.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnCoaxialShotAttempted(Entity<KsCrewVehicleCoaxialComponent> coaxial, ref ShotAttemptedEvent args)
    {
        HoldFireUntilAligned(GetAimedTurret(coaxial.Owner) ?? coaxial.Owner, coaxial.Comp.FireTolerance, ref args);
    }

    private void HoldFireUntilAligned(EntityUid weaponUid, Angle tolerance, ref ShotAttemptedEvent args)
    {
        if (args.Cancelled || args.Used.Comp.ShootCoordinates is not { } aimCoordinates)
            return;

        var aimMap = _transformSystem.ToMapCoordinates(aimCoordinates);
        var (position, rotation) = _transformSystem.GetWorldPositionRotation(weaponUid);
        var direction = aimMap.Position - position;

        // Another map is the gunner system's to refuse.
        if (aimMap.MapId != Transform(weaponUid).MapID || direction.LengthSquared() < 0.0001f)
            return;

        if (Math.Abs(Angle.ShortestDistance(rotation, direction.ToWorldAngle()).Theta) > tolerance.Theta)
            args.Cancel();
    }
}
