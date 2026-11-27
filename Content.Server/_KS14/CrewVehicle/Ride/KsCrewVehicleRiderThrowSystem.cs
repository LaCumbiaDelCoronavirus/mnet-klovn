using Content.Server._KS14.Throwing;
using Content.Shared._KS14.CrewVehicle.Ride;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;

namespace Content.Server._KS14.CrewVehicle.Ride;

/// <summary>
///     Throws the riders a crash threw off their hull, along the way it was going. A rider that can't be thrown is only
///         dismounted, beside the stopped hull.
/// </summary>
public sealed partial class KsCrewVehicleRiderThrowSystem : EntitySystem
{
    [Dependency] private ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private KsMobThrowSystem _mobThrowSystem = default!;

    [SubscribeLocalEvent]
    private void OnThrowRider(ref KsCrewVehicleThrowRiderEvent args)
    {
        if (args.Thrown || !_mobThrowSystem.TryThrowMob(args.Rider, args.Direction, args.Force, out var thrown))
            return;

        args.Thrown = true;
        _adminLogger.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(args.Hull):vehicle} crashed, throwing its rider {ToPrettyString(args.Rider):subject} off {thrown.Value.Distance:distance} tiles at {thrown.Value.Speed:speed} m/s");
    }
}
