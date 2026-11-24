using System.Numerics;
using Robust.Shared.GameStates;

namespace Content.Shared._KS14.CrewVehicle.Mounts;

/// <summary>
///     A gun mounted on a crew vehicle's hull (<see cref="KsCrewVehicleMountsComponent"/>), fired by a gunner inside. It
///         is the shooter of its own shots: their damage is its doing, and so its gunner's.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedKsCrewVehicleMountSystem))]
public sealed partial class KsCrewVehicleWeaponComponent : Component
{
    /// <summary>
    ///     Where shots leave, in the weapon's own frame (forward is -Y), so it follows the barrel as the weapon turns.
    /// </summary>
    [DataField]
    public Vector2 MuzzleOffset;

    /// <summary>
    ///     How far from the muzzle a gunner may aim. A point beyond it is aimed at along the same bearing, at this range.
    /// </summary>
    [DataField]
    public float MaxAimRange = 20f;

    /// <summary>
    ///     The full width of the arc it may fire into, centred on its mount's rest direction
    ///         (<see cref="KsCrewVehicleMount.Rotation"/>). Null fires all round. A turret's aim is also kept inside it.
    /// </summary>
    [DataField]
    public Angle? FireArc;

    /// <summary>
    ///     The hull it is mounted on. Written at the hull's map init; a data field for the same reason as
    ///         <see cref="KsCrewVehicleMountsComponent.Weapons"/>.
    /// </summary>
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadOnly)]
    public EntityUid? Vehicle;

    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadOnly)]
    public string? MountId;
}
