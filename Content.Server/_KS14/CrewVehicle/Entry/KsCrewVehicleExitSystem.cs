using System.Diagnostics.CodeAnalysis;
using Content.Server._KS14.CrewVehicle.Interior;
using Content.Shared._KS14.CrewVehicle.Entry;
using Content.Shared._KS14.CrewVehicle.Interior;
using Content.Shared._KS14.PopupLocalization;
using Content.Shared.Buckle;
using Content.Shared.DoAfter;
using Content.Shared.Ghost;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;

namespace Content.Server._KS14.CrewVehicle.Entry;

/// <summary>
///     Getting out of a crew vehicle: through an exit door inside, after a do-after, onto the matching doorway outside,
///         if the hull is not moving and nothing is in the way there.
/// </summary>
/// <remarks>
///     Server-only, as getting in is: it moves the player to another map.
/// </remarks>
public sealed partial class KsCrewVehicleExitSystem : EntitySystem
{
    [Dependency] private KsCrewVehicleInteriorSystem _interiorSystem = default!;
    [Dependency] private SharedBuckleSystem _buckleSystem = default!;
    [Dependency] private SharedDoAfterSystem _doAfterSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private EntityQuery<GhostComponent> _ghostQuery = default!;
    [Dependency] private EntityQuery<PhysicsComponent> _physicsQuery = default!;

    private static readonly LocId BuckledMessage = "ks-crew-vehicle-buckled";
    private static readonly LocId BlockedMessage = "ks-crew-vehicle-exit-blocked";
    private static readonly LocId MovingMessage = "ks-crew-vehicle-exit-moving";
    private static readonly LocId ExitVerb = "ks-crew-vehicle-verb-exit";

    [SubscribeLocalEvent]
    private void OnActivate(Entity<KsCrewVehicleExitComponent> entity, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex)
            return;

        args.Handled = true;
        TryStartExit(entity, args.User);
    }

    // GetVerbsEvent is a by-value class event, which only the (uid, component, args) handler form accepts (RA0054).
    [SubscribeLocalEvent]
    private void OnGetVerbs(EntityUid uid, KsCrewVehicleExitComponent component, GetVerbsEvent<AlternativeVerb> args)
    {
        var user = args.User;
        if (!_ghostQuery.HasComp(user) && (!args.CanAccess || !args.CanInteract))
            return;

        var exit = new Entity<KsCrewVehicleExitComponent>(uid, component);
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString(ExitVerb),
            Act = () => TryStartExit(exit, user),
        });
    }

    /// <summary>
    ///     Starts <paramref name="user"/> getting out by this door; a ghost gets out at once.
    /// </summary>
    /// <returns>Whether they got out or began to.</returns>
    public bool TryStartExit(Entity<KsCrewVehicleExitComponent> exit, EntityUid user)
    {
        if (!CanExit(exit, user, out var vehicle, out _))
            return false;

        if (_ghostQuery.HasComp(user))
            return Exit(exit, user);

        var doAfter = new DoAfterArgs(EntityManager, user, vehicle.Value.Comp.ExitDelay, new KsCrewVehicleExitDoAfterEvent(), exit, target: exit)
        {
            BreakOnMove = true,
            NeedHand = false,
        };
        return _doAfterSystem.TryStartDoAfter(doAfter);
    }

    [SubscribeLocalEvent]
    private void OnExitDoAfter(Entity<KsCrewVehicleExitComponent> entity, ref KsCrewVehicleExitDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled)
            return;

        args.Handled = true;
        Exit(entity, args.User);
    }

    /// <summary>
    ///     Whether <paramref name="user"/> may leave by this door now, and where they would land, popping up why not
    ///         if they may not.
    /// </summary>
    private bool CanExit(Entity<KsCrewVehicleExitComponent> exit,
        EntityUid user,
        [NotNullWhen(true)] out Entity<KsCrewVehicleInteriorComponent>? vehicle,
        out MapCoordinates landing)
    {
        landing = MapCoordinates.Nullspace;
        if (!_interiorSystem.TryGetVehicle(exit, out vehicle)
            || !_interiorSystem.TryGetDoorway(vehicle.Value, exit.Comp.Doorway, out var doorway))
            return false;

        if (_buckleSystem.IsBuckled(user))
        {
            _popupSystem.PopupEntity(KsPopupMessage.Create(BuckledMessage), exit, user);
            return false;
        }

        if (_physicsQuery.TryComp(vehicle.Value, out var physicsComponent)
            && physicsComponent.LinearVelocity.Length() > vehicle.Value.Comp.MaxExitSpeed)
        {
            _popupSystem.PopupEntity(KsPopupMessage.Create(MovingMessage), exit, user);
            return false;
        }

        landing = _interiorSystem.GetDoorwayPosition(vehicle.Value, doorway);
        if (_interiorSystem.IsBlockedForMobs(landing))
        {
            _popupSystem.PopupEntity(KsPopupMessage.Create(BlockedMessage), exit, user);
            return false;
        }

        return true;
    }

    private bool Exit(Entity<KsCrewVehicleExitComponent> exit, EntityUid user)
    {
        if (!CanExit(exit, user, out _, out var landing))
            return false;

        // Untracked first: moving out would otherwise remove it later in the tick, from the map change.
        _interiorSystem.UntrackOccupant(user);
        var pulled = _interiorSystem.MoveWithPulled(user, landing);
        if (pulled is { } pulledUid)
            _interiorSystem.UntrackOccupant(pulledUid);

        return true;
    }
}
