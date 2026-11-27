using Robust.Shared.GameStates;

namespace Content.Shared._KS14.CrewVehicle.CrashLockout;

/// <summary>
///     A crew vehicle that a hard enough crash into a wall leaves unable to drive for a while.
/// </summary>
/// <remarks>
///     Crashes are reported by <c>KsCrewVehicleSmash</c>: a vehicle without one is never locked out.
/// </remarks>
[RegisterComponent, Access(typeof(KsCrewVehicleCrashLockoutSystem))]
public sealed partial class KsCrewVehicleCrashLockoutComponent : Component
{
    /// <summary>
    ///     How fast, in m/s, the crash must be.
    /// </summary>
    [DataField]
    public float MinImpactSpeed = 5f;

    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(3);

    /// <summary>
    ///     Shown to the driver.
    /// </summary>
    [DataField]
    public LocId Popup = "ks-crew-vehicle-crash-lockout";

    /// <summary>
    ///     Played when a crash stalls the vehicle.
    /// </summary>
    [DataField]
    public Robust.Shared.Audio.SoundSpecifier? StallSound;
}

/// <summary>
///     On a crew vehicle while a crash keeps it from driving.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause, UnsavedComponent]
[Access(typeof(KsCrewVehicleCrashLockoutSystem))]
public sealed partial class KsCrewVehicleCrashLockedComponent : Component
{
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField, AutoPausedField]
    public TimeSpan Until;
}
