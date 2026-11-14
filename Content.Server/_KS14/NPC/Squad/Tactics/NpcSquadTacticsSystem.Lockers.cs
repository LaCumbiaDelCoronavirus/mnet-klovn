using Content.Shared._KS14.NPC;

namespace Content.Server._KS14.NPC.Squad.Tactics;

/// <summary>
///     Breaking open a locker the hostile is believed to be in, when nobody can open it: locked, or welded shut. A breaching
///         charge goes on walls and airlocks, not lockers, so it is shot open, as the glass in the way of a plan is. See
///         <see cref="NpcSquadTacticsSettings.SearchBreakLockers"/>.
/// </summary>
public sealed partial class NpcSquadTacticsSystem
{
    /// <summary>
    ///     Gives <paramref name="memberUid"/>, sent to <paramref name="point"/>, the orders to shoot it open if it is a locker to
    ///         break: up to it, then fire at it from where nothing else is in the way. Returns whether it is one, and the
    ///         orders are given; otherwise it is to be opened as any other.
    /// </summary>
    private bool TryOrderLockerBreak(EntityUid memberUid,
        EntityUid issuerUid,
        NpcHunt hunt,
        NpcHuntSearchPoint point,
        NpcSquadTacticsSettings settings,
        TimeSpan now)
    {
        if (!settings.SearchBreakLockers ||
            point.Kind != NpcSearchPointKind.Locker ||
            point.StorageUid is not { } lockerUid ||
            !_entityStorageQuery.TryComp(lockerUid, out var storageComponent) ||
            storageComponent.Open ||
            !_gunSystem.TryGetGun(memberUid, out var gunEntity) ||
            GetAmmo(gunEntity.Owner).Count <= 0 ||
            !IsBelievedIn(hunt, lockerUid) ||
            !TryGetBreakThreshold(lockerUid, out _) ||
            CanAnyoneOpen(lockerUid))
            return false;

        // Where it stands, not on it: shot open, it is gone, and the order with it.
        var lockerCoordinates = Transform(lockerUid).Coordinates;
        var memberMapCoordinates = _transformSystem.GetMapCoordinates(memberUid);
        var lockerMapCoordinates = _transformSystem.GetMapCoordinates(lockerUid);

        var inRange = memberMapCoordinates.MapId == lockerMapCoordinates.MapId &&
            (lockerMapCoordinates.Position - memberMapCoordinates.Position).Length() <= settings.SearchBreakLockerRange;

        if (inRange &&
            _npcLineOfFireSystem.TryGetFirstHit(memberMapCoordinates, lockerMapCoordinates, _npcLineOfFireSystem.GetShotMask(memberUid), out var hit) &&
            hit.HitEntity == lockerUid)
        {
            // Over combat, as for glass: one that can see the hostile once it is out has no business with lockers by then.
            if (_givingPlanOrders)
            {
                SetOrder(memberUid, issuerUid, NpcOrderKind.BreakBarrier, lockerCoordinates, Angle.Zero, OrderRange, lockerUid, now);
            }
            else
            {
                using var planOrders = GivePlanOrders();
                SetOrder(memberUid, issuerUid, NpcOrderKind.BreakBarrier, lockerCoordinates, Angle.Zero, OrderRange, lockerUid, now);
            }

            return true;
        }

        // Up to it first, as near as it would get to open it, but no nearer than it needs to be to shoot.
        var approachRange = MathF.Max(LockerOrderRange, settings.SearchBreakLockerRange - 1f);
        SetOrder(memberUid, issuerUid, NpcOrderKind.Search, lockerCoordinates, Angle.Zero, approachRange, null, now);
        return true;
    }

    /// <summary>
    ///     Whether a member saw the hunted hostile get into <paramref name="lockerUid"/>, or vanish beside it.
    /// </summary>
    private bool IsBelievedIn(NpcHunt hunt, EntityUid lockerUid)
    {
        if (hunt.TargetUid is not { } targetUid)
            return false;

        foreach (var memberUid in _members)
        {
            if (_npcPerceptionSystem.TryGetContact(memberUid, targetUid, out var contact) &&
                contact.State is NpcContactState.Concealed or NpcContactState.Suspected &&
                contact.ContainerUid == lockerUid)
                return true;
        }

        return false;
    }

    private bool CanAnyoneOpen(EntityUid lockerUid)
    {
        foreach (var memberUid in _members)
        {
            if (_entityStorageSystem.CanOpen(memberUid, lockerUid, silent: true))
                return true;
        }

        return false;
    }
}
