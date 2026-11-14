using System.Numerics;
using Content.Shared.Movement.Components;
using Content.Shared.Throwing;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;

namespace Content.Shared._KS14.Movement.StopsDives;

/// <summary>
///     Ends a dive where it runs into something with <see cref="KsStopsDivesComponent"/>.
/// </summary>
/// <remarks>
///     A dive is a throw of the diver (<c>SharedJumpAbilitySystem</c>), so ending it is landing the throw and stopping
///         it, which is also what tells the jump system it is over. Shared: a player's own dive is predicted, and stops
///         on their client as it does on the server. Only a contact that starts stops a dive, so diving out of the wire
///         one is standing in works.
/// </remarks>
public sealed partial class KsStopsDivesSystem : EntitySystem
{
    [Dependency] private SharedPhysicsSystem _physicsSystem = default!;
    [Dependency] private ThrownItemSystem _thrownItemSystem = default!;
    [Dependency] private EntityQuery<ActiveLeaperComponent> _activeLeaperQuery = default!;
    [Dependency] private EntityQuery<ThrownItemComponent> _thrownItemQuery = default!;
    [Dependency] private EntityQuery<PhysicsComponent> _physicsQuery = default!;

    [SubscribeLocalEvent]
    private void OnStartCollide(Entity<KsStopsDivesComponent> entity, ref StartCollideEvent args)
    {
        var diverUid = args.OtherEntity;

        // The jump system may have taken the leaper off already this very contact: deferred, so it is still here.
        if (!_activeLeaperQuery.HasComp(diverUid) ||
            !_thrownItemQuery.TryComp(diverUid, out var thrownItemComponent) ||
            !_physicsQuery.TryComp(diverUid, out var physicsComponent))
            return;

        _physicsSystem.SetLinearVelocity(diverUid, Vector2.Zero, body: physicsComponent);
        _thrownItemSystem.LandComponent(diverUid, thrownItemComponent, physicsComponent, playSound: false);
        _thrownItemSystem.StopThrow(diverUid, thrownItemComponent);
    }
}
