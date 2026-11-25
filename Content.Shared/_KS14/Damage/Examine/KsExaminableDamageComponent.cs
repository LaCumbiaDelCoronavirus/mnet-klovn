using Content.Shared.Dataset;
using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Damage.Examine;

/// <summary>
///     Shows how damaged something is when examined, as the server-only <c>ExaminableDamage</c> does, but worked out on
///         each side alike: the examiner's client shows the line at once, in its own language, rather than a round trip
///         later when the server's reply comes back.
/// </summary>
/// <remarks>
///     The damage reaches the client through <c>Damageable</c>'s state. The damage it is destroyed at doesn't: the
///         client reads <c>Destructible</c>'s thresholds without their behaviours, which say which threshold destroys
///         it. So the server works it out once, at map init, and sends it.
/// </remarks>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, Access(typeof(KsExaminableDamageSystem))]
public sealed partial class KsExaminableDamageComponent : Component
{
    /// <summary>
    ///     The messages, from undamaged to destroyed: the first at no damage, the rest spread evenly across the range.
    /// </summary>
    [DataField(required: true)]
    public ProtoId<LocalizedDatasetPrototype> Messages;

    /// <summary>
    ///     The total damage that destroys it, from the server's <c>Destructible</c>; null until map init.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public FixedPoint2? DestroyedAt;
}
