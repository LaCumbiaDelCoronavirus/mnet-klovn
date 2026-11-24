using Content.Server._KS14.Throwing;
using Content.Shared.Database;
using Robust.Shared.Map;

namespace Content.Server.Explosion.EntitySystems;

// Explosions send mobs with KsThrowableMobComponent flying, marked to slam into what they hit.
public sealed partial class ExplosionSystem
{
    [Dependency] private KsMobThrowSystem _ksMobThrowSystem = default!;

    /// <summary>
    ///     Throws a mob away from <paramref name="epicenter"/>, as fast and as far as <paramref name="throwForce"/>
    ///         and its <see cref="KsThrowableMobComponent"/> say.
    /// </summary>
    /// <param name="mob">Its transform is null for something anchored or in a container, as
    ///     <see cref="ProcessEntity"/> has it: that is never thrown.</param>
    private void KsThrowMob(Entity<TransformComponent?> mob, MapCoordinates epicenter, float throwForce)
    {
        // Anchored or in a container: never thrown, and cheaper to skip here than to look the transform up again.
        if (mob.Comp == null || !_ksMobThrowSystem.TryThrowMob(mob, epicenter, throwForce, out var thrown))
            return;

        _adminLogger.Add(LogType.ExplosionHit, LogImpact.Medium,
            $"Explosion at {epicenter:epicenter} threw {ToPrettyString(mob):subject} {thrown.Value.Distance:distance} tiles at {thrown.Value.Speed:speed} m/s (throw force {throwForce:force})");
    }
}
