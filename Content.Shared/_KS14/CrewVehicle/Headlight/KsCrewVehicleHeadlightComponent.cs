namespace Content.Shared._KS14.CrewVehicle.Headlight;

/// <summary>
///     A vehicle whose driver can switch its headlight, the vehicle's <c>UnpoweredFlashlight</c>, from their action bar
///         while they drive. The light and its toggle action are the flashlight's.
/// </summary>
[RegisterComponent, Access(typeof(KsCrewVehicleHeadlightSystem))]
public sealed partial class KsCrewVehicleHeadlightComponent : Component;
