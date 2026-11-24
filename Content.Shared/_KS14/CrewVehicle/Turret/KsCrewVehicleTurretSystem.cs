using Content.Shared._KS14.CrewVehicle.Gunner;
using Content.Shared._KS14.CrewVehicle.Mounts;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.CrewVehicle.Turret;

/// <summary>
///     Turns a mounted turret toward where its gunner aims, at its traverse rate, and holds its fire until it points
///         there. The gunner's client sends the aim (<see cref="KsCrewVehicleAimEvent"/>) and predicts the turning.
/// </summary>
/// <remarks>
///     The target is kept relative to the hull, so a hull that turns carries its turrets round with it until the
///         gunner's client sends a new aim. Only a turret that is turning is updated.
/// </remarks>
public sealed partial class KsCrewVehicleTurretSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private INetManager _netManager = default!;
    [Dependency] private ISharedPlayerManager _playerManager = default!;
    [Dependency] private SharedKsCrewVehicleMountSystem _mountSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    [Dependency] private EntityQuery<KsCrewVehicleGunnerComponent> _gunnerQuery = default!;
    [Dependency] private EntityQuery<KsCrewVehicleTurretComponent> _turretQuery = default!;
    [Dependency] private EntityQuery<KsCrewVehicleWeaponComponent> _weaponQuery = default!;

    /// <summary>
    ///     A gunner's aim, taken only for the turret they have selected, at a real point on its map. Anything else is
    ///         dropped without a word: it is the client's to send.
    /// </summary>
    [EventSubscription]
    private void OnAim(KsCrewVehicleAimEvent msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } playerUid ||
            !_gunnerQuery.TryComp(playerUid, out var gunnerComponent) ||
            !TryGetEntity(msg.Weapon, out var weaponUid) ||
            gunnerComponent.Selected != weaponUid ||
            !_turretQuery.HasComp(weaponUid) ||
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

        var targetAngle = direction.ToWorldAngle() - _transformSystem.GetWorldRotation(vehicleUid);
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

        // A client turns only the turret its own player has selected, which it predicts; the rest are the server's,
        //      shown as the server sends them.
        var localUid = _playerManager.LocalEntity;
        EntityUid? localSelectedUid = localUid != null && _gunnerQuery.TryComp(localUid, out var localGunner)
            ? localGunner.Selected
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

                // The server's state takes the marker off the client: a networked component the client removed while
                //      predicting would stay gone through the state reset that puts the turret back where the server
                //      has it, still turning, and the client would stop predicting the rest of the turn.
                if (_netManager.IsServer)
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
        if (args.Cancelled || args.Used.Comp.ShootCoordinates is not { } aimCoordinates)
            return;

        var aimMap = _transformSystem.ToMapCoordinates(aimCoordinates);
        var (position, rotation) = _transformSystem.GetWorldPositionRotation(turret.Owner);
        var direction = aimMap.Position - position;

        // Another map is the gunner system's to refuse.
        if (aimMap.MapId != Transform(turret.Owner).MapID || direction.LengthSquared() < 0.0001f)
            return;

        if (Math.Abs(Angle.ShortestDistance(rotation, direction.ToWorldAngle()).Theta) > turret.Comp.FireTolerance.Theta)
            args.Cancel();
    }
}
