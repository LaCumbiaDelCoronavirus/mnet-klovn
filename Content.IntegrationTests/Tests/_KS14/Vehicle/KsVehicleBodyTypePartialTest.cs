#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.Lawnmower;
using Content.Shared.Rotatable;
using Content.Shared.Vehicle.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Vehicle;

/// <summary>
///     The vehicles that park as loose objects say so in their prototypes, the upstream ones through the partials in
///         <c>_KS14/Entities/Vehicles/Partials/body_type.yml</c>. Each case also checks that the prototype kept what
///         upstream gave it: a partial that stopped applying, or replaced the prototype, looks the same otherwise.
/// </summary>
public sealed class KsVehicleBodyTypePartialTest : GameTest
{
    private static readonly EntProtoId Janicart = "VehicleJanicart";
    private static readonly EntProtoId Wheelchair = "VehicleWheelchair";
    private static readonly EntProtoId OfficeChair = "ChairOfficeLight";
    private static readonly EntProtoId BigBox = "BigBox";
    private static readonly EntProtoId StealthBox = "StealthBox";
    private static readonly EntProtoId Lawnmower = "KsLawnmower";
    private static readonly EntProtoId Ripley = "MechRipley";

    private EntityPrototype IndexWithPhysics(EntProtoId id, out PhysicsComponent physicsComponent)
    {
        var prototype = Server.ProtoMan.Index(id);
        Assert.That(prototype.TryComp(out PhysicsComponent? physics, Server.ResolveDependency<IComponentFactory>()), Is.True,
            $"{id} should have a body");
        physicsComponent = physics!;
        return prototype;
    }

    private bool Has<T>(EntityPrototype prototype) where T : IComponent, new()
    {
        return prototype.TryComp(out T? _, Server.ResolveDependency<IComponentFactory>());
    }

    [Test]
    public async Task ParkedVehiclesAreDynamicAndKeepTheirUpstreamData()
    {
        await Server.WaitAssertion(() =>
        {
            // Children of the abstract BaseVehicleStrap: the partial on the parent reaches them.
            foreach (var id in new[] { Janicart, Wheelchair })
            {
                var prototype = IndexWithPhysics(id, out var physicsComponent);
                Assert.That(physicsComponent.BodyType, Is.EqualTo(BodyType.Dynamic), $"{id} should park Dynamic");
                Assert.That(Has<StrapVehicleComponent>(prototype), Is.True, $"{id} should still be a strap vehicle");
            }

            var chair = IndexWithPhysics(OfficeChair, out var chairPhysics);
            Assert.That(chairPhysics.BodyType, Is.EqualTo(BodyType.Dynamic), "office chairs should park Dynamic");
            Assert.That(Has<RotatableComponent>(chair) && Has<StrapVehicleComponent>(chair), Is.True,
                "and still be rotatable chairs you can ride");

            foreach (var id in new[] { BigBox, StealthBox })
            {
                var prototype = IndexWithPhysics(id, out var physicsComponent);
                Assert.That(physicsComponent.BodyType, Is.EqualTo(BodyType.Dynamic), $"{id} should park Dynamic");
                Assert.That(Has<ContainerVehicleComponent>(prototype), Is.True, $"{id} should still be driven from inside");
            }

            var mower = IndexWithPhysics(Lawnmower, out var mowerPhysics);
            Assert.That(mowerPhysics.BodyType, Is.EqualTo(BodyType.Dynamic), "the lawnmower should park Dynamic");
            Assert.That(Has<KsLawnmowerComponent>(mower), Is.True);

            // Control: mechs are left as they were.
            IndexWithPhysics(Ripley, out var mechPhysics);
            Assert.That(mechPhysics.BodyType, Is.EqualTo(BodyType.KinematicController), "mechs are not changed");
        });
    }
}
