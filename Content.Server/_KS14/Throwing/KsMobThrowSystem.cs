using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Shared._KS14.Throwing;
using Content.Shared.Buckle;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;

namespace Content.Server._KS14.Throwing;

/// <summary>
///     How far and how fast a mob was thrown.
/// </summary>
public readonly record struct KsMobThrow(float Distance, float Speed);

/// <summary>
///     Sends a mob with <see cref="KsThrowableMobComponent"/> flying, marked to slam into what it hits: away from an
///         explosion, or along whatever struck it. Unpredicted: the server's throw reaches every client as state.
/// </summary>
public sealed partial class KsMobThrowSystem : EntitySystem
{
    [Dependency] private IRobustRandom _robustRandom = default!;
    [Dependency] private KsThrownSlamSystem _thrownSlamSystem = default!;
    [Dependency] private SharedContainerSystem _containerSystem = default!;
    [Dependency] private PullingSystem _pullingSystem = default!;
    [Dependency] private SharedBuckleSystem _buckleSystem = default!;
    [Dependency] private SharedPhysicsSystem _physicsSystem = default!;
    [Dependency] private SharedStunSystem _stunSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private ThrowingSystem _throwingSystem = default!;

    [Dependency] private EntityQuery<KsThrowableMobComponent> _throwableQuery = default!;
    [Dependency] private EntityQuery<PhysicsComponent> _physicsQuery = default!;
    [Dependency] private EntityQuery<PullableComponent> _pullableQuery = default!;

    /// <summary>
    ///     Throws a mob away from <paramref name="epicenter"/>, as fast and as far as <paramref name="force"/> and its
    ///         <see cref="KsThrowableMobComponent"/> say. Standing on the epicentre, it goes any way at all.
    /// </summary>
    public bool TryThrowMob(Entity<TransformComponent?> mob,
        MapCoordinates epicenter,
        float force,
        [NotNullWhen(true)] out KsMobThrow? thrown)
    {
        thrown = null;
        if (!Resolve(mob, ref mob.Comp, logMissing: false))
            return false;

        var direction = _transformSystem.GetWorldPosition(mob.Comp) - epicenter.Position;
        if (direction.IsLengthZero())
            direction = _robustRandom.NextVector2();

        return TryThrowMob(mob, direction, force, out thrown);
    }

    /// <summary>
    ///     Throws a mob along <paramref name="direction"/>, as fast and as far as <paramref name="force"/> and its
    ///         <see cref="KsThrowableMobComponent"/> say.
    /// </summary>
    /// <param name="direction">Which way; its length doesn't matter.</param>
    public bool TryThrowMob(Entity<TransformComponent?> mob,
        Vector2 direction,
        float force,
        [NotNullWhen(true)] out KsMobThrow? thrown)
    {
        thrown = null;

        // Cheapest first: nearly everything an explosion touches is no mob. Anchored or held in a container, it stays.
        if (!_throwableQuery.TryComp(mob, out var throwableComponent) ||
            !Resolve(mob, ref mob.Comp, logMissing: false) ||
            mob.Comp is not { Anchored: false } transformComponent ||
            _containerSystem.IsEntityInContainer(mob) ||
            force < throwableComponent.MinThrowForce ||
            direction.IsLengthZero() ||
            EntityManager.IsQueuedForDeletion(mob) ||
            !_physicsQuery.TryComp(mob, out var physicsComponent) ||
            physicsComponent.BodyType != BodyType.KinematicController ||
            _buckleSystem.IsBuckled(mob.Owner))
        {
            return false;
        }

        // Whoever holds them loses them: a pull's joint would yank them back mid-flight.
        if (_pullableQuery.TryComp(mob, out var pullableComponent) && pullableComponent.BeingPulled)
            _pullingSystem.TryStopPull(mob, pullableComponent);

        if (throwableComponent.KnockdownTime is { } knockdownTime)
            _stunSystem.TryKnockdown(mob.Owner, knockdownTime, drop: false);

        var distance = MathF.Min(force * throwableComponent.DistancePerForce, throwableComponent.MaxDistance);
        var speed = MathF.Min(force * throwableComponent.SpeedPerForce, throwableComponent.MaxSpeed);
        _throwingSystem.TryThrow(mob, direction.Normalized() * distance, physicsComponent, transformComponent,
            baseThrowSpeed: speed, predicted: false);
        _thrownSlamSystem.MarkThrown(mob, throwableComponent.Slam);

        // A throw adds to the speed they already had, from running or from another blast a moment ago. The cap is on
        // how fast they end up, not on what each throw adds.
        var velocity = physicsComponent.LinearVelocity;
        if (velocity.LengthSquared() > throwableComponent.MaxSpeed * throwableComponent.MaxSpeed)
            _physicsSystem.SetLinearVelocity(mob, velocity.Normalized() * throwableComponent.MaxSpeed, body: physicsComponent);

        thrown = new KsMobThrow(distance, speed);
        return true;
    }
}
