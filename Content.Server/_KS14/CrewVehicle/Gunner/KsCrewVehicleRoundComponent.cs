namespace Content.Server._KS14.CrewVehicle.Gunner;

/// <summary>
///     On a round a mounted weapon fired for a gunner: whose shot it was, for the kill credit of whatever it hits, however
///         long it flies and whoever fires the weapon after.
/// </summary>
[RegisterComponent, UnsavedComponent, Access(typeof(KsCrewVehicleShotRecordSystem))]
public sealed partial class KsCrewVehicleRoundComponent : Component
{
    [ViewVariables(VVAccess.ReadOnly)]
    public EntityUid Gunner;
}
