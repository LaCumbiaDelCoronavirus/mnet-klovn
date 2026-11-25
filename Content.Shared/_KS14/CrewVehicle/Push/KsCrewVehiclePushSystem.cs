using System.Numerics;
using Content.Shared.Item;
using Content.Shared.Mobs.Components;
using Robust.Shared.Network;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Dynamics;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.CrewVehicle.Push;

/// <summary>
///     Marks what a driven hull is pushing (<see cref="KsCrewVehiclePushedComponent"/>), so that the driver's client
///         predicts it too; the pushing itself is <see cref="KsCrewVehiclePushController"/>'s.
/// </summary>
public sealed partial class KsCrewVehiclePushSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private INetManager _netManager = default!;
    [Dependency] private SharedPhysicsSystem _physicsSystem = default!;
    [Dependency] private KsCrewVehiclePushController _pushController = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    [Dependency] private EntityQuery<ItemComponent> _itemQuery = default!;
    [Dependency] private EntityQuery<MobStateComponent> _mobStateQuery = default!;
    [Dependency] private EntityQuery<KsCrewVehiclePushComponent> _pushQuery = default!;
    [Dependency] private EntityQuery<KsCrewVehiclePushedComponent> _pushedQuery = default!;

    /// <summary>
    ///     Whether a driven hull pushes this body: a loose (Dynamic) one, hard against the hull's body, and no mob (run-over's)
    ///         or item (hulls don't collide with those at all).
    /// </summary>
    public bool IsPushable(EntityUid uid, PhysicsComponent body, Fixture fixture)
    {
        return fixture.Hard &&
               body.BodyType == BodyType.Dynamic &&
               !_mobStateQuery.HasComp(uid) &&
               !_itemQuery.HasComp(uid);
    }

    /// <summary>
    ///     Marks <paramref name="uid"/> as moved by <paramref name="hullUid"/>, so that the hull's driver's client
    ///         predicts it, until it is at rest and the hull no longer touches it.
    /// </summary>
    public void MarkPushed(EntityUid uid, EntityUid hullUid)
    {
        if (_gameTiming.ApplyingState ||
            _pushedQuery.TryComp(uid, out var existing) && existing.Hulls.Contains(hullUid))
        {
            return;
        }

        var pushedComponent = EnsureComp<KsCrewVehiclePushedComponent>(uid);
        pushedComponent.Hulls.Add(hullUid);
        Dirty(uid, pushedComponent);
    }

    [SubscribeLocalEvent]
    private void OnStartCollide(Entity<KsCrewVehiclePushComponent> hull, ref StartCollideEvent args)
    {
        if (_gameTiming.ApplyingState ||
            args.OurFixtureId != hull.Comp.Fixture ||
            !IsPushable(args.OtherEntity, args.OtherBody, args.OtherFixture))
        {
            return;
        }

        MarkPushed(args.OtherEntity, hull.Owner);

        // The contact's normal points from fixture A to B; ours may be either.
        Vector2? normal = null;
        if (args.PointCount > 0 && args.WorldNormal.LengthSquared() > 0.25f)
        {
            var away = _transformSystem.GetWorldPosition(args.OtherEntity) - _transformSystem.GetWorldPosition(hull.Owner);
            normal = Vector2.Normalize(Vector2.Dot(args.WorldNormal, away) < 0f ? -args.WorldNormal : args.WorldNormal);
        }

        _pushController.ShoveOnImpact((hull.Owner, args.OurBody), hull.Comp, (args.OtherEntity, args.OtherBody), normal);
    }

    /// <summary>
    ///     Lets go of what a hull pushed once the hull no longer touches it and it has come to rest, or the hull is gone.
    ///         Not when the contact ends: the driver's client then still has it sliding on from the shove, and would hand
    ///         it back to the server's state from a round trip ago, freezing it there until the state catches up.
    /// </summary>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // The server's to say: the state that ends it reaches a predicting client as it does anyone.
        if (_netManager.IsClient)
            return;

        var query = EntityQueryEnumerator<KsCrewVehiclePushedComponent, PhysicsComponent>();
        while (query.MoveNext(out var uid, out var pushedComponent, out var physicsComponent))
        {
            if (pushedComponent.LifeStage > ComponentLifeStage.Running)
                continue;

            var resting = physicsComponent.LinearVelocity.LengthSquared() < pushedComponent.RestSpeed * pushedComponent.RestSpeed;
            if (pushedComponent.Hulls.RemoveWhere(hullUid => !Exists(hullUid) || resting && !StillPushing(hullUid, uid)) == 0)
                continue;

            // Removed at the end of the tick: this is enumerating them.
            if (pushedComponent.Hulls.Count == 0)
                RemCompDeferred(uid, pushedComponent);
            else
                Dirty(uid, pushedComponent);
        }
    }
    private bool StillPushing(EntityUid hullUid, EntityUid pushedUid)
    {
        if (TerminatingOrDeleted(hullUid) || !_pushQuery.TryComp(hullUid, out var pushComponent))
            return false;

        var contacts = _physicsSystem.GetContacts(hullUid);
        while (contacts.MoveNext(out var contact))
        {
            if (contact.IsTouching &&
                contact.OurFixture(hullUid).Id == pushComponent.Fixture &&
                contact.OtherEnt(hullUid) == pushedUid)
            {
                return true;
            }
        }

        return false;
    }
}
