using Content.Shared._KS14.CrewVehicle.PassThrough;

namespace Content.Client._KS14.CrewVehicle.PassThrough;

/// <summary>
///     The client only decides that a round ends at a hull it goes into; what flies inside is the server's.
/// </summary>
public sealed partial class KsCrewVehiclePassThroughSystem : SharedKsCrewVehiclePassThroughSystem;
