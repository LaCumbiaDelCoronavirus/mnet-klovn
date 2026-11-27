using System.Numerics;
using Content.Shared.Climbing.Components;
using Robust.Shared.Physics;

namespace Content.Shared.Climbing.Systems;

/// <summary>
///     What riding a crew vehicle's hull needs of a climb (<c>KsCrewVehicleRideSystem</c>): ending one from outside, and
///         moving a climber onto a climbable that carries it.
/// </summary>
public sealed partial class ClimbSystem
{
    /// <summary>
    ///     Ends <paramref name="climber"/>'s climb now, as walking off what it climbed does. Nothing if it isn't climbing.
    /// </summary>
    public void KsStopClimb(Entity<ClimbingComponent?> climber)
    {
        if (!Resolve(climber.Owner, ref climber.Comp, logMissing: false) || !climber.Comp.IsClimbing)
            return;

        StopClimb(climber.Owner, climber.Comp);
    }

    /// <summary>
    ///     Puts back, in place, a climb that ended without the climber leaving what it stands on: its sensor's contact
    ///         was destroyed rather than left, as when what it climbed changed its body type. No slide, no events.
    /// </summary>
    public bool KsResumeClimb(Entity<ClimbingComponent?, FixturesComponent?> climber)
    {
        if (!Resolve(climber.Owner, ref climber.Comp1, ref climber.Comp2, logMissing: false) ||
            climber.Comp1.IsClimbing ||
            !ReplaceFixtures(climber.Owner, climber.Comp1, climber.Comp2))
        {
            return false;
        }

        climber.Comp1.IsClimbing = true;
        climber.Comp1.NextTransition = null;
        _actionBlockerSystem.UpdateCanMove(climber.Owner);
        Dirty(climber.Owner, climber.Comp1);
        return true;
    }

    /// <summary>
    ///     Parents a climber that has just climbed onto <paramref name="newParent"/> to it, keeping where it is in the
    ///         world, and holds it still for as long as a slide to <paramref name="localTarget"/> in that frame takes. The
    ///         caller does the slide, at <see cref="ClimbingComponent.TransitionRate"/>.
    /// </summary>
    /// <remarks>
    ///     The parent change would otherwise end the slide at once (<see cref="OnParentChange"/>), and with the new climb
    ///         sensor touching nothing yet, the climb with it.
    /// </remarks>
    public void KsMoveClimbingInto(Entity<ClimbingComponent?> climber, EntityUid newParent, Vector2 localTarget)
    {
        if (!Resolve(climber.Owner, ref climber.Comp, logMissing: false))
            return;

        climber.Comp.NextTransition = null;
        var transformComponent = _xformQuery.GetComponent(climber.Owner);
        _xformSystem.SetParent(climber.Owner, transformComponent, newParent);

        // The new parent's owner slides it, stopping at the target, which a fixed speed for a whole number of ticks
        //      would overshoot: this only holds it still for as long as that takes, and a tick more.
        climber.Comp.Direction = Vector2.Zero;
        var distance = (localTarget - transformComponent.LocalPosition).Length();
        climber.Comp.NextTransition = distance < 0.01f
            ? null
            : _timing.CurTime + TimeSpan.FromSeconds(distance / climber.Comp.TransitionRate) + _timing.TickPeriod;

        _actionBlockerSystem.UpdateCanMove(climber.Owner);
        Dirty(climber.Owner, climber.Comp);
    }
}
