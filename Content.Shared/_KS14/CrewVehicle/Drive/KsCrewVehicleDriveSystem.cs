using Content.Shared._KS14.CrewVehicle.Interior;
using Content.Shared._KS14.CrewVehicle.View;
using Content.Shared._KS14.Movement;
using Content.Shared._KS14.Throwing;
using Content.Shared.Audio;
using Content.Shared.Buckle.Components;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Vehicle;
using Content.Shared.Vehicle.Components;
using Content.Shared.Vehicle.Systems;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.CrewVehicle.Drive;

/// <summary>
///     Whoever sits in a crew vehicle's driver seat operates the vehicle, through the upstream vehicle system, which
///         relays their keys to the hull's mover. This turns those keys into a vehicle's steering, and starts the engine
///         while someone drives.
/// </summary>
/// <remarks>
///     The seat is on the interior map and the hull outside, so the upstream <c>StrapVehicle</c>, which wants the seat
///         and the vehicle to be one entity, can't be used: the seat finds the hull through the interior's link instead.
///         Everything else, the relay, the eye on the hull, taking it all down again by any route, is upstream's.
/// </remarks>
public sealed partial class KsCrewVehicleDriveSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private SharedAmbientSoundSystem _ambientSoundSystem = default!;
    [Dependency] private SharedKsCrewVehicleInteriorSystem _interiorSystem = default!;
    [Dependency] private SharedMoverController _moverController = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private VehicleSystem _vehicleSystem = default!;

    [Dependency] private EntityQuery<InputMoverComponent> _moverQuery = default!;
    [Dependency] private EntityQuery<Robust.Shared.Physics.Components.PhysicsComponent> _physicsQuery = default!;
    [Dependency] private EntityQuery<StrapComponent> _strapQuery = default!;
    [Dependency] private EntityQuery<VehicleComponent> _vehicleQuery = default!;
    [Dependency] private EntityQuery<KsCrewVehicleViewSeatComponent> _viewSeatQuery = default!;

    private const string DriverSeatClaim = "driver-seat";

    [SubscribeLocalEvent]
    private void OnStrapped(Entity<KsCrewVehicleDriverSeatComponent> seat, ref StrappedEvent args)
    {
        // A seat that arrives already occupied raises this again while the client applies it; the server's state
        //      already has the operator, the relay and the eye.
        if (_gameTiming.ApplyingState)
            return;

        if (!_interiorSystem.TryGetVehicle(seat.Owner, out var vehicle) ||
            !_vehicleQuery.TryComp(vehicle.Value.Owner, out var vehicleComponent))
            return;

        _vehicleSystem.TrySetOperator((vehicle.Value.Owner, vehicleComponent), args.Buckle.Owner);
    }

    [SubscribeLocalEvent]
    private void OnUnstrapped(Entity<KsCrewVehicleDriverSeatComponent> seat, ref UnstrappedEvent args)
    {
        if (_gameTiming.ApplyingState)
            return;

        // Through the operator rather than the interior's link, which is already gone while the interior map is deleted.
        if (_vehicleSystem.GetOperatedVehicleOrNull(args.Buckle.Owner) is not { } operatedUid)
            return;

        // Driving something else, put there by other means: not this seat's to take away.
        if (_interiorSystem.TryGetVehicle(seat.Owner, out var vehicle) && vehicle.Value.Owner != operatedUid)
            return;

        _vehicleSystem.TryRemoveOperator(new Entity<VehicleOperatorComponent?>(args.Buckle.Owner, null));
    }

    /// <summary>
    ///     The engine runs while someone drives.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnOperatorSet(Entity<KsCrewVehicleDriveComponent> hull, ref VehicleOperatorSetEvent args)
    {
        if (!TerminatingOrDeleted(hull.Owner))
            _ambientSoundSystem.SetAmbience(hull.Owner, args.NewOperator != null);
    }

    /// <summary>
    ///     A hull is far too heavy to throw, by hand or by blast.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnCanBeThrown(Entity<KsCrewVehicleDriveComponent> hull, ref KsCanBeThrownEvent args)
    {
        args.Cancelled = true;
    }

    /// <summary>
    ///     Turns the keys, as the mover read them, into the hull's steering.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnUpdateWishDir(Entity<KsCrewVehicleDriveComponent> hull, ref UpdateWishDirEvent args)
    {
        // A driver who can no longer drive (unconscious, cuffed) stops relaying keys, but any the hull already holds
        //      would stay held: let go of them here.
        if (!_vehicleQuery.TryComp(hull.Owner, out var vehicleComponent) ||
            vehicleComponent.Operator is not { } operatorUid ||
            !_moverQuery.TryComp(operatorUid, out var operatorMover) ||
            !operatorMover.CanMove ||
            !_moverQuery.TryComp(hull.Owner, out var hullMover))
        {
            args.WishDir = System.Numerics.Vector2.Zero;
            return;
        }

        var transformComponent = Transform(hull.Owner);
        var worldRotation = _transformSystem.GetWorldRotation(transformComponent);

        var input = new KsCrewVehicleSteeringInput(args.WishDir,
            _moverController.GetParentGridAngle(hullMover),
            worldRotation,
            // The velocity the mover read at the start of this step: predicted state, like the transform.
            _physicsQuery.CompOrNull(hull.Owner)?.LinearVelocity ?? System.Numerics.Vector2.Zero,
            args.FrameTime);

        // The one place a control scheme is chosen. Another one plugs in here as a pure function like Tank: from this
        //      step's keys (the wish and the frame they were turned by), the hull's facing and velocity, it returns the
        //      hull's new rotation and the wish velocity to drive with, which must lie along that facing (the grip
        //      controller takes away anything else). Chosen by data on the drive component, should it ever need to be.
        var result = KsCrewVehicleSteering.Tank(input, hull.Comp);

        args.WishDir = result.Wish;

        // NoRotateOnMove keeps the mover from turning the hull itself; the same world-to-local step it would use.
        if (!result.HullRotation.EqualsApprox(worldRotation))
        {
            _transformSystem.SetLocalRotation(hull.Owner,
                transformComponent.LocalRotation + (result.HullRotation - worldRotation),
                transformComponent);
        }
    }

    [SubscribeLocalEvent]
    private void OnGetInteriorErrors(Entity<KsCrewVehicleDriverSeatComponent> seat, ref KsGetCrewVehicleInteriorErrorsEvent args)
    {
        var validation = args.Validation;
        var name = ToPrettyString(seat);

        if (!_strapQuery.HasComp(seat))
            validation.AddError($"driver seat {name} has no Strap, so nobody can sit in it");

        if (!_viewSeatQuery.HasComp(seat))
            validation.AddError($"driver seat {name} is not also a view seat, so its driver could not see where they drive");

        if (!validation.TryClaim(DriverSeatClaim, string.Empty, seat, out var otherSeat))
            validation.AddError($"driver seat {name} is a second one, after {ToPrettyString(otherSeat)}: a vehicle has one driver");

        var vehicleUid = args.Vehicle.Owner;
        if (!_vehicleQuery.HasComp(vehicleUid) || !HasComp<KsCrewVehicleDriveComponent>(vehicleUid))
            validation.AddError($"driver seat {name} is in a vehicle without Vehicle and KsCrewVehicleDrive, which can't be driven");
    }
}
