using Content.Shared._KS14.CrewVehicle.Gunner;
using Content.Shared._KS14.CrewVehicle.Mounts;
using Content.Shared._KS14.CrewVehicle.Turret;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Client._KS14.CrewVehicle.Turret;

/// <summary>
///     Sends where the local gunner aims to their selected turret, when the aim has moved by at least the turret's
///         threshold and no more often than its interval.
/// </summary>
/// <remarks>
///     Hooks nothing outside itself (it reads the eye and the mouse each tick), so there is nothing to unsubscribe.
/// </remarks>
public sealed partial class KsCrewVehicleTurretInputSystem : EntitySystem
{
    [Dependency] private IEyeManager _eyeManager = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IInputManager _inputManager = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    [Dependency] private EntityQuery<KsCrewVehicleGunnerComponent> _gunnerQuery = default!;
    [Dependency] private EntityQuery<KsCrewVehicleTurretComponent> _turretQuery = default!;
    [Dependency] private EntityQuery<KsCrewVehicleWeaponComponent> _weaponQuery = default!;

    // For the one local player: which turret was last aimed, at what, and when the next aim may go.
    private EntityUid? _lastWeaponUid;
    private Angle _lastAngle;
    private TimeSpan _nextSend;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_gameTiming.IsFirstTimePredicted)
            return;

        if (_playerManager.LocalEntity is not { } playerUid ||
            !_gunnerQuery.TryComp(playerUid, out var gunnerComponent) ||
            gunnerComponent.Selected is not { } weaponUid ||
            !_turretQuery.TryComp(weaponUid, out var turretComponent) ||
            !_weaponQuery.TryComp(weaponUid, out var weaponComponent) ||
            weaponComponent.Vehicle is not { } vehicleUid)
        {
            _lastWeaponUid = null;
            return;
        }

        var mouse = _inputManager.MouseScreenPosition;
        if (!mouse.IsValid)
            return;

        var mouseMap = _eyeManager.PixelToMap(mouse);
        var transformComponent = Transform(weaponUid);
        if (mouseMap.MapId == MapId.Nullspace || mouseMap.MapId != transformComponent.MapID)
            return;

        var direction = mouseMap.Position - _transformSystem.GetWorldPosition(transformComponent);
        if (direction.LengthSquared() < 0.0001f)
            return;

        var angle = direction.ToWorldAngle() - _transformSystem.GetWorldRotation(vehicleUid);
        var curTime = _gameTiming.CurTime;
        if (_lastWeaponUid == weaponUid &&
            (curTime < _nextSend || Math.Abs(Angle.ShortestDistance(_lastAngle, angle).Theta) < turretComponent.AimSendThreshold.Theta))
            return;

        _lastWeaponUid = weaponUid;
        _lastAngle = angle;
        _nextSend = curTime + turretComponent.AimSendInterval;

        RaisePredictiveEvent(new KsCrewVehicleAimEvent
        {
            Weapon = GetNetEntity(weaponUid),
            Target = GetNetCoordinates(_transformSystem.ToCoordinates(mouseMap)),
        });
    }
}
