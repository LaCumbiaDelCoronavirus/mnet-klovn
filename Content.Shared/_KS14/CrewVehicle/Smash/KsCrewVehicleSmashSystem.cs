using System.Numerics;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs.Components;
using Content.Shared.Vehicle.Components;
using Content.Shared.Whitelist;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.CrewVehicle.Smash;

/// <summary>
///     A crew vehicle hitting something fast enough: a smashable obstacle breaks and the hull goes on, slower; a wall is
///         damaged and the hull stops dead (<see cref="KsCrewVehicleCrashedEvent"/>). Either costs the hull some damage.
/// </summary>
/// <remarks>
///     <para>
///         Runs on the contact's start, which physics reports after the step's speeds are set and before it resolves
///             the collision, so the hull's velocity is still the one it hit with. Mobs are run-over's.
///     </para>
///     <para>
///         Predicted by the driver's client, which predicts the hull; the obstacle breaks when the server's state says
///             so. Nothing here is random.
///     </para>
/// </remarks>
public sealed partial class KsCrewVehicleSmashSystem : EntitySystem
{
    [Dependency] private DamageableSystem _damageableSystem = default!;
    [Dependency] private EntityWhitelistSystem _whitelistSystem = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private SharedAudioSystem _audioSystem = default!;
    [Dependency] private SharedPhysicsSystem _physicsSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    [Dependency] private EntityQuery<MobStateComponent> _mobStateQuery = default!;
    [Dependency] private EntityQuery<VehicleComponent> _vehicleQuery = default!;

    [SubscribeLocalEvent]
    private void OnStartCollide(Entity<KsCrewVehicleSmashComponent> hull, ref StartCollideEvent args)
    {
        if (_gameTiming.ApplyingState ||
            args.OurFixtureId != hull.Comp.Fixture ||
            !args.OtherFixture.Hard ||
            _mobStateQuery.HasComp(args.OtherEntity))
        {
            return;
        }

        var otherUid = args.OtherEntity;
        var smashable = _whitelistSystem.IsWhitelistPass(hull.Comp.Smashable, otherUid);
        var wall = !smashable && _whitelistSystem.IsWhitelistPass(hull.Comp.Walls, otherUid);
        if (!smashable && !wall)
            return;

        var velocity = args.OurBody.LinearVelocity;
        var impactSpeed = ImpactSpeed(hull.Owner, velocity, otherUid, args);
        if (impactSpeed < hull.Comp.MinSpeed)
            return;

        var driverUid = _vehicleQuery.CompOrNull(hull.Owner)?.Operator;
        var origin = driverUid ?? hull.Owner;

        if (smashable)
        {
            _damageableSystem.TryChangeDamage(otherUid, hull.Comp.SmashDamage, origin: origin);
            _physicsSystem.SetLinearVelocity(hull.Owner, velocity * hull.Comp.SmashSpeedRetained, body: args.OurBody);
            _audioSystem.PlayPredicted(hull.Comp.SmashSound, otherUid, driverUid);
            DamageSelf(hull, hull.Comp.SmashSelfDamage, origin);
            return;
        }

        if (hull.Comp.WallDamage is { } wallDamage)
            _damageableSystem.TryChangeDamage(otherUid, wallDamage, origin: origin);

        _physicsSystem.SetLinearVelocity(hull.Owner, Vector2.Zero, body: args.OurBody);
        _audioSystem.PlayPredicted(hull.Comp.CrashSound, hull.Owner, driverUid);
        DamageSelf(hull, hull.Comp.WallSelfDamage, origin);

        var ev = new KsCrewVehicleCrashedEvent(otherUid, impactSpeed);
        RaiseLocalEvent(hull.Owner, ref ev);
    }

    /// <summary>
    ///     How fast the hull was going into <paramref name="otherUid"/>: along the contact's normal, or failing one, the
    ///         line between them.
    /// </summary>
    private float ImpactSpeed(EntityUid hullUid, Vector2 velocity, EntityUid otherUid, StartCollideEvent args)
    {
        if (args.PointCount > 0 && args.WorldNormal.LengthSquared() > 0.0001f)
            return MathF.Abs(Vector2.Dot(velocity, args.WorldNormal));

        var away = _transformSystem.GetWorldPosition(otherUid) - _transformSystem.GetWorldPosition(hullUid);
        return away.LengthSquared() < 0.0001f ? 0f : MathF.Max(0f, Vector2.Dot(velocity, Vector2.Normalize(away)));
    }

    private void DamageSelf(Entity<KsCrewVehicleSmashComponent> hull, DamageSpecifier? damage, EntityUid origin)
    {
        if (damage == null || _gameTiming.CurTime < hull.Comp.NextSelfDamage)
            return;

        hull.Comp.NextSelfDamage = _gameTiming.CurTime + hull.Comp.SelfDamageCooldown;
        Dirty(hull);
        _damageableSystem.TryChangeDamage(hull.Owner, damage, origin: origin);
    }
}
