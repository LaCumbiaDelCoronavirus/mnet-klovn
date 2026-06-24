using System.Numerics;
using Robust.Client.GameObjects;
using Robust.Shared.Map;

namespace Content.Client._KS14.Rift;

public sealed class KsRiftSystem : EntitySystem
{
    [Dependency] private readonly TransformSystem _transformSystem = default!;

    public IEnumerable<(Entity<KsRiftComponent, TransformComponent>, (Vector2, Angle))> GetVisibleRiftsEnumerator(MapId mapId)
    {
        var eqe = EntityQueryEnumerator<KsRiftComponent, TransformComponent>();
        while (eqe.MoveNext(out var uid, out var riftComponent, out var transformComponent))
        {
            if (transformComponent.MapID != mapId)
                continue;

            var (worldPosition, worldRotation) = _transformSystem.GetWorldPositionRotation(transformComponent);
            yield return ((uid, riftComponent, transformComponent), (worldPosition, worldRotation));
        }
    }
}
