#nullable enable
using System.Numerics;
using System.Threading.Tasks;
using Content.Shared._KS14.CrewVehicle.Pull;
using Content.Shared.Movement.Pulling.Systems;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.CrewVehicle;

/// <summary>
///     Pulling stops the puller and the pulled colliding, but a crew vehicle's hull stays solid to the one pulling it.
/// </summary>
[TestOf(typeof(KsCrewVehiclePullCollisionSystem))]
public sealed class KsCrewVehiclePullCollisionTest : KsCrewVehicleDriveTestBase
{
    private readonly EntProtoId _pullableItem = "KsCrewVehiclePullCollisionTestItem";

    [TestPrototypes]
    private const string Prototypes = @"
# A hard, pullable thing a mob can't walk through, like the hull but not a vehicle.
- type: entity
  id: KsCrewVehiclePullCollisionTestItem
  components:
  - type: Physics
    bodyType: Dynamic
    fixedRotation: true
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeAabb
          bounds: ""-0.5,-0.8,0.5,0.8""
        density: 400
        mask:
        - MobMask
        layer:
        - MidImpassable
  - type: Pullable
";

    /// <summary>Puts the puller on top of the pulled thing, lets physics run, and says how far apart their centres end up.</summary>
    private async Task<float> OverlapThenMeasureAsync(EntityUid pulled, bool pull)
    {
        EntityUid puller = default;
        await Server.WaitPost(() => puller = SEntMan.SpawnAtPosition(Human, SEntMan.GetComponent<TransformComponent>(pulled).Coordinates.Offset(new Vector2(0f, -2f))));
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            var pulling = SEntMan.System<PullingSystem>();
            if (pull)
                Assert.That(pulling.TryStartPull(puller, pulled), Is.True, "precondition: the pull starts");
        });
        await RunTicks(2);

        // Onto the pulled thing's edge: pushed back out only if the two collide.
        await Server.WaitPost(() =>
        {
            var transformSystem = SEntMan.System<SharedTransformSystem>();
            transformSystem.SetWorldPosition(puller, transformSystem.GetWorldPosition(pulled) + new Vector2(0f, -0.4f));
            // A sleeping body keeps the contacts it had before it was moved.
            var physicsSystem = SEntMan.System<Robust.Shared.Physics.Systems.SharedPhysicsSystem>();
            physicsSystem.WakeBody(puller);
            physicsSystem.WakeBody(pulled);
        });
        await RunTicks(TickRate);

        var distance = 0f;
        await Server.WaitPost(() =>
        {
            var transformSystem = SEntMan.System<SharedTransformSystem>();
            distance = (transformSystem.GetWorldPosition(puller) - transformSystem.GetWorldPosition(pulled)).Length();
        });
        return distance;
    }

    [Test]
    public async Task HullStaysSolidToItsPullerButAnOrdinaryItemDoesNot()
    {
        var scene = await SpawnHullAsync(new Vector2(0f, 6f), EngineHullPrototype);
        EntityUid item = default;
        await Server.WaitPost(() => item = SEntMan.SpawnAtPosition(_pullableItem, OriginCoordinates.Offset(new Vector2(8f, 6f))));
        await RunTicks(5);

        var hullDistance = await OverlapThenMeasureAsync(scene.Hull, pull: true);
        var itemDistance = await OverlapThenMeasureAsync(item, pull: true);
        var itemUnpulledDistance = await OverlapThenMeasureAsync(item, pull: false);

        TestContext.Out.WriteLine($"hull {hullDistance} m, pulled item {itemDistance} m, unpulled item {itemUnpulledDistance} m");
        Assert.Multiple(() =>
        {
            Assert.That(itemUnpulledDistance, Is.GreaterThan(0.9f), "control: an unpulled item is solid, and pushes the puller out");
            Assert.That(itemDistance, Is.LessThan(0.6f), "an ordinary pulled item doesn't collide with its puller");
            Assert.That(hullDistance, Is.GreaterThan(0.9f), "a pulled hull still collides with its puller");
        });
    }
}
