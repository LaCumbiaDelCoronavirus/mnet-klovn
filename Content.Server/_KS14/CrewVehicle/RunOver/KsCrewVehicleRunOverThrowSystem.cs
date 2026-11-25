using Content.Server._KS14.Throwing;
using Content.Shared._KS14.CrewVehicle.RunOver;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;

namespace Content.Server._KS14.CrewVehicle.RunOver;

/// <summary>
///     Throws the mobs crew vehicles strike hard enough, along the hull's way.
/// </summary>
public sealed partial class KsCrewVehicleRunOverThrowSystem : EntitySystem
{
    [Dependency] private ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private KsMobThrowSystem _mobThrowSystem = default!;

    [SubscribeLocalEvent]
    private void OnThrowStruckMob(ref KsCrewVehicleThrowStruckMobEvent args)
    {
        if (args.Thrown || !_mobThrowSystem.TryThrowMob(args.Mob, args.Direction, args.Force, out var thrown))
            return;

        args.Thrown = true;
        _adminLogger.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(args.Hull):vehicle} struck {ToPrettyString(args.Mob):subject}, throwing them {thrown.Value.Distance:distance} tiles at {thrown.Value.Speed:speed} m/s");
    }
}
