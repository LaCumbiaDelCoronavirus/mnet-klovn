using Content.Shared._KS14.Fluidmos.Serialization;
using Content.Shared.Chemistry.Components;
using Content.Shared.FixedPoint;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Fluidmos;

/// <summary>
///     Reagents carried inside a <see cref="Content.Shared.Atmos.GasMixture"/>, as solid, liquid and (unlinked) vapor.
/// </summary>
/// <remarks>
///     <para>
///         The linear operations (split, merge, scale) need no prototypes. Totals are cached and kept exact by the
///             operations that walk every entry anyway; <see cref="Add"/> updates them incrementally, with a recount every
///             <see cref="KsFluidmos.OperationsPerRecount"/> operations against drift.
///     </para>
///     <para>
///         The heat capacity needs each reagent's properties, so it is cached separately and refreshed through
///             <see cref="GetHeatCapacity"/>, which takes the property table.
///     </para>
///     <para>
///         Solids never move with <see cref="RemoveRatio"/>: a pump drawing from a slurry takes the liquid and leaves
///             the ice. They do split with <see cref="Add"/> at a fraction, which is how a dividing pipe net shares them.
///     </para>
/// </remarks>
[Serializable, NetSerializable, DataDefinition]
public sealed partial class KsFluidMixture : IEquatable<KsFluidMixture>
{
    [DataField(customTypeSerializer: typeof(KsFluidReagentsSerializer))]
    public Dictionary<KsFluidReagentKey, KsFluidAmount> Reagents = new();

    private float _liquidUnits;
    private float _solidUnits;
    private float _vaporMoles;
    private bool _countsValid;
    private int _operationsSinceRecount;

    /// <summary>
    ///     Unscaled J/K of everything in the mixture, valid while <see cref="_heatCapacityValid"/>.
    /// </summary>
    private float _heatCapacity;
    private bool _heatCapacityValid;

    /// <summary>
    ///     Temperature at the last phase step, for skipping quiescent mixtures.
    /// </summary>
    [ViewVariables]
    public float LastTemperature = float.NaN;

    /// <summary>
    ///     Pressure at the last phase step, for skipping quiescent mixtures.
    /// </summary>
    [ViewVariables]
    public float LastPressure = float.NaN;

    /// <summary>
    ///     Whether reagents have entered or left since the last phase step.
    /// </summary>
    [ViewVariables]
    public bool CompositionChanged = true;

    /// <summary>
    ///     Whether the last phase step changed next to nothing, so an unchanged mixture can skip the next one.
    /// </summary>
    [ViewVariables]
    public bool PhaseSettled;

    /// <summary>
    ///     When the last phase step ran, for its time step.
    /// </summary>
    [ViewVariables]
    public TimeSpan? LastPhaseTime;

    public KsFluidMixture()
    {
    }

    public KsFluidMixture(int capacity)
    {
        Reagents = new Dictionary<KsFluidReagentKey, KsFluidAmount>(capacity);
    }

    [ViewVariables]
    public float LiquidUnits
    {
        get
        {
            EnsureCounts();
            return _liquidUnits;
        }
    }

    [ViewVariables]
    public float SolidUnits
    {
        get
        {
            EnsureCounts();
            return _solidUnits;
        }
    }

    /// <summary>
    ///     Liquid and solid together: everything that takes up volume.
    /// </summary>
    [ViewVariables]
    public float CondensedUnits
    {
        get
        {
            EnsureCounts();
            return _liquidUnits + _solidUnits;
        }
    }

    /// <summary>
    ///     Moles of unlinked vapor. Linked vapor is in the gas mixture's moles instead.
    /// </summary>
    [ViewVariables]
    public float VaporMoles
    {
        get
        {
            EnsureCounts();
            return _vaporMoles;
        }
    }

    [ViewVariables]
    public bool IsEmpty => Reagents.Count == 0;

    public int Count => Reagents.Count;

    /// <summary>
    ///     Unscaled heat capacity, in J/K, of everything in the mixture.
    /// </summary>
    public float GetHeatCapacity(KsFluidmosPropertiesSystem propertiesSystem)
    {
        if (_heatCapacityValid)
            return _heatCapacity;

        var heatCapacity = 0f;
        foreach (var (key, amount) in Reagents)
        {
            if (!propertiesSystem.TryGetProperties(key.Prototype, out var properties))
            {
                heatCapacity += amount.Solid + amount.Liquid;
                continue;
            }

            heatCapacity += (amount.Solid + amount.Liquid) * properties.SpecificHeat;
            if (amount.Vapor > 0f)
                heatCapacity += amount.Vapor / properties.MolesPerUnit * properties.VaporHeatCapacityPerUnit;
        }

        _heatCapacity = heatCapacity;
        _heatCapacityValid = true;
        return heatCapacity;
    }

    /// <summary>
    ///     Marks the cached heat capacity stale, after a change it cannot follow.
    /// </summary>
    public void InvalidateHeatCapacity()
    {
        _heatCapacityValid = false;
    }

    public bool TryGetAmount(KsFluidReagentKey key, out KsFluidAmount amount)
    {
        return Reagents.TryGetValue(key, out amount);
    }

    /// <summary>
    ///     Replaces one reagent's amounts, dropping it when they fall below the minimums. Must not be called while
    ///         enumerating <see cref="Reagents"/>.
    /// </summary>
    public void SetAmount(KsFluidReagentKey key, KsFluidAmount amount)
    {
        Reagents.TryGetValue(key, out var previous);
        Trim(ref amount);

        if (amount.IsEmpty)
            Reagents.Remove(key);
        else
            Reagents[key] = amount;

        if (_countsValid)
        {
            _liquidUnits += amount.Liquid - previous.Liquid;
            _solidUnits += amount.Solid - previous.Solid;
            _vaporMoles += amount.Vapor - previous.Vapor;
            CountOperation();
        }

        _heatCapacityValid = false;
        CompositionChanged = true;
    }

    /// <summary>
    ///     Adds units of liquid of one reagent.
    /// </summary>
    public void AddLiquid(KsFluidReagentKey key, float units)
    {
        if (units <= 0f)
            return;

        Reagents.TryGetValue(key, out var amount);
        amount.Liquid += units;
        SetAmount(key, amount);
    }

    /// <summary>
    ///     Splits off <paramref name="ratio"/> of the liquid and vapor into a new mixture. Solids stay.
    /// </summary>
    /// <param name="includeLiquid">False takes vapor only, as a gas-only draw does.</param>
    public KsFluidMixture RemoveRatio(float ratio, bool includeLiquid = true)
    {
        return Split(ratio, includeLiquid: includeLiquid, includeVapor: true);
    }

    /// <summary>
    ///     Splits off <paramref name="ratio"/> of the liquid only into a new mixture.
    /// </summary>
    public KsFluidMixture RemoveLiquidRatio(float ratio)
    {
        return Split(ratio, includeLiquid: true, includeVapor: false);
    }

    private KsFluidMixture Split(float ratio, bool includeLiquid, bool includeVapor)
    {
        var removed = new KsFluidMixture(Reagents.Count);
        if (ratio <= 0f || Reagents.Count == 0)
        {
            removed._countsValid = true;
            return removed;
        }

        ratio = MathF.Min(ratio, 1f);
        var remaining = new Dictionary<KsFluidReagentKey, KsFluidAmount>(Reagents.Count);
        var liquid = 0f;
        var solid = 0f;
        var vapor = 0f;

        foreach (var (key, amount) in Reagents)
        {
            var taken = new KsFluidAmount(
                0f,
                includeLiquid ? amount.Liquid * ratio : 0f,
                includeVapor ? amount.Vapor * ratio : 0f);
            var left = new KsFluidAmount(amount.Solid, amount.Liquid - taken.Liquid, amount.Vapor - taken.Vapor);
            Trim(ref taken);
            Trim(ref left);

            if (!taken.IsEmpty)
            {
                removed.Reagents[key] = taken;
                removed._liquidUnits += taken.Liquid;
                removed._vaporMoles += taken.Vapor;
            }

            if (!left.IsEmpty)
            {
                remaining[key] = left;
                liquid += left.Liquid;
                solid += left.Solid;
                vapor += left.Vapor;
            }
        }

        removed._countsValid = true;
        removed.CompositionChanged = true;

        Reagents = remaining;
        _liquidUnits = liquid;
        _solidUnits = solid;
        _vaporMoles = vapor;
        _countsValid = true;
        _operationsSinceRecount = 0;
        _heatCapacityValid = false;
        CompositionChanged = true;

        return removed;
    }

    /// <summary>
    ///     Adds <paramref name="fraction"/> of every phase of <paramref name="other"/>, which is not modified.
    /// </summary>
    public void Add(KsFluidMixture other, float fraction = 1f)
    {
        if (fraction <= 0f || other.Reagents.Count == 0)
            return;

        foreach (var (key, amount) in other.Reagents)
        {
            Reagents.TryGetValue(key, out var existing);
            existing.Solid += amount.Solid * fraction;
            existing.Liquid += amount.Liquid * fraction;
            existing.Vapor += amount.Vapor * fraction;
            Trim(ref existing);

            if (!existing.IsEmpty)
                Reagents[key] = existing;
        }

        if (_countsValid && other._countsValid)
        {
            _liquidUnits += other._liquidUnits * fraction;
            _solidUnits += other._solidUnits * fraction;
            _vaporMoles += other._vaporMoles * fraction;
            CountOperation();
        }
        else
        {
            _countsValid = false;
        }

        if (_heatCapacityValid && other._heatCapacityValid)
            _heatCapacity += other._heatCapacity * fraction;
        else
            _heatCapacityValid = false;

        CompositionChanged = true;
    }

    /// <summary>
    ///     Scales every phase, solids included, as deleting part of a pipe net does.
    /// </summary>
    public void Multiply(float multiplier)
    {
        if (multiplier <= 0f)
        {
            Clear();
            return;
        }

        var scaled = new Dictionary<KsFluidReagentKey, KsFluidAmount>(Reagents.Count);
        foreach (var (key, amount) in Reagents)
        {
            var result = new KsFluidAmount(amount.Solid * multiplier, amount.Liquid * multiplier, amount.Vapor * multiplier);
            Trim(ref result);
            if (!result.IsEmpty)
                scaled[key] = result;
        }

        Reagents = scaled;
        Recount();
        _heatCapacityValid = false;
        CompositionChanged = true;
    }

    public void Clear()
    {
        Reagents.Clear();
        _liquidUnits = 0f;
        _solidUnits = 0f;
        _vaporMoles = 0f;
        _countsValid = true;
        _heatCapacity = 0f;
        _heatCapacityValid = true;
        CompositionChanged = true;
    }

    public void CopyFrom(KsFluidMixture other)
    {
        Reagents = new Dictionary<KsFluidReagentKey, KsFluidAmount>(other.Reagents);
        _liquidUnits = other._liquidUnits;
        _solidUnits = other._solidUnits;
        _vaporMoles = other._vaporMoles;
        _countsValid = other._countsValid;
        _heatCapacity = other._heatCapacity;
        _heatCapacityValid = other._heatCapacityValid;
        _operationsSinceRecount = other._operationsSinceRecount;
        CompositionChanged = true;
    }

    public KsFluidMixture Clone()
    {
        var clone = new KsFluidMixture();
        clone.CopyFrom(this);
        return clone;
    }

    /// <summary>
    ///     Adds a solution's reagents as liquid. The caller merges the temperature.
    /// </summary>
    public void AddSolution(Solution solution)
    {
        foreach (var reagentQuantity in solution.Contents)
        {
            AddLiquid(KsFluidReagentKey.From(reagentQuantity.Reagent), (float)reagentQuantity.Quantity);
        }
    }

    /// <summary>
    ///     Takes up to <paramref name="units"/> of liquid, spread across the reagents in proportion, as a solution. What
    ///         <see cref="FixedPoint2"/> truncates stays in the mixture. The caller sets the temperature.
    /// </summary>
    public Solution TakeLiquidAsSolution(float units)
    {
        var liquidUnits = LiquidUnits;
        if (units <= 0f || liquidUnits <= 0f)
            return new Solution();

        var ratio = MathF.Min(units / liquidUnits, 1f);
        return TakeAsSolution(amount => amount.Liquid * ratio, (ref KsFluidAmount amount, float taken) => amount.Liquid -= taken);
    }

    /// <summary>
    ///     Takes all the solid as a solution. The caller sets the temperature.
    /// </summary>
    public Solution TakeSolidAsSolution()
    {
        return TakeAsSolution(amount => amount.Solid, (ref KsFluidAmount amount, float taken) => amount.Solid -= taken);
    }

    /// <summary>
    ///     Takes up to <paramref name="moles"/> of unlinked vapor, in proportion, as a solution of the units it came from.
    /// </summary>
    public Solution TakeVaporAsSolution(float moles, KsFluidmosPropertiesSystem propertiesSystem)
    {
        var vaporMoles = VaporMoles;
        if (moles <= 0f || vaporMoles <= 0f)
            return new Solution();

        var ratio = MathF.Min(moles / vaporMoles, 1f);
        var solution = new Solution();
        var updated = new Dictionary<KsFluidReagentKey, KsFluidAmount>(Reagents.Count);

        foreach (var (key, amount) in Reagents)
        {
            var remaining = amount;
            if (amount.Vapor > 0f)
            {
                var molesPerUnit = propertiesSystem.TryGetProperties(key.Prototype, out var properties)
                    ? properties.MolesPerUnit
                    : 1f / KsFluidmos.DefaultMolarMass;
                var quantity = FixedPoint2.New(amount.Vapor * ratio / molesPerUnit);
                if (quantity > FixedPoint2.Zero)
                {
                    solution.AddReagent(key.ToReagentId(), quantity);
                    remaining.Vapor = MathF.Max(0f, amount.Vapor - (float)quantity * molesPerUnit);
                }
            }

            Trim(ref remaining);
            if (!remaining.IsEmpty)
                updated[key] = remaining;
        }

        ReplaceReagents(updated);
        return solution;
    }

    private delegate void TakeFromAmount(ref KsFluidAmount amount, float taken);

    private Solution TakeAsSolution(Func<KsFluidAmount, float> available, TakeFromAmount take)
    {
        var solution = new Solution();
        var updated = new Dictionary<KsFluidReagentKey, KsFluidAmount>(Reagents.Count);

        foreach (var (key, amount) in Reagents)
        {
            var remaining = amount;
            var quantity = FixedPoint2.New(available(amount));
            if (quantity > FixedPoint2.Zero)
            {
                solution.AddReagent(key.ToReagentId(), quantity);
                take(ref remaining, (float)quantity);
            }

            Trim(ref remaining);
            if (!remaining.IsEmpty)
                updated[key] = remaining;
        }

        ReplaceReagents(updated);
        return solution;
    }

    private void ReplaceReagents(Dictionary<KsFluidReagentKey, KsFluidAmount> reagents)
    {
        Reagents = reagents;
        Recount();
        _heatCapacityValid = false;
        CompositionChanged = true;
    }

    /// <summary>
    ///     Recounts the cached totals from the entries.
    /// </summary>
    public void Recount()
    {
        var liquid = 0f;
        var solid = 0f;
        var vapor = 0f;

        foreach (var amount in Reagents.Values)
        {
            liquid += amount.Liquid;
            solid += amount.Solid;
            vapor += amount.Vapor;
        }

        _liquidUnits = liquid;
        _solidUnits = solid;
        _vaporMoles = vapor;
        _countsValid = true;
        _operationsSinceRecount = 0;
    }

    private void EnsureCounts()
    {
        if (!_countsValid)
            Recount();
    }

    private void CountOperation()
    {
        if (++_operationsSinceRecount >= KsFluidmos.OperationsPerRecount)
            Recount();
    }

    private static void Trim(ref KsFluidAmount amount)
    {
        if (!(amount.Solid >= KsFluidmos.MinLiquidUnits))
            amount.Solid = 0f;

        if (!(amount.Liquid >= KsFluidmos.MinLiquidUnits))
            amount.Liquid = 0f;

        if (!(amount.Vapor >= KsFluidmos.MinVaporMoles))
            amount.Vapor = 0f;
    }

    public bool Equals(KsFluidMixture? other)
    {
        if (ReferenceEquals(this, other))
            return true;

        if (other == null || Reagents.Count != other.Reagents.Count)
            return false;

        foreach (var (key, amount) in Reagents)
        {
            if (!other.Reagents.TryGetValue(key, out var otherAmount) ||
                !amount.Solid.Equals(otherAmount.Solid) ||
                !amount.Liquid.Equals(otherAmount.Liquid) ||
                !amount.Vapor.Equals(otherAmount.Vapor))
                return false;
        }

        return true;
    }

    public override bool Equals(object? obj)
    {
        return obj is KsFluidMixture other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Reagents.Count, LiquidUnits, SolidUnits, VaporMoles);
    }
}
