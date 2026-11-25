using System.Numerics;
using Content.Shared._KS14.PopupLocalization;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Standing;
using Content.Shared.Stunnable;
using Content.Shared.Vehicle.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.CrewVehicle.RunOver;

/// <summary>
///     A crew vehicle closing on a standing mob at <see cref="KsCrewVehicleRunOverComponent.MinSpeed"/> or more runs it
///         over: it is hurt once and knocked down, and stays down until the hull has gone. From
///         <see cref="KsCrewVehicleRunOverComponent.ThrowSpeed"/> it is hurt and thrown along the hull's way instead. A
///         slower hull carries it along (<see cref="KsCrewVehicleRunOverController"/>); a mob already down is simply
///         driven over.
/// </summary>
/// <remarks>
///     Predicted by the driver's client, which predicts the hull, except the throw, which is the server's
///         (<see cref="KsCrewVehicleThrowStruckMobEvent"/>). Nothing here is random.
/// </remarks>
public sealed partial class KsCrewVehicleRunOverSystem : EntitySystem
{
    [Dependency] private DamageableSystem _damageableSystem = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private INetManager _netManager = default!;
    [Dependency] private SharedAudioSystem _audioSystem = default!;
    [Dependency] private SharedPhysicsSystem _physicsSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private SharedStunSystem _stunSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private StandingStateSystem _standingSystem = default!;

    [Dependency] private EntityQuery<MobStateComponent> _mobStateQuery = default!;
    [Dependency] private EntityQuery<KsCrewVehicleStruckComponent> _struckQuery = default!;
    [Dependency] private EntityQuery<KsCrewVehicleUnderHullComponent> _underHullQuery = default!;
    [Dependency] private EntityQuery<VehicleComponent> _vehicleQuery = default!;

    private static readonly LocId RunOverPopup = "ks-crew-vehicle-run-over";

    [SubscribeLocalEvent]
    private void OnStartCollide(Entity<KsCrewVehicleRunOverComponent> hull, ref StartCollideEvent args)
    {
        if (_gameTiming.ApplyingState ||
            args.OurFixtureId != hull.Comp.Fixture ||
            !args.OtherFixture.Hard ||
            !_mobStateQuery.HasComp(args.OtherEntity) ||
            IsUnder(args.OtherEntity, hull.Owner) ||
            IsStruck(args.OtherEntity))
        {
            return;
        }

        var mobUid = args.OtherEntity;
        var closingSpeed = ClosingSpeed(hull.Owner, args.OurBody, mobUid);
        if (closingSpeed < hull.Comp.MinSpeed)
            return;

        // A mob already down is driven over, unhurt.
        if (!_standingSystem.IsDown(mobUid))
        {
            var driverUid = _vehicleQuery.CompOrNull(hull.Owner)?.Operator;
            _damageableSystem.TryChangeDamage(mobUid, hull.Comp.Damage, origin: driverUid ?? hull.Owner);
            _audioSystem.PlayPredicted(hull.Comp.HitSound, mobUid, driverUid);
            _popupSystem.PopupEntity(KsPopupMessage.Create(RunOverPopup, ("vehicle", hull.Owner), ("victim", mobUid)),
                mobUid, PopupType.MediumCaution);

            if (closingSpeed >= hull.Comp.ThrowSpeed && TryThrow(hull, args.OurBody, mobUid, closingSpeed))
                return;

            _stunSystem.TryKnockdown(mobUid, hull.Comp.Knockdown, refresh: true);
        }

        var underHullComponent = EnsureComp<KsCrewVehicleUnderHullComponent>(mobUid);
        underHullComponent.Hulls.Add(hull.Owner);
        Dirty(mobUid, underHullComponent);
    }

    [SubscribeLocalEvent]
    private void OnStandUpAttempt(Entity<KsCrewVehicleUnderHullComponent> mob, ref StandUpAttemptEvent args)
    {
        if (mob.Comp.LifeStage <= ComponentLifeStage.Running)
            args.Cancelled = true;
    }

    /// <summary>
    ///     Sends a struck mob flying along the hull's way, and marks it to be left alone while it gets clear.
    /// </summary>
    private bool TryThrow(Entity<KsCrewVehicleRunOverComponent> hull, PhysicsComponent hullBody, EntityUid mobUid, float closingSpeed)
    {
        var velocity = hullBody.LinearVelocity;
        if (velocity.LengthSquared() < 0.0001f)
            return false;

        var ev = new KsCrewVehicleThrowStruckMobEvent(hull.Owner, mobUid, Vector2.Normalize(velocity),
            closingSpeed * hull.Comp.ThrowForcePerSpeed);
        RaiseLocalEvent(ref ev);

        // The throw is the server's, and whether a mob can be thrown is a server component's to say: a predicting
        //      client takes it that it is, and the server's state corrects the rare mob that can't.
        if (!ev.Thrown && !_netManager.IsClient)
            return false;

        var struckComponent = EnsureComp<KsCrewVehicleStruckComponent>(mobUid);
        struckComponent.Until = _gameTiming.CurTime + hull.Comp.StruckIgnoreTime;
        Dirty(mobUid, struckComponent);
        return true;
    }

    /// <summary>
    ///     Whether <paramref name="mobUid"/> was just thrown by a hull and is still to be left alone.
    /// </summary>
    public bool IsStruck(EntityUid mobUid)
    {
        return _struckQuery.TryComp(mobUid, out var struckComponent) &&
               struckComponent.LifeStage <= ComponentLifeStage.Running &&
               _gameTiming.CurTime < struckComponent.Until;
    }

    /// <summary>
    ///     Whether <paramref name="mobUid"/> has been run over by, or driven over by, <paramref name="hullUid"/> and is
    ///         still under it.
    /// </summary>
    public bool IsUnder(EntityUid mobUid, EntityUid hullUid)
    {
        return _underHullQuery.TryComp(mobUid, out var underHullComponent) &&
               underHullComponent.LifeStage <= ComponentLifeStage.Running &&
               underHullComponent.Hulls.Contains(hullUid);
    }

    /// <summary>
    ///     How fast <paramref name="hullUid"/> itself is moving toward <paramref name="otherUid"/>. Only the hull's own
    ///         speed: someone running into a parked hull isn't run over by it.
    /// </summary>
    public float ClosingSpeed(EntityUid hullUid, PhysicsComponent hullBody, EntityUid otherUid)
    {
        var away = _transformSystem.GetWorldPosition(otherUid) - _transformSystem.GetWorldPosition(hullUid);
        if (away.LengthSquared() < 0.0001f)
            return 0f;

        return Vector2.Dot(hullBody.LinearVelocity, Vector2.Normalize(away));
    }

    /// <summary>
    ///     Lets mobs out from under the hulls that have left them. Their own contacts can't say: a sleeping body gets no
    ///         contact updates, so this asks the distance, and only of mobs under a hull.
    /// </summary>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Whether the hull has left is the server's to say: a client that let the mob go early would predict running it
        //      over again, and its state carries the removal anyway.
        if (_netManager.IsClient)
            return;

        var struckQuery = EntityQueryEnumerator<KsCrewVehicleStruckComponent>();
        while (struckQuery.MoveNext(out var struckUid, out var struckComponent))
        {
            // Removed at the end of the tick: this is enumerating them.
            if (struckComponent.LifeStage <= ComponentLifeStage.Running && _gameTiming.CurTime >= struckComponent.Until)
                RemCompDeferred(struckUid, struckComponent);
        }

        var query = EntityQueryEnumerator<KsCrewVehicleUnderHullComponent, TransformComponent>();
        while (query.MoveNext(out var mobUid, out var underHullComponent, out var transformComponent))
        {
            if (underHullComponent.LifeStage > ComponentLifeStage.Running)
                continue;

            var changed = underHullComponent.Hulls.RemoveWhere(hullUid => HasLeft(mobUid, transformComponent, hullUid)) > 0;
            if (underHullComponent.Hulls.Count == 0)
            {
                // Removed at the end of the tick: this is enumerating them.
                RemCompDeferred(mobUid, underHullComponent);
                continue;
            }

            if (changed)
                Dirty(mobUid, underHullComponent);
        }
    }

    private bool HasLeft(EntityUid mobUid, TransformComponent mobTransform, EntityUid hullUid)
    {
        if (TerminatingOrDeleted(hullUid) ||
            !TryComp<KsCrewVehicleRunOverComponent>(hullUid, out var runOverComponent) ||
            Transform(hullUid).MapUid != mobTransform.MapUid)
        {
            return true;
        }

        // Out of reach of the contact fixture. Its contact can be stale, for a body that was moved while asleep (the
        //      physics-queries trap), so too far from the hull's body counts as gone whatever the contact says.
        if (!_physicsSystem.TryGetDistance(mobUid, hullUid, out var distance) ||
            distance > runOverComponent.ReleaseDistance)
        {
            return true;
        }

        var contacts = _physicsSystem.GetContacts(hullUid);
        while (contacts.MoveNext(out var contact))
        {
            if (contact.IsTouching &&
                contact.OurFixture(hullUid).Id == runOverComponent.Fixture &&
                contact.OtherEnt(hullUid) == mobUid)
            {
                return false;
            }
        }

        return true;
    }
}
