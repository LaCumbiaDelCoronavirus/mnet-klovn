using Content.Server.Explosion.EntitySystems;
using Content.Shared._KS14.Construction;
using Content.Shared._Trauma.Projectiles;
using Content.Shared.Construction.Components;
using Content.Shared.Projectiles;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._KS14.KillCredit;

/// <summary>
///     Answers two questions about damage, which are not the same thing:
///     <list type="bullet">
///         <item>
///             <b>What did it</b> (<see cref="GetAttacker"/>): the thing itself, a turret rather than its owner. For
///                 whatever reacts to being hurt, such as an NPC turning to fire back.
///         </item>
///         <item>
///             <b>Who is paid for it</b> (<see cref="GetHitRecipient"/>): a player, asked of the attacker with
///                 <see cref="KsGetCreditRecipientEvent"/> as the hit lands. A player's body answers with its player
///                 and a turret with whoever set it up.
///         </item>
///     </list>
///     Whatever can outlive what it answers for is told the answer early: a bullet is stamped with its shooter's
///         recipient as it is fired, and an explosion is given one as it is queued.
/// </summary>
public sealed partial class KsKillCreditSystem : EntitySystem
{
    /// <summary>
    ///     How many entities may pass the question on (a bullet to its shooter...) before giving up, so a loop of
    ///         them can't hang the server.
    /// </summary>
    private const int MaxDepth = 8;

    [Dependency] private ExplosionSystem _explosionSystem = default!;
    [Dependency] private EntityQuery<ProjectileComponent> _projectileQuery = default!;

    /// <summary>
    ///     What really dealt damage with this <paramref name="origin"/>: the shooter of a bullet, and for damage with
    ///         no origin, inside an explosion, whatever set it off. Never an owner. It may be deleted already.
    /// </summary>
    public EntityUid? GetAttacker(EntityUid? origin)
    {
        var attackerUid = origin ?? _explosionSystem.KsProcessingUser;

        if (attackerUid is { } uid &&
            _projectileQuery.TryComp(uid, out var projectileComponent) &&
            projectileComponent.Shooter is { } shooterUid)
            return shooterUid;

        return attackerUid;
    }

    /// <summary>
    ///     The player who is paid for damage dealt with this <paramref name="origin"/>, or null if nobody is. To be
    ///         asked as the damage lands, while the origin still exists.
    /// </summary>
    public NetUserId? GetHitRecipient(EntityUid? origin)
    {
        return origin is { } originUid ? GetRecipient(originUid) : _explosionSystem.KsProcessingRecipient;
    }

    /// <summary>
    ///     The player who is paid for what <paramref name="uid"/> does, or null if nobody is, or it is gone.
    /// </summary>
    public NetUserId? GetRecipient(EntityUid uid)
    {
        return GetRecipient(uid, depth: 0);
    }

    private NetUserId? GetRecipient(EntityUid uid, int depth)
    {
        if (depth >= MaxDepth || Deleted(uid))
            return null;

        var ev = new KsGetCreditRecipientEvent(Depth: depth);
        RaiseLocalEvent(uid, ref ev);
        return ev.Recipient;
    }

    /// <summary>
    ///     A player's own body is paid for what it does.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnActorGetRecipient(Entity<ActorComponent> entity, ref KsGetCreditRecipientEvent args)
    {
        args.Recipient ??= entity.Comp.PlayerSession.UserId;
    }

    [SubscribeLocalEvent]
    private void OnOwnedGetRecipient(Entity<KsCreditOwnerComponent> entity, ref KsGetCreditRecipientEvent args)
    {
        args.Recipient ??= entity.Comp.OwnerUserId;
    }

    /// <summary>
    ///     A bullet that was never stamped answers for its shooter, while that still exists.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnProjectileGetRecipient(Entity<ProjectileComponent> entity, ref KsGetCreditRecipientEvent args)
    {
        if (args.Recipient == null && entity.Comp.Shooter is { } shooterUid && shooterUid != entity.Owner)
            args.Recipient = GetRecipient(shooterUid, args.Depth + 1);
    }

    /// <summary>
    ///     A bullet is stamped with whoever its shooter answers to, which outlives the shooter: a disposable turret
    ///         blows up on its last shot, before the bullet lands.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnPlayerShotProjectile(ref PlayerShotProjectileEvent args)
    {
        if (GetRecipient(args.User) is { } recipient)
            SetOwner(args.Projectile, recipient);
    }

    /// <summary>
    ///     What comes out of a flatpack answers to whoever unpacked it.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnFlatpackUnpacked(Entity<FlatpackComponent> entity, ref KsFlatpackUnpackedEvent args)
    {
        if (GetRecipient(args.User) is { } recipient)
            SetOwner(args.Unpacked, recipient);
    }

    /// <summary>
    ///     Makes <paramref name="uid"/> answer to a player from here on.
    /// </summary>
    public void SetOwner(EntityUid uid, NetUserId ownerUserId)
    {
        EnsureComp<KsCreditOwnerComponent>(uid).OwnerUserId = ownerUserId;
    }
}
