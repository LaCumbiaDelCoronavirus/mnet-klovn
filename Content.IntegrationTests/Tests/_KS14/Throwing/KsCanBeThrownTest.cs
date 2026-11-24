#nullable enable
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.Throwing;
using Content.Shared.Throwing;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Throwing;

/// <summary>
///     <see cref="KsCanBeThrownEvent"/> lets an entity refuse every throw, whoever or whatever does it.
/// </summary>
[TestOf(typeof(ThrowingSystem))]
public sealed class KsCanBeThrownTest : GameTest
{
    private readonly EntProtoId _thrown = "KsCanBeThrownTestCrate";
    private readonly EntProtoId _refuser = "KsCanBeThrownTestRefuser";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsCanBeThrownTestCrate
  components:
  - type: Physics
    bodyType: Dynamic
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeCircle
          radius: 0.3
        density: 50

- type: entity
  parent: KsCanBeThrownTestCrate
  id: KsCanBeThrownTestRefuser
  components:
  - type: KsCanBeThrownTestRefuser
";

    [Test]
    public async Task ARefusingEntityIsNotThrown()
    {
        var map = await Pair.CreateTestMap();
        EntityUid crateUid = default, refuserUid = default;

        await Server.WaitPost(() =>
        {
            crateUid = Server.EntMan.SpawnEntity(_thrown, map.MapCoords);
            refuserUid = Server.EntMan.SpawnEntity(_refuser, new MapCoordinates(map.MapCoords.Position + new Vector2(0f, 3f), map.MapCoords.MapId));
        });
        await Pair.RunTicksSync(2);

        await Server.WaitAssertion(() =>
        {
            var throwingSystem = Server.EntMan.System<ThrowingSystem>();
            throwingSystem.TryThrow(crateUid, new Vector2(4f, 0f), baseThrowSpeed: 8f);
            throwingSystem.TryThrow(refuserUid, new Vector2(4f, 0f), baseThrowSpeed: 8f);

            Assert.That(Server.EntMan.HasComponent<ThrownItemComponent>(crateUid), Is.True, "control: an ordinary body is thrown");
            Assert.That(Server.EntMan.HasComponent<ThrownItemComponent>(refuserUid), Is.False,
                "an entity refusing KsCanBeThrownEvent shouldn't be thrown");
            Assert.That(Server.EntMan.GetComponent<PhysicsComponent>(refuserUid).LinearVelocity.Length(), Is.Zero,
                "or set moving");
        });
    }
}

[RegisterComponent]
public sealed partial class KsCanBeThrownTestRefuserComponent : Component;

public sealed partial class KsCanBeThrownTestRefuserSystem : EntitySystem
{
    [SubscribeLocalEvent]
    private void OnCanBeThrown(Entity<KsCanBeThrownTestRefuserComponent> entity, ref KsCanBeThrownEvent args)
    {
        args.Cancelled = true;
    }
}
