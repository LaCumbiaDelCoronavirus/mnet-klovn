using System.Diagnostics.CodeAnalysis;
using Content.Shared._KS14.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Map;

namespace Content.Shared.Weapons.Ranged.Systems;

// Partial: guns a player fires without holding them (KsGetControlledGunEvent), kept apart from the upstream request code.
public abstract partial class SharedGunSystem
{
    [Dependency] private EntityQuery<GunComponent> _ksGunQuery = default!;

    /// <summary>
    ///     While a shot request is being handled, the entity it is fired as, when that is not the requesting player...
    /// </summary>
    private EntityUid? _ksShotShooter;

    /// <summary>
    ///     ...and that player. See <see cref="KsGetPredictingUser"/>.
    /// </summary>
    private EntityUid? _ksShotPlayer;

    /// <summary>
    ///     What a controlled gun's answer says about the shot beyond the gun itself.
    /// </summary>
    protected readonly record struct KsControlledShot(EntityCoordinates? Muzzle, float? MaxRange);

    /// <summary>
    ///     The gun <paramref name="playerUid"/>'s shoot input fires: one they control but do not hold
    ///         (<see cref="KsGetControlledGunEvent"/>), and otherwise <see cref="TryGetGun"/> of
    ///         <paramref name="shooterUid"/>. For a controlled gun, <paramref name="shooterUid"/> becomes what fires it.
    /// </summary>
    protected bool KsTryGetShotGun(EntityUid playerUid, ref EntityUid shooterUid, out Entity<GunComponent> gun, out KsControlledShot shot)
    {
        var ev = new KsGetControlledGunEvent();
        RaiseLocalEvent(playerUid, ref ev);
        if (ev.Gun is { } gunUid && _ksGunQuery.TryComp(gunUid, out var gunComponent))
        {
            shooterUid = ev.Shooter ?? playerUid;
            gun = (gunUid, gunComponent);
            shot = new KsControlledShot(ev.Muzzle, ev.MaxRange);
            return true;
        }

        shot = default;
        return TryGetGun(shooterUid, out gun);
    }

    /// <inheritdoc cref="KsTryGetShotGun(EntityUid, ref EntityUid, out Entity{GunComponent}, out KsControlledShot)"/>
    protected bool KsTryGetShotGun(EntityUid? playerUid, [NotNullWhen(true)] ref EntityUid? shooterUid, out Entity<GunComponent> gun, out KsControlledShot shot)
    {
        if (shooterUid is not { } uid)
        {
            gun = default;
            shot = default;
            return false;
        }

        if (playerUid is not { } knownPlayerUid)
        {
            shot = default;
            return TryGetGun(uid, out gun);
        }

        var found = KsTryGetShotGun(knownPlayerUid, ref uid, out gun, out shot);
        shooterUid = uid;
        return found;
    }

    /// <summary>
    ///     Opens the scope of one requested shot: <see cref="KsGetPredictingUser"/> of <paramref name="shooterUid"/> is
    ///         <paramref name="playerUid"/> until it is disposed, and the gun's aim is brought within the controlled
    ///         shot's range.
    /// </summary>
    protected KsShotScope KsBeginShot(EntityUid shooterUid, EntityUid? playerUid, Entity<GunComponent> gun, KsControlledShot shot)
    {
        var scope = new KsShotScope(this, _ksShotShooter, _ksShotPlayer);
        if (playerUid != null && playerUid != shooterUid)
        {
            _ksShotShooter = shooterUid;
            _ksShotPlayer = playerUid;
        }

        if (shot.MaxRange is { } maxRange)
            KsClampAim(gun, shot.Muzzle ?? Transform(shooterUid).Coordinates, maxRange);

        return scope;
    }

    /// <summary>
    ///     Brings <paramref name="gun"/>'s aim to at most <paramref name="maxRange"/> from <paramref name="from"/>, along
    ///         the same bearing and in the same frame. An aim on another map is left for the gun's own checks to refuse.
    /// </summary>
    private void KsClampAim(Entity<GunComponent> gun, EntityCoordinates from, float maxRange)
    {
        if (gun.Comp.ShootCoordinates is not { } aimCoordinates)
            return;

        var fromMap = TransformSystem.ToMapCoordinates(from);
        var aimMap = TransformSystem.ToMapCoordinates(aimCoordinates);
        if (fromMap.MapId != aimMap.MapId)
            return;

        var offset = aimMap.Position - fromMap.Position;
        var distance = offset.Length();
        if (distance <= maxRange || !float.IsFinite(distance))
            return;

        var clamped = new MapCoordinates(fromMap.Position + offset / distance * maxRange, fromMap.MapId);
        gun.Comp.ShootCoordinates = TransformSystem.ToCoordinates(aimCoordinates.EntityId, clamped);
    }

    /// <summary>
    ///     Restores the previous shot scope when disposed. See <see cref="KsBeginShot"/>.
    /// </summary>
    protected readonly struct KsShotScope : IDisposable
    {
        private readonly SharedGunSystem _system;
        private readonly EntityUid? _previousShooter;
        private readonly EntityUid? _previousPlayer;

        internal KsShotScope(SharedGunSystem system, EntityUid? previousShooter, EntityUid? previousPlayer)
        {
            _system = system;
            _previousShooter = previousShooter;
            _previousPlayer = previousPlayer;
        }

        public void Dispose()
        {
            _system._ksShotShooter = _previousShooter;
            _system._ksShotPlayer = _previousPlayer;
        }
    }
}
