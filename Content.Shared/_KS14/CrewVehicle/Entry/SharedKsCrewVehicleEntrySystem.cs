using System.Diagnostics.CodeAnalysis;
using Content.Shared._KS14.CrewVehicle.Interior;
using Content.Shared._KS14.PopupLocalization;
using Content.Shared.Buckle;
using Content.Shared.DoAfter;
using Content.Shared.Ghost;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Popups;
using Content.Shared.Verbs;

namespace Content.Shared._KS14.CrewVehicle.Entry;

/// <summary>
///     Getting into a crew vehicle: through one of its doorways, after a do-after, while it has room.
/// </summary>
/// <remarks>
///     The try, its refusals and the do-after are predicted. What happens when the do-after ends is the server's
///         (<see cref="CompleteEnter"/>): it loads the interior and moves the player to another map, neither of which
///         a client can do.
/// </remarks>
public abstract partial class SharedKsCrewVehicleEntrySystem : EntitySystem
{
    [Dependency] private MobStateSystem _mobStateSystem = default!;
    [Dependency] private SharedBuckleSystem _buckleSystem = default!;
    [Dependency] private SharedDoAfterSystem _doAfterSystem = default!;
    [Dependency] private SharedKsCrewVehicleInteriorSystem _interiorSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private EntityQuery<GhostComponent> _ghostQuery = default!;

    private static readonly LocId UseADoorMessage = "ks-crew-vehicle-enter-use-a-door";
    private static readonly LocId FullMessage = "ks-crew-vehicle-enter-full";
    private static readonly LocId BuckledMessage = "ks-crew-vehicle-buckled";
    private static readonly LocId EnterVerb = "ks-crew-vehicle-verb-enter";

    [SubscribeLocalEvent]
    private void OnActivate(Entity<KsCrewVehicleInteriorComponent> entity, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex)
            return;

        args.Handled = true;
        TryStartEnter(entity, args.User);
    }

    // GetVerbsEvent is a by-value class event, which only the (uid, component, args) handler form accepts (RA0054).
    [SubscribeLocalEvent]
    private void OnGetVerbs(EntityUid uid, KsCrewVehicleInteriorComponent component, GetVerbsEvent<AlternativeVerb> args)
    {
        var user = args.User;
        if (!_ghostQuery.HasComp(user) && (!args.CanAccess || !args.CanInteract))
            return;

        var vehicle = new Entity<KsCrewVehicleInteriorComponent>(uid, component);
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString(EnterVerb),
            Act = () => TryStartEnter(vehicle, user),
        });
    }

    /// <summary>
    ///     Starts <paramref name="user"/> getting in by the doorway they stand at; a ghost gets in at once, which only
    ///         the server does.
    /// </summary>
    /// <returns>Whether they got in or began to.</returns>
    public bool TryStartEnter(Entity<KsCrewVehicleInteriorComponent> vehicle, EntityUid user)
    {
        if (!CanEnter(vehicle, user, out var doorwayId))
            return false;

        if (_ghostQuery.HasComp(user))
            return CompleteEnter(vehicle, user, doorwayId);

        if (!_interiorSystem.TryGetDoorway(vehicle, doorwayId, out var doorway))
            return false;

        var doAfter = new DoAfterArgs(EntityManager, user, vehicle.Comp.EnterDelay, new KsCrewVehicleEnterDoAfterEvent(), vehicle, target: vehicle)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = false,
            // The vehicle driving off breaks it too: it is measured from the hull's centre, the doorway being off it.
            DistanceThreshold = doorway.Offset.Length() + doorway.Radius,
        };
        return _doAfterSystem.TryStartDoAfter(doAfter);
    }

    [SubscribeLocalEvent]
    private void OnEnterDoAfter(Entity<KsCrewVehicleInteriorComponent> entity, ref KsCrewVehicleEnterDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled)
            return;

        args.Handled = true;

        // Checked again, predicted like the try: the client's do-after ends on the server's tick, so a refusal's popup
        //      is matched to the server's. Getting in is then the server's alone (CompleteEnter).
        if (CanEnter(entity, args.User, out var doorwayId))
            CompleteEnter(entity, args.User, doorwayId);
    }

    /// <summary>
    ///     Puts <paramref name="user"/> inside, by <paramref name="doorwayId"/>'s door. The server's: nothing on the
    ///         client.
    /// </summary>
    /// <returns>Whether they got in.</returns>
    protected virtual bool CompleteEnter(Entity<KsCrewVehicleInteriorComponent> vehicle, EntityUid user, string doorwayId)
    {
        return false;
    }

    /// <summary>
    ///     Whether <paramref name="user"/> may get in now, popping up why not if they may not.
    /// </summary>
    private bool CanEnter(Entity<KsCrewVehicleInteriorComponent> vehicle,
        EntityUid user,
        [NotNullWhen(true)] out string? doorwayId)
    {
        if (!_interiorSystem.TryFindDoorway(vehicle, _transformSystem.GetMapCoordinates(user), out doorwayId))
        {
            _popupSystem.PopupEntity(KsPopupMessage.Create(UseADoorMessage), vehicle, user);
            return false;
        }

        if (_ghostQuery.HasComp(user))
            return true;

        if (_buckleSystem.IsBuckled(user))
        {
            _popupSystem.PopupEntity(KsPopupMessage.Create(BuckledMessage), vehicle, user);
            return false;
        }

        var ev = new KsCanEnterCrewVehicleEvent(user);
        RaiseLocalEvent(vehicle, ref ev);
        if (ev.Cancelled)
        {
            if (ev.Message is { } message)
                _popupSystem.PopupEntity(KsPopupMessage.Create(message), vehicle, user);
            return false;
        }

        if (vehicle.Comp.LivingOccupants + CountNewLiving(user) > vehicle.Comp.MaxOccupants)
        {
            _popupSystem.PopupEntity(KsPopupMessage.Create(FullMessage, ("vehicle", vehicle.Owner)), vehicle, user);
            return false;
        }

        return true;
    }

    /// <summary>
    ///     How many living people getting in would bring: the user, and whoever they pull.
    /// </summary>
    private int CountNewLiving(EntityUid user)
    {
        var count = IsLiving(user) ? 1 : 0;
        if (TryComp<PullerComponent>(user, out var pullerComponent) && pullerComponent.Pulling is { } pulled && IsLiving(pulled))
            count++;

        return count;
    }

    protected bool IsLiving(EntityUid uid)
    {
        return TryComp<MobStateComponent>(uid, out var mobStateComponent) && !_mobStateSystem.IsDead(uid, mobStateComponent);
    }
}
