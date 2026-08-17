using System.Numerics;
using System.Reflection;
using Content.IntegrationTests.Fixtures;
using Content.Shared.ActionBlocker;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Vehicle.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;

namespace Content.IntegrationTests.Tests._KS14.Vehicles
{
    /// <summary>
    /// Diagnostic test for the "can't move around in a buckled vehicle" bug report.
    /// Traces the real prototype (VehicleWheelchair) and the real runtime chain:
    /// buckle -> TrySetOperator -> SetRelay -> HandleDirChange forwarding -> HandleMobMovement.
    /// </summary>
    [TestFixture]
    public sealed class VehicleMovementTest : GameTest
    {
        private const string RiderDummyId = "VehicleTestRiderDummy";

        [TestPrototypes]
        private const string Prototypes = $@"
- type: entity
  name: {RiderDummyId}
  id: {RiderDummyId}
  components:
  - type: Buckle
  - type: Hands
  - type: ComplexInteraction
  - type: InputMover
  - type: Physics
    bodyType: KinematicController
  - type: Body
    prototype: Human
  - type: StandingState
";

        [Test]
        public async Task WheelchairPhysicsBodyTypeIsKinematicController()
        {
            var server = Pair.Server;
            var testMap = await Pair.CreateTestMap();
            var coords = testMap.GridCoords;

            await server.WaitAssertion(() =>
            {
                var wheelchair = SEntMan.SpawnEntity("VehicleWheelchair", coords);
                var physics = SEntMan.GetComponent<PhysicsComponent>(wheelchair);

                // BaseVehicleStrap sets bodyType: KinematicController, but VehicleWheelchair
                // has parent: [BaseVehicleStrap, BaseFoldable, BaseItem] and BaseItem sets
                // bodyType: Dynamic. Confirm inheritance resolves to the vehicle's own value.
                Assert.That(physics.BodyType, Is.EqualTo(BodyType.KinematicController),
                    $"VehicleWheelchair resolved BodyType is {physics.BodyType}, expected KinematicController. " +
                    "This likely comes from multi-parent YAML inheritance being clobbered by BaseItem's Physics component.");
            });
        }

        [Test]
        public async Task RidingWheelchairSetsUpRelayCorrectly()
        {
            var server = Pair.Server;
            var testMap = await Pair.CreateTestMap();
            var coords = testMap.GridCoords;

            var buckleSystem = SEntMan.System<SharedBuckleSystem>();

            EntityUid human = default;
            EntityUid wheelchair = default;

            await server.WaitAssertion(() =>
            {
                human = SEntMan.SpawnEntity(RiderDummyId, coords);
                wheelchair = SEntMan.SpawnEntity("VehicleWheelchair", coords);

                Assert.That(buckleSystem.TryBuckle(human, human, wheelchair), Is.True, "Failed to buckle rider to wheelchair");
            });

            await server.WaitRunTicks(2);

            await server.WaitAssertion(() =>
            {
                Assert.Multiple(() =>
                {
                    var vehicle = SEntMan.GetComponent<VehicleComponent>(wheelchair);
                    Assert.That(vehicle.Operator, Is.EqualTo(human), "VehicleComponent.Operator was not set to the buckled rider");

                    Assert.That(SEntMan.HasComponent<RelayInputMoverComponent>(human), Is.True, "Rider is missing RelayInputMoverComponent");
                    var relay = SEntMan.GetComponent<RelayInputMoverComponent>(human);
                    Assert.That(relay.RelayEntity, Is.EqualTo(wheelchair), "Rider's relay does not point at the wheelchair");

                    Assert.That(SEntMan.HasComponent<MovementRelayTargetComponent>(wheelchair), Is.True, "Wheelchair is missing MovementRelayTargetComponent");

                    var humanMover = SEntMan.GetComponent<InputMoverComponent>(human);
                    Assert.That(humanMover.CanMove, Is.True, "Rider's InputMoverComponent.CanMove is false - buckling is blocking movement despite the relay");

                    var chairMover = SEntMan.GetComponent<InputMoverComponent>(wheelchair);
                    Assert.That(chairMover.CanMove, Is.True, "Wheelchair's own InputMoverComponent.CanMove is false");

                    var chairPhysics = SEntMan.GetComponent<PhysicsComponent>(wheelchair);
                    Assert.That(chairPhysics.BodyType, Is.EqualTo(BodyType.KinematicController), "Wheelchair physics body is not KinematicController");
                });
            });
        }

        [Test]
        public async Task RidingWheelchairActuallyMovesOnInput()
        {
            var server = Pair.Server;
            var testMap = await Pair.CreateTestMap();
            var coords = testMap.GridCoords;

            var buckleSystem = SEntMan.System<SharedBuckleSystem>();
            var moverController = SEntMan.System<SharedMoverController>();
            var xformSystem = SEntMan.System<Robust.Shared.GameObjects.SharedTransformSystem>();

            // HandleDirChange is private; it is the exact method the real client-input keybind
            // handler (MoverDirInputCmdHandler) calls, and it contains the relay-forwarding logic.
            var handleDirChange = typeof(SharedMoverController).GetMethod(
                "HandleDirChange",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(handleDirChange, Is.Not.Null, "Could not find SharedMoverController.HandleDirChange via reflection");

            EntityUid human = default;
            EntityUid wheelchair = default;
            Vector2 startPos = default;

            await server.WaitAssertion(() =>
            {
                human = SEntMan.SpawnEntity(RiderDummyId, coords);
                wheelchair = SEntMan.SpawnEntity("VehicleWheelchair", coords);

                Assert.That(buckleSystem.TryBuckle(human, human, wheelchair), Is.True);
            });

            await server.WaitRunTicks(2);

            await server.WaitAssertion(() =>
            {
                startPos = xformSystem.GetWorldPosition(wheelchair);

                // Simulate pressing "move north" while riding, exactly like MoverDirInputCmdHandler does
                // for session.AttachedEntity.
                handleDirChange!.Invoke(moverController, new object[] { (EntityUid?)human, Direction.North, (ushort)0, true });
            });

            await server.WaitRunTicks(30);

            await server.WaitAssertion(() =>
            {
                var endPos = xformSystem.GetWorldPosition(wheelchair);
                Assert.That((endPos - startPos).Length(), Is.GreaterThan(0.01f),
                    $"Wheelchair did not move after simulated movement input. Start: {startPos}, End: {endPos}");
            });
        }
    }
}
