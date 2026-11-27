using Content.Shared._KS14.Projectiles;
using Content.Shared.Projectiles;

namespace Content.Shared._Trauma.Projectiles;

public sealed partial class PredictedProjectileSystem
{
    /// <summary>
    ///     A round that has just penetrated <paramref name="target"/> asks it whether it goes on from here
    ///         (<see cref="KsProjectilePenetrateAttemptEvent"/>): something it went through can take it elsewhere instead,
    ///         such as a crew vehicle's interior.
    /// </summary>
    /// <returns>Whether the round flies on where it is.</returns>
    private bool KsTryContinuePenetrating(Entity<ProjectileComponent> projectile, EntityUid target)
    {
        var velocity = _physicsQuery.TryComp(projectile.Owner, out var physicsComponent)
            ? _physicsSystem.GetMapLinearVelocity(projectile.Owner, physicsComponent)
            : default;

        var ev = new KsProjectilePenetrateAttemptEvent(projectile.Owner, velocity, _transformSystem.GetMapCoordinates(projectile.Owner));
        RaiseLocalEvent(target, ref ev);
        return !ev.Cancelled;
    }
}
