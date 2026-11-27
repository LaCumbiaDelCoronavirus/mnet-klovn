using System.Diagnostics.CodeAnalysis;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;

namespace Content.Shared.Weapons.Ranged.Systems;

// Partial: moving rounds between two ballistic providers from code, kept apart from the upstream provider.
public abstract partial class SharedGunSystem
{
    /// <summary>
    ///     Moves one round from <paramref name="source"/> (a box, a magazine) into <paramref name="target"/>. Predicted:
    ///         a round that exists is moved between the two containers, and one that doesn't yet, of the round the target
    ///         is filled with, is moved as a count. A round the target refuses goes back where it came from.
    /// </summary>
    /// <returns>False when the target is full, the source empty, or the round refused; on a client, also when only the
    ///     server can move it (<see cref="KsCanTransferBallisticPredicted"/>).</returns>
    public bool KsTryTransferBallisticRound(Entity<BallisticAmmoProviderComponent> source,
        Entity<BallisticAmmoProviderComponent> target,
        EntityUid? user)
    {
        if (IsFull(target) || GetBallisticShots(source.Comp) == 0)
            return false;

        // Not yet a round, and the round the target is filled with: the same thing to both, so only the counts move.
        //      The target's whitelist takes its own fill round (KsCrewVehicleWeaponWhitelistTest checks mounted guns).
        if (source.Comp.Entities.Count == 0 && source.Comp.Proto != null && source.Comp.Proto == target.Comp.Proto)
        {
            SetBallisticUnspawned(source, source.Comp.UnspawnedCount - 1);
            SetBallisticUnspawned(target, target.Comp.UnspawnedCount + 1);
            return true;
        }

        // A round of another kind has to be made to be moved. A client's would be a predicted spawn put into a
        //      networked container, which the next state contradicts, so the server moves it and the client is told.
        if (!KsCanTransferBallisticPredicted(source, target) && _netManager.IsClient)
            return false;

        var ammo = new List<(EntityUid? Entity, IShootable Shootable)>(1);
        RaiseLocalEvent(source, new TakeAmmoEvent(1, ammo, Transform(source).Coordinates, user));

        foreach (var (roundUid, _) in ammo)
        {
            if (roundUid is not { } round)
                continue;

            if (TryBallisticInsert(target, round, user, suppressInsertionSound: true))
                return true;

            TryBallisticInsert(source, round, user, suppressInsertionSound: true);
            return false;
        }

        return false;
    }

    /// <summary>
    ///     Takes the round that would be fired next out of <paramref name="provider"/>, whole and unfired, next to it.
    ///         Predicted for a round that exists; one that doesn't yet (a count of the provider's fill round) only the
    ///         server makes.
    /// </summary>
    /// <returns>False when it is empty, or on a client when only the server can make the round.</returns>
    public bool KsTryTakeBallisticRound(Entity<BallisticAmmoProviderComponent> provider, [NotNullWhen(true)] out EntityUid? round)
    {
        round = null;
        if (provider.Comp.Entities.Count > 0)
        {
            var existing = provider.Comp.Entities[^1];
            provider.Comp.Entities.RemoveAt(provider.Comp.Entities.Count - 1);
            DirtyField(provider.AsNullable(), nameof(BallisticAmmoProviderComponent.Entities));
            Containers.Remove(existing, provider.Comp.Container);
            round = existing;
        }
        else if (provider.Comp.UnspawnedCount > 0 && provider.Comp.Proto is { } proto && _netManager.IsServer)
        {
            SetBallisticUnspawned(provider, provider.Comp.UnspawnedCount - 1);
            round = Spawn(proto, Transform(provider).Coordinates);
        }
        else
        {
            return false;
        }

        UpdateBallisticAppearance(provider);
        UpdateAmmoCount(provider);
        return true;
    }

    /// <summary>
    ///     Whether a client can predict <see cref="KsTryTransferBallisticRound"/>'s next round: it already exists, or it
    ///         is the target's own fill round, moved as a count.
    /// </summary>
    public bool KsCanTransferBallisticPredicted(Entity<BallisticAmmoProviderComponent> source,
        Entity<BallisticAmmoProviderComponent> target)
    {
        return source.Comp.Entities.Count > 0 || source.Comp.Proto != null && source.Comp.Proto == target.Comp.Proto;
    }
}
