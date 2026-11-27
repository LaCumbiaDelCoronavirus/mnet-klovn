using Robust.Shared.GameStates;

namespace Content.Shared._KS14.CrewVehicle.Ride;

/// <summary>
///     On a mob riding a hull: it has it exactly while it is parented to <see cref="Hull"/> and climbing on it.
/// </summary>
/// <remarks>
///     Runtime only, added after map init: never saved, as nothing about a save keeps a climb.
/// </remarks>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true), UnsavedComponent]
[Access(typeof(KsCrewVehicleRideSystem))]
public sealed partial class KsCrewVehicleRiderComponent : Component
{
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public EntityUid Hull;

    /// <summary>
    ///     Where on the deck, in the hull's frame, the climb on slides it to (<see cref="KsCrewVehicleRideController"/>).
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public System.Numerics.Vector2 MountTarget;

    /// <summary>
    ///     The body's own <c>SleepingAllowed</c>, given back on getting off: a rider standing still on a moving hull has
    ///         no speed of its own, and asleep it would get no contact updates.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public bool SleepingAllowed = true;
}

/// <summary>
///     Raised, broadcast, on the server, for a rider a crash has thrown off its hull: the server's throw answers it.
/// </summary>
/// <param name="Direction">Which way: the hull's travel before the crash stopped it.</param>
[ByRefEvent]
public record struct KsCrewVehicleThrowRiderEvent(EntityUid Hull, EntityUid Rider, System.Numerics.Vector2 Direction, float Force)
{
    public bool Thrown;
}
