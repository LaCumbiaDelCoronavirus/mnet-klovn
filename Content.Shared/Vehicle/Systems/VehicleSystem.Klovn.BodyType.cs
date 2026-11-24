using Content.Shared.Vehicle.Components;
using Robust.Shared.Network;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Shared.Vehicle.Systems;

/// <summary>
///     A vehicle is a loose object while nobody drives it and a kinematic controller, as the mover needs, while someone
///         does. A parked vehicle is solid to mobs and to other vehicles, stops a driven one, and coasts to a stop when
///         its driver gets off; a driven one moves like a mob.
/// </summary>
/// <remarks>
///     <para>
///         The parked body type is the vehicle prototype's own <c>Physics.bodyType</c>. A prototype that keeps
///             <see cref="BodyType.KinematicController"/> there (mechs) is never changed; a vehicle with no prototype is
///             left alone.
///     </para>
///     <para>
///         Invariant: a vehicle whose body is not a kinematic controller has no operator and cannot move, so the
///             mover's body-type assert never sees it. Taking a driver sets the kinematic controller at once, in the
///             same call that sets the relay. Losing one stops it moving at once, and parks the body at the next update:
///             the body type change regenerates contacts, which isn't safe from inside a physics callback, where an
///             unbuckle can come from.
///     </para>
/// </remarks>
public sealed partial class VehicleSystem
{
    [Dependency] private INetManager _netManager = default!;
    [Dependency] private SharedPhysicsSystem _physicsSystem = default!;

    [Dependency] private EntityQuery<PhysicsComponent> _physicsQuery = default!;

    // Transient work list: vehicles to park or to check after loading, drained each update.
    private readonly HashSet<EntityUid> _ksBodyTypeChecks = new();
    private readonly List<EntityUid> _ksBodyTypeChecksDraining = new();

    [SubscribeLocalEvent]
    private void OnKsOperatorSetBodyType(Entity<VehicleComponent> ent, ref VehicleOperatorSetEvent args)
    {
        if (TerminatingOrDeleted(ent.Owner))
            return;

        if (args.NewOperator != null)
        {
            if (KsGetParkedBodyType(ent.Owner) != null)
                _physicsSystem.SetBodyType(ent.Owner, BodyType.KinematicController);

            return;
        }

        KsQueueBodyTypeCheck(ent.Owner);
    }

    /// <summary>
    ///     A parked vehicle doesn't move itself: keeps <c>SharedMoverController.HandleMobMovement</c>'s body-type assert
    ///         away from it.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnKsCanRunParked(Entity<VehicleComponent> ent, ref VehicleCanRunEvent args)
    {
        if (ent.Comp.Operator == null &&
            _physicsQuery.TryComp(ent.Owner, out var physicsComponent) &&
            physicsComponent.BodyType != BodyType.KinematicController)
        {
            args.CanRun = false;
        }
    }

    /// <summary>
    ///     Unanchoring an input mover makes it a kinematic controller (<c>SharedMoverController.OnAnchorState</c>).
    /// </summary>
    [SubscribeLocalEvent]
    private void OnKsAnchorStateChanged(Entity<VehicleComponent> ent, ref AnchorStateChangedEvent args)
    {
        if (!args.Anchored)
            KsQueueBodyTypeCheck(ent.Owner);
    }

    /// <summary>
    ///     A map saved while someone drove holds a kinematic body and an operator that wasn't saved with it.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnKsVehicleStartup(Entity<VehicleComponent> ent, ref ComponentStartup args)
    {
        KsQueueBodyTypeCheck(ent.Owner);
    }

    private void KsQueueBodyTypeCheck(EntityUid uid)
    {
        // The client stops predicting a vehicle when it loses its operator (the relay goes, and with it the
        //      prediction), so its parked body type is whatever the server's state says.
        if (_netManager.IsServer)
            _ksBodyTypeChecks.Add(uid);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_ksBodyTypeChecks.Count == 0)
            return;

        // Through a second list: parking raises events, and a handler may queue another vehicle.
        _ksBodyTypeChecksDraining.AddRange(_ksBodyTypeChecks);
        _ksBodyTypeChecks.Clear();

        foreach (var uid in _ksBodyTypeChecksDraining)
        {
            if (!_vehicleQuery.TryComp(uid, out var vehicleComponent) || TerminatingOrDeleted(uid))
                continue;

            KsCheckBodyType((uid, vehicleComponent));
        }

        _ksBodyTypeChecksDraining.Clear();
    }

    private void KsCheckBodyType(Entity<VehicleComponent> ent)
    {
        var changed = false;
        if (ent.Comp.Operator is { } operatorUid &&
            (!Exists(operatorUid) ||
             !_operatorQuery.TryComp(operatorUid, out var operatorComponent) ||
             operatorComponent.Vehicle != ent.Owner))
        {
            ent.Comp.Operator = null;
            DirtyFields(ent.Owner, ent.Comp, null, nameof(VehicleComponent.Operator));
            changed = true;
        }

        if (ent.Comp.Operator == null &&
            KsGetParkedBodyType(ent.Owner) is { } parkedBodyType &&
            _physicsQuery.TryComp(ent.Owner, out var physicsComponent) &&
            physicsComponent.BodyType != parkedBodyType)
        {
            // Raises the mover's UpdateCanMove itself, through PhysicsBodyTypeChangedEvent.
            _physicsSystem.SetBodyType(ent.Owner, parkedBodyType, body: physicsComponent);
            changed = true;
        }

        // Only on a change: on a vehicle spawned parked, nothing here differs and nothing may be modified.
        if (changed)
            RefreshCanRun(ent.AsNullable());
    }

    /// <summary>
    ///     The body type <paramref name="uid"/>'s prototype gives it, or null if it has none to go by.
    /// </summary>
    public BodyType? KsGetParkedBodyType(EntityUid uid)
    {
        if (MetaData(uid).EntityPrototype is not { } prototype ||
            !prototype.TryComp<PhysicsComponent>(out var physicsComponent, Factory))
            return null;

        return physicsComponent.BodyType;
    }
}
