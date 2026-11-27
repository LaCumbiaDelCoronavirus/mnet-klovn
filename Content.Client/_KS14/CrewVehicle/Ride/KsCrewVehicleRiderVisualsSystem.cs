using Content.Shared._KS14.CrewVehicle.Ride;
using Robust.Client.GameObjects;

namespace Content.Client._KS14.CrewVehicle.Ride;

/// <summary>
///     Draws a rider over the hull it rides: at the hull's depth it would sort behind the hull's sprite on the deck's
///         far half. Its own depth is put back when it gets off.
/// </summary>
public sealed partial class KsCrewVehicleRiderVisualsSystem : EntitySystem
{
    [Dependency] private SpriteSystem _spriteSystem = default!;

    [Dependency] private EntityQuery<KsCrewVehicleRiderDrawDepthComponent> _drawDepthQuery = default!;
    [Dependency] private EntityQuery<SpriteComponent> _spriteQuery = default!;

    [SubscribeLocalEvent]
    private void OnRiderStartup(Entity<KsCrewVehicleRiderComponent> rider, ref ComponentStartup args)
    {
        Apply(rider);
    }

    [SubscribeLocalEvent]
    private void OnRiderHandleState(Entity<KsCrewVehicleRiderComponent> rider, ref AfterAutoHandleStateEvent args)
    {
        Apply(rider);
    }

    [SubscribeLocalEvent]
    private void OnRiderShutdown(Entity<KsCrewVehicleRiderComponent> rider, ref ComponentShutdown args)
    {
        if (!_drawDepthQuery.TryComp(rider, out var drawDepthComponent))
            return;

        if (_spriteQuery.TryComp(rider, out var spriteComponent))
            _spriteSystem.SetDrawDepth((rider.Owner, spriteComponent), drawDepthComponent.OriginalDrawDepth);

        RemComp(rider, drawDepthComponent);
    }

    private void Apply(Entity<KsCrewVehicleRiderComponent> rider)
    {
        if (!_spriteQuery.TryComp(rider, out var spriteComponent) ||
            !_spriteQuery.TryComp(rider.Comp.Hull, out var hullSpriteComponent))
            return;

        var drawDepthComponent = _drawDepthQuery.CompOrNull(rider);
        if (drawDepthComponent == null)
        {
            drawDepthComponent = AddComp<KsCrewVehicleRiderDrawDepthComponent>(rider);
            drawDepthComponent.OriginalDrawDepth = spriteComponent.DrawDepth;
        }

        _spriteSystem.SetDrawDepth((rider.Owner, spriteComponent), hullSpriteComponent.DrawDepth + 1);
    }
}

/// <summary>
///     On a rider on this client: the draw depth it had before it got on.
/// </summary>
[RegisterComponent, Access(typeof(KsCrewVehicleRiderVisualsSystem))]
public sealed partial class KsCrewVehicleRiderDrawDepthComponent : Component
{
    [ViewVariables(VVAccess.ReadOnly)]
    public int OriginalDrawDepth;
}
