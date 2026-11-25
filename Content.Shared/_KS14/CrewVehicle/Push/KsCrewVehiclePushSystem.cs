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

    [SubscribeLocalEvent]
    private void OnStartCollide(Entity<KsCrewVehiclePushComponent> hull, ref StartCollideEvent args)
    {
        if (_gameTiming.ApplyingState ||
            args.OurFixtureId != hull.Comp.Fixture ||
            !IsPushable(args.OtherEntity, args.OtherBody, args.OtherFixture))
        {
            return;
        }

        var pushedComponent = EnsureComp<KsCrewVehiclePushedComponent>(args.OtherEntity);
        if (pushedComponent.Hulls.Add(hull.Owner))
            Dirty(args.OtherEntity, pushedComponent);
    }

    [SubscribeLocalEvent]
    private void OnEndCollide(Entity<KsCrewVehiclePushComponent> hull, ref EndCollideEvent args)
    {
        if (_gameTiming.ApplyingState ||
            args.OurFixtureId != hull.Comp.Fixture ||
            !_pushedQuery.TryComp(args.OtherEntity, out var pushedComponent) ||
            !pushedComponent.Hulls.Remove(hull.Owner))
        {
            return;
        }

        if (pushedComponent.Hulls.Count == 0)
            RemCompDeferred(args.OtherEntity, pushedComponent);
        else
            Dirty(args.OtherEntity, pushedComponent);
    }

    /// <summary>
    ///     Lets go of objects whose hull went without an end to the contact: deleted, moved off while asleep.
    /// </summary>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // The server's to say: the state that ends it reaches a predicting client as it does anyone.
        if (_netManager.IsClient)
            return;

        var query = EntityQueryEnumerator<KsCrewVehiclePushedComponent>();
        while (query.MoveNext(out var uid, out var pushedComponent))
        {
            if (pushedComponent.LifeStage > ComponentLifeStage.Running ||
                pushedComponent.Hulls.RemoveWhere(hullUid => !StillPushing(hullUid, uid)) == 0)
            {
                continue;
            }

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
