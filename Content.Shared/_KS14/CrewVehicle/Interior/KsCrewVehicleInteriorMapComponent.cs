using System.Numerics;
using Robust.Shared.GameStates;

namespace Content.Shared._KS14.CrewVehicle.Interior;

/// <summary>
///     On the map entity of a crew vehicle's interior: the link back to the vehicle it is the inside of.
/// </summary>
/// <remarks>
///     Added when the interior is loaded, so never saved: an admin saving the map must not write a link to a vehicle
///         that will not exist when it is loaded again.
/// </remarks>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, UnsavedComponent]
[Access(typeof(SharedKsCrewVehicleInteriorSystem))]
public sealed partial class KsCrewVehicleInteriorMapComponent : Component
{
    /// <summary>
    ///     The vehicle this map is the inside of.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public EntityUid Vehicle;

    /// <summary>
    ///     The vehicle's <see cref="KsCrewVehicleInteriorComponent.InteriorAnchor"/>, for clients, which do not know
    ///         the vehicle's interior link.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public Vector2 Anchor;

    /// <summary>
    ///     How far the cabin is turned on its grid, from the vehicle's
    ///         <see cref="KsCrewVehicleInteriorComponent.CabinFront"/>, for clients.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public Angle CabinRotation;

    /// <summary>
    ///     The vehicle's <see cref="KsCrewVehicleInteriorComponent.OutsideLightBleed"/>, for the client's renderer.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public float OutsideLightBleed;

    /// <summary>
    ///     The vehicle's <see cref="KsCrewVehicleInteriorComponent.OutsideBackdrop"/>, for the client's renderer.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public bool OutsideBackdrop = true;

    /// <summary>
    ///     The vehicle's <see cref="KsCrewVehicleInteriorComponent.CabinAmbient"/>, for the client's renderer.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public Color CabinAmbient = Color.Black;
}
