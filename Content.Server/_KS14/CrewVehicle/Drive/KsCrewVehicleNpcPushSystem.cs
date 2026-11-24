using Content.Server._KS14.NPC.Pushing;
using Content.Shared._KS14.CrewVehicle.Drive;

namespace Content.Server._KS14.CrewVehicle.Drive;

/// <summary>
///     A parked crew vehicle is a loose body, which NPCs would otherwise take for something to push out of their way.
/// </summary>
public sealed partial class KsCrewVehicleNpcPushSystem : EntitySystem
{
    [SubscribeLocalEvent]
    private void OnPushableAttempt(Entity<KsCrewVehicleDriveComponent> hull, ref NpcPushableAttemptEvent args)
    {
        args.Cancelled = true;
    }
}
