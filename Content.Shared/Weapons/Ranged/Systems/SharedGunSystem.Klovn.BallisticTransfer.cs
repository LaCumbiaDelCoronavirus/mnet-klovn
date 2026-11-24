using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;

namespace Content.Shared.Weapons.Ranged.Systems;

// Partial: moving rounds between two ballistic providers from code, kept apart from the upstream provider.
public abstract partial class SharedGunSystem
{
    /// <summary>
    ///     Moves one round from <paramref name="source"/> (a box, a magazine) into <paramref name="target"/>, through the
    ///         providers' own taking and inserting. A round <paramref name="target"/> refuses goes back where it came from.
    /// </summary>
    /// <returns>False when the target is full, the source empty, or the round refused.</returns>
    /// <remarks>
    ///     Server only: what the source takes out may be a fresh spawn, which a client would only have predicted.
    /// </remarks>
    public bool KsTryTransferBallisticRound(Entity<BallisticAmmoProviderComponent> source,
        Entity<BallisticAmmoProviderComponent> target,
        EntityUid? user)
    {
        if (IsFull(target) || GetBallisticShots(source.Comp) == 0)
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
}
