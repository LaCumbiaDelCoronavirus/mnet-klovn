using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Shared._KS14.Weapons.Ranged;
using Content.Shared.Damage.Components;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Map;
using Robust.Shared.Physics;

namespace Content.Shared.Weapons.Ranged.Systems;

// Partial: guns a player fires without holding them (KsGetControlledGunEvent), kept apart from the upstream request code.
public abstract partial class SharedGunSystem
{
    [Dependency] private EntityQuery<GunComponent> _ksGunQuery = default!;
    [Dependency] private EntityQuery<RequireProjectileTargetComponent> _ksRequireTargetQuery = default!;
    [Dependency] private EntityQuery<FixturesComponent> _ksFixturesQuery = default!;
    [Dependency] private EntityLookupSystem _ksEntityLookupSystem = default!;

    /// <summary>
    ///     How far from a lying body's hitbox a controlled shot's aim point may be and still count as aimed at it: the
    ///         client aims where it last saw the body, and a body shoved along by the hits before is a little further on
    ///         by the time the server reads the aim.
    /// </summary>
    private const float KsAimedAtTolerance = 0.25f;

    // Scratch for KsTargetWhatIsAimedAt.
    private readonly HashSet<Entity<RequireProjectileTargetComponent>> _ksAimedAtCandidates = new();

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
    /// <param name="Controlled">Whether the gun is one the player controls (<see cref="KsGetControlledGunEvent"/>), not one held or worn.</param>
    protected readonly record struct KsControlledShot(EntityCoordinates? Muzzle, float? MaxRange, bool Controlled = false);

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
            shot = new KsControlledShot(ev.Muzzle, ev.MaxRange, Controlled: true);
            return true;
        }

        shot = default;
        return TryGetGun(shooterUid, out gun);
    }

    /// <summary>
    ///     The gun <paramref name="playerUid"/>'s fire input fires, as <see cref="KsTryGetShotGun(EntityUid, ref EntityUid, out Entity{GunComponent}, out KsControlledShot)"/>
    ///         finds it, with <paramref name="aimFrameUid"/> set to what the aim point is to be sent relative to: a
    ///         controlled gun's <see cref="KsGetControlledGunEvent.AimFrame"/>, else its shooter.
    /// </summary>
    protected bool KsTryGetAimedGun(EntityUid playerUid, ref EntityUid aimFrameUid, out Entity<GunComponent> gun)
    {
        var ev = new KsGetControlledGunEvent();
        RaiseLocalEvent(playerUid, ref ev);
        if (ev.Gun is { } gunUid && _ksGunQuery.TryComp(gunUid, out var gunComponent))
        {
            aimFrameUid = ev.AimFrame ?? ev.Shooter ?? playerUid;
            gun = (gunUid, gunComponent);
            return true;
        }

        return TryGetGun(aimFrameUid, out gun);
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

        // Before the clamp: a body aimed at past the weapon's aim range is still what the round flies on at.
        if (shot.Controlled)
            KsTargetWhatIsAimedAt(gun);

        if (shot.MaxRange is { } maxRange)
            KsClampAim(gun, shot.Muzzle ?? Transform(shooterUid).Coordinates, maxRange);

        return scope;
    }

    /// <summary>
    ///     Makes <paramref name="gun"/>'s target what its aim point lies on, when the shot names no target a lying body could
    ///         be (<see cref="RequireProjectileTargetComponent"/>): a lying body lets through every round not aimed at it.
    /// </summary>
    /// <remarks>
    ///     The target a client sends is the topmost clickable sprite under its cursor, and anything drawn over a body lying
    ///         at item depth takes the click: a round in flight, another body, or a client-side entity the server knows
    ///         nothing of, which it reads as no target at all. The round then went through the body on the server. The aim
    ///         point is in the same message, so both sides pick the same body from it: the nearest whose hard fixture's
    ///         bounds, widened by <see cref="KsAimedAtTolerance"/>, hold it.
    /// </remarks>
    private void KsTargetWhatIsAimedAt(Entity<GunComponent> gun)
    {
        if (gun.Comp.Target is { } targetUid && !TerminatingOrDeleted(targetUid) && _ksRequireTargetQuery.HasComp(targetUid))
            return;

        if (gun.Comp.ShootCoordinates is not { } aimCoordinates)
            return;

        var aimMap = TransformSystem.ToMapCoordinates(aimCoordinates);
        if (aimMap.MapId == MapId.Nullspace)
            return;

        _ksAimedAtCandidates.Clear();
        _ksEntityLookupSystem.GetEntitiesIntersecting(aimMap.MapId,
            Box2.CenteredAround(aimMap.Position, new Vector2(KsAimedAtTolerance * 2f)),
            _ksAimedAtCandidates,
            // Sundries: a body asleep with its collision off (a corpse's) is in no physics tree.
            LookupFlags.Dynamic | LookupFlags.Static | LookupFlags.Sundries | LookupFlags.Approximate);

        EntityUid? aimedAtUid = null;
        var aimedAtDistanceSquared = float.MaxValue;
        foreach (var candidate in _ksAimedAtCandidates)
        {
            // Cheapest first: a standing body is hit whatever the shot is aimed at.
            if (!candidate.Comp.Active || !_ksFixturesQuery.TryComp(candidate, out var fixturesComponent))
                continue;

            var (position, rotation) = TransformSystem.GetWorldPositionRotation(candidate);
            var distanceSquared = (position - aimMap.Position).LengthSquared();
            if (distanceSquared >= aimedAtDistanceSquared)
                continue;

            var bodyTransform = new Robust.Shared.Physics.Transform(position, rotation);
            foreach (var fixture in fixturesComponent.Fixtures.Values)
            {
                if (!fixture.Hard || !fixture.Shape.ComputeAABB(bodyTransform, 0).Enlarged(KsAimedAtTolerance).Contains(aimMap.Position))
                    continue;

                aimedAtUid = candidate.Owner;
                aimedAtDistanceSquared = distanceSquared;
                break;
            }
        }

        _ksAimedAtCandidates.Clear();
        if (aimedAtUid != null)
            gun.Comp.Target = aimedAtUid;
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
