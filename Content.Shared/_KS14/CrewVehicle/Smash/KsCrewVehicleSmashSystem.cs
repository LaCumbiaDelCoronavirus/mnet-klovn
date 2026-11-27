using System.Numerics;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Destructible;
using Content.Shared.Destructible.Thresholds.Triggers;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Components;
using Content.Shared.Tag;
using Content.Shared.Vehicle.Components;
using Content.Shared.Whitelist;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.CrewVehicle.Smash;

/// <summary>
///     A crew vehicle hitting something hard fast enough. Every such thing is one of: a mob (run-over's), a loose body
///         (the push controller's), or this system's: a wall, which is damaged and stops the hull dead; a door that would
///         open for the hull, which opens; and anything else, which takes <see cref="KsCrewVehicleSmashComponent.SmashDamage"/>
///         and either breaks, letting the hull drive on slower, or stands and is crashed into like a wall. Either costs
///         the hull some damage, and a crash raises <see cref="KsCrewVehicleCrashedEvent"/>. Nothing stops the hull
///         silently.
/// </summary>
/// <remarks>
///     <para>
///         Runs on the contact's start, which physics reports after the step's speeds are set and before it resolves
///             the collision, so the hull's velocity is still the one it hit with.
///     </para>
///     <para>
///         Predicted by the driver's client, which predicts the hull; the obstacle breaks when the server's state says
///             so. Whether it breaks is decided the same way on both sides, from its damage and its
///             <c>Destructible</c>. Nothing here is random.
///     </para>
/// </remarks>
public sealed partial class KsCrewVehicleSmashSystem : EntitySystem
{
    [Dependency] private DamageableSystem _damageableSystem = default!;
    [Dependency] private EntityWhitelistSystem _whitelistSystem = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private KsCrewVehicleSmashedSystem _smashedSystem = default!;
    [Dependency] private SharedAudioSystem _audioSystem = default!;
    [Dependency] private SharedDestructibleSystem _destructibleSystem = default!;
    [Dependency] private SharedDoorSystem _doorSystem = default!;
    [Dependency] private SharedPhysicsSystem _physicsSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private TagSystem _tagSystem = default!;

    [Dependency] private EntityQuery<DamageableComponent> _damageableQuery = default!;
    [Dependency] private EntityQuery<DestructibleComponent> _destructibleQuery = default!;
    [Dependency] private EntityQuery<DoorComponent> _doorQuery = default!;
    [Dependency] private EntityQuery<MobStateComponent> _mobStateQuery = default!;
    [Dependency] private EntityQuery<VehicleComponent> _vehicleQuery = default!;

    [SubscribeLocalEvent]
    private void OnStartCollide(Entity<KsCrewVehicleSmashComponent> hull, ref StartCollideEvent args)
    {
        // Mobs are run-over's, and loose bodies (crates, parked hulls, unanchored furniture) the push controller's.
        if (_gameTiming.ApplyingState ||
            args.OurFixtureId != hull.Comp.Fixture ||
            !args.OurFixture.Hard ||
            !args.OtherFixture.Hard ||
            args.OtherBody.BodyType == BodyType.Dynamic ||
            _mobStateQuery.HasComp(args.OtherEntity))
        {
            return;
        }

        var otherUid = args.OtherEntity;
        var velocity = args.OurBody.LinearVelocity;
        var impactSpeed = ImpactSpeed(hull.Owner, velocity, otherUid, args);
        if (impactSpeed < hull.Comp.MinSpeed)
            return;

        // A door that would open for the hull opens itself, on its own collision.
        if (_doorQuery.TryComp(otherUid, out var doorComponent) &&
            doorComponent.BumpOpen &&
            _tagSystem.HasTag(hull.Owner, SharedDoorSystem.DoorBumpTag) &&
            _doorSystem.CanOpen(otherUid, doorComponent, hull.Owner, quiet: true))
        {
            return;
        }

        var driverUid = _vehicleQuery.CompOrNull(hull.Owner)?.Operator;
        var origin = driverUid ?? hull.Owner;

        if (_whitelistSystem.IsWhitelistPass(hull.Comp.Walls, otherUid))
        {
            if (hull.Comp.WallDamage is { } wallDamage)
                _damageableSystem.TryChangeDamage(otherUid, wallDamage, origin: origin);
        }
        else if (_damageableQuery.HasComp(otherUid))
        {
            // From what it had and what the hit dealt, not from what it has after: the client may not apply the damage
            //      (the server's state brings it), and must still decide as the server does.
            var before = _damageableSystem.GetTotalDamage(otherUid);
            _damageableSystem.TryChangeDamage(otherUid, hull.Comp.SmashDamage, out var dealt, origin: origin);

            if (Broke(otherUid, before + dealt.GetTotal()))
            {
                Smash(hull, otherUid, velocity, args.OurBody, driverUid, origin);
                return;
            }
        }

        Crash(hull, otherUid, impactSpeed, velocity, args.OurBody, driverUid, origin);
    }

    /// <summary>
    ///     Whether <paramref name="otherUid"/> is gone, or <paramref name="totalDamage"/> is as much as breaks it.
    /// </summary>
    private bool Broke(EntityUid otherUid, FixedPoint2 totalDamage)
    {
        if (TerminatingOrDeleted(otherUid) || EntityManager.IsQueuedForDeletion(otherUid))
            return true;

        // The component isn't networked, so a client has its prototype's, and the server-only behaviors are stripped
        //      from that: it can't tell which threshold destroys. Reading the damage of the triggers instead, an
        //      approximation that the server's state corrects, is the best it can do.
        if (!_destructibleQuery.TryComp(otherUid, out var destructibleComponent) &&
            !(MetaData(otherUid).EntityPrototype is { } prototype &&
              prototype.TryComp(out destructibleComponent, Factory)))
        {
            return false;
        }

        var destroyedAt = _destructibleSystem.DestroyedAt(otherUid, destructibleComponent);
        if (destroyedAt == FixedPoint2.MaxValue)
        {
            foreach (var threshold in destructibleComponent.Thresholds)
            {
                if (threshold.Behaviors.Count == 0 && threshold.Trigger is DamageTrigger trigger)
                    destroyedAt = FixedPoint2.Min(destroyedAt, trigger.Damage);
            }
        }

        return totalDamage >= destroyedAt;
    }

    private void Smash(Entity<KsCrewVehicleSmashComponent> hull,
        EntityUid otherUid,
        Vector2 velocity,
        PhysicsComponent hullBody,
        EntityUid? driverUid,
        EntityUid origin)
    {
        if (!TerminatingOrDeleted(otherUid))
            _smashedSystem.MarkSmashed(otherUid, hull.Owner, hull.Comp.SmashedPassTime);

        if (!_smashedSystem.IsReplayedSmash(hull.Owner, otherUid))
        {
            _physicsSystem.SetLinearVelocity(hull.Owner, velocity * hull.Comp.SmashSpeedRetained, body: hullBody);
            _smashedSystem.RecordPredictedSmash(hull.Owner, otherUid, velocity * hull.Comp.SmashSpeedRetained, hull.Comp.SmashedPassTime);
        }

        _audioSystem.PlayPredicted(hull.Comp.SmashSound, otherUid, driverUid);
        SpawnEffect(hull.Comp.SmashEffect, otherUid);
        DamageSelf(hull, hull.Comp.SmashSelfDamage, origin);
    }

    private void Crash(Entity<KsCrewVehicleSmashComponent> hull,
        EntityUid otherUid,
        float impactSpeed,
        Vector2 velocity,
        PhysicsComponent hullBody,
        EntityUid? driverUid,
        EntityUid origin)
    {
        _physicsSystem.SetLinearVelocity(hull.Owner, Vector2.Zero, body: hullBody);
        _audioSystem.PlayPredicted(hull.Comp.CrashSound, hull.Owner, driverUid);
        SpawnEffect(hull.Comp.CrashEffect, otherUid);
        DamageSelf(hull, hull.Comp.WallSelfDamage, origin);

        var ev = new KsCrewVehicleCrashedEvent(otherUid, impactSpeed, velocity);
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

    /// <summary>
    ///     A one-shot effect where <paramref name="atUid"/> is: predicted, the first time through only.
    /// </summary>
    private void SpawnEffect(EntProtoId? effect, EntityUid atUid)
    {
        if (effect is { } prototype && _gameTiming.IsFirstTimePredicted)
            PredictedSpawnAtPosition(prototype, Transform(atUid).Coordinates);
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
