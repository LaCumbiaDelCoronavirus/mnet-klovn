namespace Content.Shared._KS14.CrewVehicle.Interior;

/// <summary>
///     Asks what is wrong with a crew vehicle's freshly loaded interior, before anything on it has been initialised.
///         Raised on every entity of the interior map, then on the vehicle itself, so a system checks its own
///         components there: a seat's rules on the seat's component, rules across the whole vehicle on the vehicle's.
/// </summary>
/// <remarks>
///     Pure: handlers only add errors and claims to <see cref="Validation"/>. Any error refuses the interior. The
///         errors are for the log, so plain English naming the entity and the offending id.
/// </remarks>
[ByRefEvent]
public readonly record struct KsGetCrewVehicleInteriorErrorsEvent(
    Entity<KsCrewVehicleInteriorComponent> Vehicle,
    EntityUid InteriorMap,
    KsCrewVehicleInteriorValidation Validation);

/// <summary>
///     What <see cref="KsGetCrewVehicleInteriorErrorsEvent"/> collects about one interior.
/// </summary>
public sealed class KsCrewVehicleInteriorValidation
{
    private readonly List<string> _errors = new();
    private readonly Dictionary<(string Kind, string Key), EntityUid> _claims = new();

    public IReadOnlyList<string> Errors => _errors;

    public void AddError(string error)
    {
        _errors.Add(error);
    }

    /// <summary>
    ///     Records that <paramref name="claimant"/> uses the thing called <paramref name="key"/> of the given
    ///         <paramref name="kind"/> ("doorway", "gunner-mount"), for rules that allow only one user of each.
    /// </summary>
    /// <returns>False, with whoever claimed it first, if something already had.</returns>
    public bool TryClaim(string kind, string key, EntityUid claimant, out EntityUid previousClaimant)
    {
        if (_claims.TryGetValue((kind, key), out previousClaimant))
            return false;

        _claims[(kind, key)] = claimant;
        return true;
    }

    public bool IsClaimed(string kind, string key)
    {
        return _claims.ContainsKey((kind, key));
    }
}
