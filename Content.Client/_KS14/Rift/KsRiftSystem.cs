using System.Numerics;
using Content.Client.Examine;
using Content.Client.Viewport;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Graphics;
using Robust.Shared.Map;

namespace Content.Client._KS14.Rift;

public sealed class KsRiftSystem : EntitySystem
{
    [Dependency] private readonly TransformSystem _transformSystem = default!;
    [Dependency] private readonly ExamineSystem _examineSystem = default!;

    public IEnumerable<(Entity<KsRiftComponent, TransformComponent>, (Vector2, Angle))> GetVisibleRiftsEnumerator(IEye eye, ScalingViewport viewport)
    {
        var mapId = eye.Position.MapId;
        var eyeSize = viewport.ViewportSize / EyeManager.PixelsPerMeter;
        var eyeMaxRange = MathF.Max(eyeSize.X, eyeSize.Y);
        var eqe = EntityQueryEnumerator<KsRiftComponent, TransformComponent>();

        while (eqe.MoveNext(out var uid, out var riftComponent, out var transformComponent))
        {
            if (transformComponent.MapID != mapId)
                continue;

            var (worldPosition, worldRotation) = _transformSystem.GetWorldPositionRotation(transformComponent);
            if (!_examineSystem.InRangeUnOccluded(eye.Position, new MapCoordinates(worldPosition, mapId), eyeMaxRange, null, entMan: EntityManager))
                continue;

            yield return ((uid, riftComponent, transformComponent), (worldPosition, worldRotation));
        }
    }
}
