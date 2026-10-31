using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Fluidmos;

/// <summary>
///     One reagent a mixture carries, for UIs: every data variant (blood of different DNA, say) summed, in units. Vapor of
///         linked reagents is gas, and shows with the gases instead.
/// </summary>
[Serializable, NetSerializable]
public readonly record struct KsFluidEntry(ProtoId<ReagentPrototype> Reagent, float Solid, float Liquid, float Vapor)
{
    /// <summary>
    ///     Amounts below this are left out, as gas analyzers leave out trace gases.
    /// </summary>
    public const float MinUnits = 0.01f;

    /// <summary>
    ///     A mixture's fluid as UI entries, or null if it carries none worth showing.
    /// </summary>
    public static KsFluidEntry[]? FromMixture(KsFluidMixture? fluid, KsFluidmosPropertiesSystem propertiesSystem)
    {
        if (fluid == null || fluid.IsEmpty)
            return null;

        var entries = new List<KsFluidEntry>(fluid.Count);
        foreach (var (key, amount) in fluid.Reagents)
        {
            var vaporUnits = amount.Vapor > 0f && propertiesSystem.TryGetProperties(key.Prototype, out var properties) && properties.MolesPerUnit > 0f
                ? amount.Vapor / properties.MolesPerUnit
                : 0f;

            var index = entries.FindIndex(entry => entry.Reagent == key.Prototype);
            if (index < 0)
            {
                entries.Add(new KsFluidEntry(key.Prototype, amount.Solid, amount.Liquid, vaporUnits));
                continue;
            }

            var existing = entries[index];
            entries[index] = existing with
            {
                Solid = existing.Solid + amount.Solid,
                Liquid = existing.Liquid + amount.Liquid,
                Vapor = existing.Vapor + vaporUnits,
            };
        }

        entries.RemoveAll(entry => entry.Solid < MinUnits && entry.Liquid < MinUnits && entry.Vapor < MinUnits);
        return entries.Count > 0 ? entries.ToArray() : null;
    }
}
