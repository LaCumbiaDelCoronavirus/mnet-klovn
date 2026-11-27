#nullable enable
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.CCVar;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Destructible;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Systems;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.UnitTesting;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Projectiles;

/// <summary>
///     A penetrating round hitting something that has no damage modifier set: the framework scales the round's damage to
///         what the target needs to be penetrated (<c>PredictedProjectileSystem.CalculateMultiplier</c>), as it does
///         through a modifier set. It once scaled it the wrong way round, so a round of 100 against a need of 250 dealt 40.
/// </summary>
[TestFixture]
public sealed class KsPenetrationUnmodifiedTargetTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false, Dirty = true };

    private readonly EntProtoId _target = "KsPenetrationUnmodifiedTargetTestWall";
    private readonly EntProtoId _round = "KsPenetrationUnmodifiedTargetTestRound";
    private readonly EntProtoId _shooter = "KsPenetrationUnmodifiedTargetTestShooter";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsPenetrationUnmodifiedTargetTestWall
  components:
  - type: Physics
    bodyType: Static
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeAabb
          bounds: ""-0.5,-0.5,0.5,0.5""
        layer:
        - MidImpassable
        - BulletImpassable
  - type: Damageable
  - type: Injurable
    damageContainer: StructuralInorganic
  - type: Destructible
    thresholds:
    - trigger:
        !type:DamageTrigger
        damage: 1000
      behaviors:
      - !type:DoActsBehavior
        acts: [ ""Destruction"" ]

- type: entity
  id: KsPenetrationUnmodifiedTargetTestShooter

- type: entity
  parent: BaseBullet
  id: KsPenetrationUnmodifiedTargetTestRound
  components:
  - type: Projectile
    damage:
      types:
        Structural: 100
    penetrationThreshold: 1000
    penetrationDamageTypeRequirement:
    - Structural
";

    /// <summary>
    ///     Against a target with no modifier set, the round deals what the target needs to be penetrated (the share of its
    ///         destruction a hit has to do, <see cref="KsCCVars.GunPenetrationMinShots"/>), whatever it was fired with,
    ///         and what it spent of its budget is that amount, so it flies on with the rest.
    /// </summary>
    [TestCase(4f)]
    [TestCase(2f)]
    public async Task RoundDealsWhatTheTargetNeedsToBePenetrated(float minShots)
    {
        await OverrideCVar(Side.Server, KsCCVars.GunPenetrationMinShots, minShots);
        var testMap = await Pair.CreateTestMap();
        EntityUid target = default;
        EntityUid round = default;

        await Server.WaitPost(() =>
        {
            target = SEntMan.Spawn(_target, new MapCoordinates(new Vector2(0f, 10f), testMap.MapId));
            var shooter = SEntMan.Spawn(_shooter, new MapCoordinates(new Vector2(20f, 20f), testMap.MapId));
            round = SEntMan.Spawn(_round, new MapCoordinates(new Vector2(0f, 7f), testMap.MapId));
            SEntMan.System<SharedGunSystem>().ShootProjectile(round, new Vector2(0f, 1f), Vector2.Zero, gunUid: null, user: shooter, speed: 20f);
        });

        var required = SEntMan.System<SharedDestructibleSystem>().DestroyedAt(target).Float() / minShots;
        var roundDamage = SProtoMan.Index(_round).TryGetComponent<ProjectileComponent>(out var prototypeProjectile, SEntMan.ComponentFactory)
            ? prototypeProjectile.Damage.GetTotal().Float() * SEntMan.System<DamageableSystem>().UniversalProjectileDamageModifier
            : 0f;
        Assert.That(roundDamage, Is.GreaterThan(0f), "precondition: the round has damage");
        Assert.That(roundDamage, Is.Not.EqualTo(required).Within(1f), "precondition: the round's damage is not already what it needs, so scaling shows");

        float? penetrated = null;
        bool? spent = null;
        for (var tick = 0; tick < 3 * SGameTiming.TickRate && penetrated == null; tick++)
        {
            await Pair.RunTicksSync(1);
            await Server.WaitPost(() =>
            {
                if (Damage(target) <= 0f || !SEntMan.TryGetComponent<ProjectileComponent>(round, out var projectile))
                    return;

                penetrated = projectile.PenetrationAmount.Float();
                spent = projectile.ProjectileSpent;
            });
        }

        await Server.WaitAssertion(() =>
        {
            Assert.That(Damage(target), Is.EqualTo(required).Within(1f), "it deals what the target needs to be penetrated");
            Assert.That(penetrated, Is.Not.Null, "the round is there to be read after its hit");
            Assert.That(penetrated!.Value, Is.EqualTo(required).Within(0.01f), "and spends that much of its budget");
            Assert.That(spent, Is.False, "it has budget left and goes on");
        });
    }

    private float Damage(EntityUid uid) =>
        SEntMan.System<DamageableSystem>().GetTotalDamage(SEntity<DamageableComponent>(uid).AsNullable()).Float();
}
