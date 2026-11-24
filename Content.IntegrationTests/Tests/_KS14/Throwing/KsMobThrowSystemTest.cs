#nullable enable
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Throwing;
using Content.Server.Gravity;
using Content.Shared._KS14.Throwing;
using Content.Shared.Gravity;
using NUnit.Framework;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Throwing;

/// <summary>
///     Throwing a mob along a direction, as whatever struck it does: it flies that way, no faster and no further than
///         its <see cref="KsThrowableMobComponent"/> allows. (Away from an epicentre is the explosion tests'.)
/// </summary>
[TestFixture]
[TestOf(typeof(KsMobThrowSystem))]
public sealed class KsMobThrowSystemTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Dirty = true };

    private readonly EntProtoId _mob = "KsMobThrowTestMob";

    // Caps any force here reaches, so the throw is exactly them.
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: KsMobThrowTestMob
  components:
  - type: KsThrowableMob
    minThrowForce: 10
    speedPerForce: 1
    maxSpeed: 6
    distancePerForce: 1
    maxDistance: 3
    knockdownTime: 1
";

    /// <summary>Far over the test mob's caps.</summary>
    private const float Force = 100f;

    [Test]
    public async Task ThrownAlongTheGivenDirectionWithinItsCaps()
    {
        var testMap = await Pair.CreateTestMap();
        EntityUid mobUid = default, controlUid = default, anchoredUid = default, containedUid = default;
        var direction = new Vector2(0f, 1f);

        await Server.WaitPost(() =>
        {
            var mapSystem = SEntMan.System<SharedMapSystem>();
            for (var x = -6; x <= 6; x++)
            {
                for (var y = -6; y <= 12; y++)
                {
                    mapSystem.SetTile(testMap.Grid.Owner, testMap.Grid.Comp, new Vector2i(x, y), testMap.Tile.Tile);
                }
            }

            var gravityComponent = SEntMan.EnsureComponent<GravityComponent>(testMap.Grid.Owner);
            SEntMan.System<GravitySystem>().EnableGravity(testMap.Grid.Owner, gravityComponent);
            mobUid = SEntMan.SpawnEntity(_mob, new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
            controlUid = SEntMan.SpawnEntity(_mob, new EntityCoordinates(testMap.Grid, -4.5f, 0.5f));
            SEntMan.RemoveComponent<KsThrowableMobComponent>(controlUid);

            anchoredUid = SEntMan.SpawnEntity(_mob, new EntityCoordinates(testMap.Grid, 4.5f, 0.5f));
            SEntMan.System<SharedTransformSystem>().AnchorEntity(anchoredUid);

            var holderUid = SEntMan.SpawnEntity(null, new EntityCoordinates(testMap.Grid, 4.5f, -4.5f));
            containedUid = SEntMan.SpawnEntity(_mob, new EntityCoordinates(testMap.Grid, 4.5f, -4.5f));
            var containerSystem = SEntMan.System<SharedContainerSystem>();
            containerSystem.Insert(containedUid, containerSystem.EnsureContainer<Container>(holderUid, "ks-test-holder"));
        });
        await Pair.RunTicksSync(5);

        KsThrowableMobComponent caps = default!;
        Vector2 start = default, controlStart = default;
        await Server.WaitAssertion(() =>
        {
            var throwSystem = SEntMan.System<KsMobThrowSystem>();
            caps = SEntMan.GetComponent<KsThrowableMobComponent>(mobUid);
            start = SEntMan.GetComponent<TransformComponent>(mobUid).LocalPosition;
            controlStart = SEntMan.GetComponent<TransformComponent>(controlUid).LocalPosition;

            Assert.That(throwSystem.TryThrowMob(mobUid, direction, Force, out var thrown), Is.True, "a throwable mob, given by its uid alone, is thrown");
            Assert.That(thrown!.Value.Distance, Is.EqualTo(caps.MaxDistance), "as far as its cap");
            Assert.That(thrown.Value.Speed, Is.EqualTo(caps.MaxSpeed), "as fast as its cap");
            Assert.That(SEntMan.HasComponent<KsThrownSlamComponent>(mobUid), Is.True, "and marked to slam into what it hits");

            Assert.That(throwSystem.TryThrowMob(controlUid, direction, Force, out _), Is.False,
                "control: a mob without the component isn't thrown");
            Assert.That(SEntMan.GetComponent<TransformComponent>(anchoredUid).Anchored, Is.True, "precondition: anchored");
            Assert.That(throwSystem.TryThrowMob(anchoredUid, direction, Force, out _), Is.False, "an anchored mob isn't thrown");
            Assert.That(throwSystem.TryThrowMob(containedUid, direction, Force, out _), Is.False, "nor one inside a container");
        });

        var topSpeed = 0f;
        for (var tick = 0; tick < 90; tick++)
        {
            await Pair.RunTicksSync(1);
            await Server.WaitPost(() =>
                topSpeed = MathF.Max(topSpeed, SEntMan.GetComponent<PhysicsComponent>(mobUid).LinearVelocity.Length()));
        }

        await Server.WaitAssertion(() =>
        {
            var moved = SEntMan.GetComponent<TransformComponent>(mobUid).LocalPosition - start;
            Assert.That(Vector2.Dot(moved, direction), Is.GreaterThan(caps.MaxDistance * 0.5f), "it flies the given way");
            Assert.That(MathF.Abs(moved.X), Is.LessThan(0.25f), "and not off to the side");
            Assert.That(topSpeed, Is.LessThanOrEqualTo(caps.MaxSpeed * 1.05f), "no faster than its cap");
            Assert.That((SEntMan.GetComponent<TransformComponent>(controlUid).LocalPosition - controlStart).Length(),
                Is.LessThan(0.05f), "control: the mob without it stays put");
        });
    }
}
