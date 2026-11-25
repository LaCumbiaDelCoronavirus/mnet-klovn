using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Destructible;
using Content.Shared.Examine;
using Content.Shared.Rounding;
using Robust.Shared.Network;

namespace Content.Shared._KS14.Damage.Examine;

/// <summary>
///     Adds the damage line to an examine with <see cref="KsExaminableDamageComponent"/>, on both sides alike.
/// </summary>
public sealed partial class KsExaminableDamageSystem : EntitySystem
{
    [Dependency] private DamageableSystem _damageableSystem = default!;
    [Dependency] private INetManager _netManager = default!;
    [Dependency] private SharedDestructibleSystem _destructibleSystem = default!;

    [Dependency] private EntityQuery<DamageableComponent> _damageableQuery = default!;

    /// <summary>
    ///     Where upstream's <c>ExaminableDamage</c> puts its line, so the two read the same.
    /// </summary>
    private const int Priority = -99;

    [SubscribeLocalEvent]
    private void OnMapInit(Entity<KsExaminableDamageComponent> entity, ref MapInitEvent args)
    {
        // Only the server reads Destructible's thresholds with their behaviours.
        if (_netManager.IsClient)
            return;

        entity.Comp.DestroyedAt = _destructibleSystem.DestroyedAt(entity.Owner);
        Dirty(entity);
    }

    [SubscribeLocalEvent]
    private void OnExamined(Entity<KsExaminableDamageComponent> entity, ref ExaminedEvent args)
    {
        if (!ProtoMan.Resolve(entity.Comp.Messages, out var messages) || messages.Values.Count == 0)
            return;

        var level = ContentHelpers.RoundToNearestLevels(DamageFraction(entity), 1, messages.Values.Count - 1);
        args.PushMarkup(Loc.GetString(messages.Values[level]), Priority);
    }

    /// <summary>
    ///     How damaged it is, from 0, undamaged, to 1, destroyed.
    /// </summary>
    public float DamageFraction(Entity<KsExaminableDamageComponent> entity)
    {
        if (!_damageableQuery.TryComp(entity, out var damageableComponent) ||
            entity.Comp.DestroyedAt is not { } destroyedAt ||
            destroyedAt <= 0)
        {
            return 0f;
        }

        return (float)(_damageableSystem.GetTotalDamage((entity.Owner, damageableComponent)) / destroyedAt);
    }
}
