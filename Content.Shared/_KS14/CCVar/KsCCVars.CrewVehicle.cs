using Content.Shared.Administration;
using Content.Shared.CCVar.CVarAccess;
using Robust.Shared.Configuration;

namespace Content.Shared._KS14.CCVar;

public sealed partial class KsCCVars
{
    /// <summary>
    ///     How crew vehicles steer. False: press a direction and the hull turns to face it, driving that way. True: tank
    ///         controls, left and right turn the hull, up and down drive it forward and back.
    /// </summary>
    /// <remarks>
    ///     Replicated: the driver's client predicts the steering with the same mode.
    /// </remarks>
    [CVarControl(AdminFlags.Fun)]
    public static readonly CVarDef<bool> CrewVehicleTankControls =
        CVarDef.Create("klovn.crew_vehicle.tank_controls", false, CVar.SERVER | CVar.REPLICATED | CVar.ARCHIVE);
}
