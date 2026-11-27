using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Shared._KS14.Player;

/// <summary>
///     Filters by who can see a point, counting every place a player sees from rather than only where their body is:
///         their attached entity, what their eye follows, and every entity their session is view-subscribed to (a crew
///         vehicle's outside viewer, a camera, a z-level below). <see cref="Filter.Pvs(MapCoordinates, float, IEntityManager?, ISharedPlayerManager?, IConfigurationManager?)"/>
///         counts the body alone, so someone looking out of a vehicle from inside it is never sent what happens
///         outside.
/// </summary>
/// <remarks>
///     The same rule as the engine's otherwise: on the point's map, within the PVS range times
///         <c>rangeMultiplier</c>, everyone with PVS off. A view entity's range is also scaled by its own eye's PVS
///         scale. View subscriptions exist on the server only; the client sees its own body and eye target.
/// </remarks>
public static class KsViewFilter
{
    /// <summary>
    ///     Adds every player who can see <paramref name="origin"/> from any of their view points.
    /// </summary>
    public static Filter AddPlayersByView(this Filter filter,
        MapCoordinates origin,
        EntityManager entityManager,
        float rangeMultiplier = 2f,
        ISharedPlayerManager? playerManager = null,
        IConfigurationManager? configurationManager = null)
    {
        IoCManager.Resolve(ref playerManager, ref configurationManager);

        if (!configurationManager.GetCVar(CVars.NetPVS))
            return filter.AddAllPlayers(playerManager);

        var range = configurationManager.GetCVar(CVars.NetMaxUpdateRange) * rangeMultiplier;
        var transformSystem = entityManager.System<SharedTransformSystem>();
        var transformQuery = entityManager.TransformQuery;
        var eyeQuery = entityManager.GetEntityQuery<EyeComponent>();

        bool Sees(EntityUid viewUid, float scale)
        {
            return transformQuery.TryComp(viewUid, out var transformComponent) &&
                   transformComponent.MapID == origin.MapId &&
                   (transformSystem.GetWorldPosition(transformComponent) - origin.Position).Length() < range * scale;
        }

        return filter.AddWhere(session =>
        {
            if (session.AttachedEntity is not { } attachedUid)
                return false;

            // The body exactly as the engine counts it.
            if (Sees(attachedUid, 1f))
                return true;

            if (eyeQuery.TryComp(attachedUid, out var attachedEye) && attachedEye.Target is { } targetUid && Sees(targetUid, 1f))
                return true;

            foreach (var viewUid in session.ViewSubscriptions)
            {
                var scale = eyeQuery.CompOrNull(viewUid)?.PvsScale ?? 1f;
                if (Sees(viewUid, MathF.Max(scale, 1f)))
                    return true;
            }

            return false;
        }, playerManager);
    }

    /// <summary>
    ///     Every player who can see <paramref name="origin"/>; see <see cref="AddPlayersByView"/>.
    /// </summary>
    public static Filter View(EntityUid origin,
        EntityManager entityManager,
        ISharedPlayerManager? playerManager = null,
        IConfigurationManager? configurationManager = null)
    {
        var mapCoordinates = entityManager.System<SharedTransformSystem>().GetMapCoordinates(origin);
        return Filter.Empty().AddPlayersByView(mapCoordinates, entityManager, playerManager: playerManager, configurationManager: configurationManager);
    }

    /// <summary>
    ///     Every player who can see <paramref name="origin"/>, except whoever is attached to it.
    /// </summary>
    public static Filter ViewExcept(EntityUid origin,
        EntityManager entityManager,
        ISharedPlayerManager? playerManager = null,
        IConfigurationManager? configurationManager = null)
    {
        return View(origin, entityManager, playerManager: playerManager, configurationManager: configurationManager).RemoveWhereAttachedEntity(uid => uid == origin);
    }

    /// <summary>
    ///     Every player who can see <paramref name="origin"/>.
    /// </summary>
    public static Filter View(EntityCoordinates origin,
        EntityManager entityManager,
        ISharedPlayerManager? playerManager = null,
        IConfigurationManager? configurationManager = null)
    {
        var mapCoordinates = entityManager.System<SharedTransformSystem>().ToMapCoordinates(origin);
        return Filter.Empty().AddPlayersByView(mapCoordinates, entityManager, playerManager: playerManager, configurationManager: configurationManager);
    }

    /// <summary>
    ///     Adds the players who see one of <paramref name="entities"/> from a view point while their body is on another
    ///         map, as someone looking out of a vehicle does. Players whose body is on the same map as an entity are left
    ///         as the caller's filter has them, so a caller that leaves out the player who predicted the effect still
    ///         does.
    /// </summary>
    public static Filter AddRemoteViewersOf(this Filter filter,
        IReadOnlyList<EntityUid> entities,
        EntityManager entityManager,
        ISharedPlayerManager? playerManager = null,
        IConfigurationManager? configurationManager = null)
    {
        IoCManager.Resolve(ref playerManager, ref configurationManager);

        if (!configurationManager.GetCVar(CVars.NetPVS))
            return filter;

        var transformSystem = entityManager.System<SharedTransformSystem>();
        var transformQuery = entityManager.TransformQuery;
        foreach (var uid in entities)
        {
            if (!transformQuery.TryComp(uid, out var transformComponent))
                continue;

            var origin = transformSystem.GetMapCoordinates(transformComponent);
            var viewers = Filter.Empty().AddPlayersByView(origin, entityManager, playerManager: playerManager, configurationManager: configurationManager);
            foreach (var session in viewers.Recipients)
            {
                if (session.AttachedEntity is { } attachedUid &&
                    transformQuery.TryComp(attachedUid, out var attachedTransform) &&
                    attachedTransform.MapID != origin.MapId)
                {
                    filter.AddPlayer(session);
                }
            }
        }

        return filter;
    }
}
