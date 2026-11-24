#nullable enable
using System.Numerics;
using Content.IntegrationTests.Tests.Movement;
using Content.Shared.Buckle;
using Content.Shared.Movement.Components;
using Content.Shared.Vehicle.Components;
using Content.Shared.Vehicle.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._KS14.Vehicle;

/// <summary>
///     A vehicle is a loose Dynamic body while parked and a kinematic controller while driven, driven here by a real
///         player through their client, so the predicting client and both Debug body-type asserts take part: any assert
///         firing fails the test.
/// </summary>
[TestOf(typeof(VehicleSystem))]
public sealed class KsVehicleBodyTypeTest : MovementTest
{
    private readonly EntProtoId _cart = "KsVehicleBodyTypeTestCart";

    // Room to drive and coast in.
    protected override int Tiles => 12;

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsVehicleBodyTypeTestCart
  components:
  - type: Physics
    bodyType: Dynamic
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeCircle
          radius: 0.3
        density: 200
        mask:
        - MobMask
        layer:
        - MidImpassable
  - type: InputMover
  - type: MovementSpeedModifier
    baseWalkSpeed: 4
    baseSprintSpeed: 4
  - type: Strap
  - type: Vehicle
    requiresHands: false
    transferDamage: false
  - type: StrapVehicle
";

    private async Task<EntityUid> SpawnCart()
    {
        var origin = SEntMan.GetCoordinates(PlayerCoords);
        var cartUid = await SpawnEntity(_cart, origin.Offset(new Vector2(1f, 0f)));
        await RunTicks(5);
        return cartUid;
    }

    private void AssertParked(EntityUid cartUid, string when)
    {
        Assert.That(SEntMan.GetComponent<PhysicsComponent>(cartUid).BodyType, Is.EqualTo(BodyType.Dynamic),
            $"{when}: a vehicle nobody drives should be a loose Dynamic body");
        Assert.That(SEntMan.GetComponent<InputMoverComponent>(cartUid).CanMove, Is.False,
            $"{when}: and unable to move itself");
        Assert.That(SEntMan.GetComponent<VehicleComponent>(cartUid).Operator, Is.Null, when);
    }

    private void AssertDriven(EntityUid cartUid, string when)
    {
        Assert.That(SEntMan.GetComponent<PhysicsComponent>(cartUid).BodyType, Is.EqualTo(BodyType.KinematicController),
            $"{when}: a driven vehicle should be a kinematic controller, as the mover needs");
        Assert.That(SEntMan.GetComponent<InputMoverComponent>(cartUid).CanMove, Is.True, when);
        Assert.That(SEntMan.GetComponent<VehicleComponent>(cartUid).Operator, Is.EqualTo(SPlayer), when);
    }

    [Test]
    public async Task FlipsBothWays()
    {
        var cartUid = await SpawnCart();
        await Server.WaitAssertion(() => AssertParked(cartUid, "spawned"));

        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.System<SharedBuckleSystem>().TryBuckle(SPlayer, SPlayer, cartUid), Is.True);
            // In the same call that set the relay, before any movement update can see it.
            AssertDriven(cartUid, "on buckling");
        });

        await RunTicks(5);
        await Client.WaitAssertion(() =>
        {
            Assert.That(CEntMan.GetComponent<PhysicsComponent>(ToClient(cartUid)).BodyType, Is.EqualTo(BodyType.KinematicController),
                "the driver's client should have the kinematic controller too");
        });

        await Server.WaitPost(() => Server.System<SharedBuckleSystem>().Unbuckle(SPlayer, SPlayer));
        await RunTicks(5);

        await Server.WaitAssertion(() => AssertParked(cartUid, "after the driver got off"));
        await Client.WaitAssertion(() =>
        {
            Assert.That(CEntMan.GetComponent<PhysicsComponent>(ToClient(cartUid)).BodyType, Is.EqualTo(BodyType.Dynamic),
                "and the client should hear of it");
        });
    }

    /// <summary>
    ///     Off at speed: the body parks without losing its speed, stays awake and slows to a stop on its own; getting
    ///         back on mid-coast drives it again. Either Debug body-type assert firing on the way fails this.
    /// </summary>
    [Test]
    public async Task LeavingAtSpeedCoastsToAStop()
    {
        var cartUid = await SpawnCart();
        var buckleSystem = Server.System<SharedBuckleSystem>();
        var tickRate = (int)Server.ResolveDependency<IGameTiming>().TickRate;

        await Server.WaitAssertion(() => Assert.That(buckleSystem.TryBuckle(SPlayer, SPlayer, cartUid), Is.True));
        await RunTicks(5);

        await SetMovementKey(DirectionFlag.East, BoundKeyState.Down);
        await RunTicks(tickRate / 2);

        var drivenSpeed = 0f;
        await Server.WaitPost(() =>
        {
            drivenSpeed = SEntMan.GetComponent<PhysicsComponent>(cartUid).LinearVelocity.Length();
            buckleSystem.Unbuckle(SPlayer, SPlayer);
        });
        await SetMovementKey(DirectionFlag.East, BoundKeyState.Up);

        Assert.That(drivenSpeed, Is.GreaterThan(1f), "the player's input should have got the vehicle going");

        await RunTicks(1);
        var previousSpeed = 0f;
        await Server.WaitAssertion(() =>
        {
            var physicsComponent = SEntMan.GetComponent<PhysicsComponent>(cartUid);
            AssertParked(cartUid, "the tick after getting off at speed");
            Assert.That(physicsComponent.Awake, Is.True, "a coasting vehicle should be awake");
            previousSpeed = physicsComponent.LinearVelocity.Length();
            Assert.That(previousSpeed, Is.GreaterThan(drivenSpeed * 0.1f),
                "parking shouldn't stop the vehicle dead: it keeps its speed and coasts");
        });

        // Coasts down monotonically, to a stop.
        var stopped = false;
        for (var tick = 0; tick < tickRate * 10 && !stopped; tick++)
        {
            await RunTicks(1);
            await Server.WaitAssertion(() =>
            {
                var speed = SEntMan.GetComponent<PhysicsComponent>(cartUid).LinearVelocity.Length();
                Assert.That(speed, Is.LessThanOrEqualTo(previousSpeed + 0.001f), "a coasting vehicle should only slow down");
                previousSpeed = speed;
                stopped = speed < 0.01f;
            });
        }

        Assert.That(stopped, Is.True, "a parked vehicle should coast to a stop");

        // On again, and off again, without the vehicle ever having been a Dynamic body that could move.
        await Server.WaitAssertion(() =>
        {
            Assert.That(buckleSystem.TryBuckle(SPlayer, SPlayer, cartUid), Is.True);
            AssertDriven(cartUid, "on getting back on");
        });
        await Move(DirectionFlag.West, 0.3f);
        await Server.WaitPost(() => buckleSystem.Unbuckle(SPlayer, SPlayer));
        await RunTicks(3);
        await Server.WaitAssertion(() => AssertParked(cartUid, "after the second ride"));
    }

    /// <summary>
    ///     A map saved while someone drove holds a kinematic vehicle whose operator wasn't saved with it. Loaded, it parks
    ///         and can be driven again.
    /// </summary>
    [Test]
    public async Task DanglingOperatorIsClearedAndTheVehicleParks()
    {
        var cartUid = await SpawnCart();

        await Server.WaitPost(() =>
        {
            var goneUid = SEntMan.SpawnEntity(null, MapCoordinates.Nullspace);
            SEntMan.DeleteEntity(goneUid);

            // As a load would leave it: a kinematic body, and a vehicle starting up naming an operator that isn't there.
            SEntMan.RemoveComponent<VehicleComponent>(cartUid);
            Server.System<SharedPhysicsSystem>().SetBodyType(cartUid, BodyType.KinematicController);
            var vehicleComponent = new VehicleComponent();
#pragma warning disable RA0002
            vehicleComponent.Operator = goneUid;
            vehicleComponent.RequiresHands = false;
#pragma warning restore RA0002
            SEntMan.AddComponent(cartUid, vehicleComponent);
        });
        await RunTicks(3);

        await Server.WaitAssertion(() =>
        {
            AssertParked(cartUid, "after loading");
            Assert.That(Server.System<SharedBuckleSystem>().TryBuckle(SPlayer, SPlayer, cartUid), Is.True);
            AssertDriven(cartUid, "and it should be drivable again");
        });
    }

    /// <summary>
    ///     Unanchoring an input mover makes it a kinematic controller; a parked vehicle goes back to being loose.
    /// </summary>
    [Test]
    public async Task UnanchoringKeepsItParked()
    {
        var cartUid = await SpawnCart();

        await Server.WaitPost(() => Transform.AnchorEntity(cartUid));
        await RunTicks(3);
        await Server.WaitPost(() => Transform.Unanchor(cartUid));
        await RunTicks(3);

        await Server.WaitAssertion(() => AssertParked(cartUid, "after being anchored and unanchored"));
    }
}
