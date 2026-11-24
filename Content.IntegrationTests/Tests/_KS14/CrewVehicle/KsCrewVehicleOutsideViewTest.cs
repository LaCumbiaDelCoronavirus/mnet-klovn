#nullable enable
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server._KS14.CrewVehicle.Entry;
using Content.Server._KS14.CrewVehicle.View;
using Content.Shared._KS14.CrewVehicle.Interior;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._KS14.CrewVehicle;

/// <summary>
///     Everyone inside a crew vehicle, seated or not, is sent what is around it outside, for as long as they are inside.
/// </summary>
/// <remarks>
///     The occupant stands on the interior map, so their own PVS covers only the interior: anything outside that reaches
///         the client came through the viewer on the hull.
/// </remarks>
[TestFixture]
[TestOf(typeof(KsCrewVehicleOutsidePvsSystem))]
public sealed class KsCrewVehicleOutsideViewTest : KsCrewVehicleViewTestBase
{
    [Test]
    public async Task OccupantIsSentTheOutside()
    {
        var scene = await SetUpVehicleAsync();
        await EnterAsPlayerAsync(scene);

        var near = await SpawnMarkerAsync(scene, 2f);
        var far = await SpawnMarkerAsync(scene, FarDistance * scene.PvsRange);
        var clientHasHull = true;
        var hullNetEntity = SEntMan.GetNetEntity(scene.Hull);
        for (var sample = 0; sample < 3; sample++)
        {
            await Pair.RunTicksSync(SettleTicks);
            clientHasHull &= await ClientHasAsync(hullNetEntity);
        }

        Assert.That(await ClientHasAsync(near), Is.True,
            "an entity spawned beside the vehicle after they got in should reach the occupant's client");
        Assert.That(await ClientHasAsync(far), Is.False,
            "an entity far from both the vehicle and the interior should not reach the client");
        Assert.That(clientHasHull, Is.True, "the client should hold the vehicle for as long as they are inside");
        await Server.WaitAssertion(() =>
        {
            var viewers = SubscribedViewers();
            Assert.That(viewers, Has.Count.EqualTo(1), "one viewer per occupant");
            Assert.That(SComp<TransformComponent>(viewers[0]).ParentUid, Is.EqualTo(scene.Hull), "riding on the hull");
        });
    }

    [Test]
    public async Task LeavingEndsTheSubscription()
    {
        var scene = await SetUpVehicleAsync();
        var mob = await EnterAsPlayerAsync(scene);

        EntityUid viewer = default;
        await Server.WaitPost(() =>
        {
            viewer = SubscribedViewers().Single();
            var interiorMap = SComp<KsCrewVehicleInteriorComponent>(scene.Hull).InteriorMap;
            var door = SEntMan.AllEntities<KsCrewVehicleExitComponent>()
                .First(exit => exit.Comp.Doorway == "left" && SComp<TransformComponent>(exit).MapUid == interiorMap);
            Assert.That(SEntMan.System<KsCrewVehicleExitSystem>().TryStartExit(door, mob), Is.True);
        });
        await Pair.RunTicksSync(TicksFor(SComp<KsCrewVehicleInteriorComponent>(scene.Hull).ExitDelay));

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<KsCrewVehicleOccupantComponent>(mob), Is.False, "precondition: they got out");
            Assert.That(SubscribedViewers(), Is.Empty, "after leaving, the view subscription should be gone");
            Assert.That(SEntMan.Deleted(viewer), Is.True, "and its viewer with it");
            Assert.That(SEntMan.HasComponent<KsCrewVehicleOutsideViewComponent>(mob), Is.False);
        });

        // Driven off out of their own sight, the vehicle's surroundings are no longer theirs.
        await Server.WaitPost(() =>
            SEntMan.System<SharedTransformSystem>().SetWorldPosition(scene.Hull, new Vector2(FarDistance * scene.PvsRange, 0f)));
        var nearHull = await SpawnMarkerAsync(scene, 2f);
        await Pair.RunTicksSync(SettleTicks);
        Assert.That(await ClientHasAsync(nearHull), Is.False,
            "once outside, what is around the vehicle should no longer be sent to them");
    }

    /// <summary>
    ///     Getting in by a door is not the only way to be inside: whoever comes to be on the interior grid is an
    ///         occupant, and gets the outside once a player is in them, and loses it when the player leaves.
    /// </summary>
    /// <param name="playerFirst">The player is in the mob before it is teleported in; otherwise it is spawned inside and the player joins it.</param>
    [TestCase(true)]
    [TestCase(false)]
    public async Task ViewerFollowsThePlayer(bool playerFirst)
    {
        var scene = await SetUpVehicleAsync();
        var insideCoordinates = new EntityCoordinates(scene.InteriorGrid, new Vector2(2.5f, 2.5f));

        EntityUid mob = default;
        await Server.WaitPost(() =>
        {
            if (playerFirst)
            {
                mob = SEntMan.Spawn(MobPrototype, new MapCoordinates(scene.Left, scene.OutsideMapId));
                Server.PlayerMan.SetAttachedEntity(ServerSession!, mob);
                SEntMan.System<SharedTransformSystem>().SetCoordinates(mob, insideCoordinates);
            }
            else
            {
                mob = SEntMan.SpawnAtPosition(MobPrototype, insideCoordinates);
            }
        });
        await Pair.RunTicksSync(SettleTicks);

        if (!playerFirst)
        {
            await Server.WaitAssertion(() =>
            {
                Assert.That(SEntMan.HasComponent<KsCrewVehicleOccupantComponent>(mob), Is.True, "precondition: spawned inside is inside");
                Assert.That(SEntMan.HasComponent<KsCrewVehicleOutsideViewComponent>(mob), Is.False,
                    "nobody to send the outside to, so no viewer");
            });
            await Server.WaitPost(() => Server.PlayerMan.SetAttachedEntity(ServerSession!, mob));
            await Pair.RunTicksSync(SettleTicks);
        }

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<KsCrewVehicleOccupantComponent>(mob), Is.True, "precondition: they are inside");
            Assert.That(SubscribedViewers(), Has.Count.EqualTo(1), "with a player in them, they should have a viewer");
        });
        var near = await SpawnMarkerAsync(scene, 2f);
        await Pair.RunTicksSync(SettleTicks);
        Assert.That(await ClientHasAsync(near), Is.True, "and be sent what is beside the vehicle");

        // The player goes elsewhere, somewhere that sees nothing of the vehicle.
        var elsewhere = await Pair.CreateTestMap();
        await Server.WaitPost(() =>
            Server.PlayerMan.SetAttachedEntity(ServerSession!, SEntMan.SpawnAtPosition(MobPrototype, elsewhere.GridCoords)));
        await Pair.RunTicksSync(SettleTicks);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SubscribedViewers(), Is.Empty, "the player left the mob, so their session should lose its viewer");
            Assert.That(SEntMan.HasComponent<KsCrewVehicleOutsideViewComponent>(mob), Is.False);
        });
        var nearAfter = await SpawnMarkerAsync(scene, 2f);
        await Pair.RunTicksSync(SettleTicks);
        Assert.That(await ClientHasAsync(nearAfter), Is.False,
            "with the player gone from the occupant, what is beside the vehicle should not be sent to them");
    }

    [Test]
    public async Task VehicleDeletedWithSomeoneInside()
    {
        var scene = await SetUpVehicleAsync();
        var mob = await EnterAsPlayerAsync(scene);

        EntityUid viewer = default;
        await Server.WaitPost(() =>
        {
            viewer = SubscribedViewers().Single();
            SEntMan.DeleteEntity(scene.Hull);
        });
        await Pair.RunTicksSync(SettleTicks);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SComp<TransformComponent>(mob).MapID, Is.EqualTo(scene.OutsideMapId), "precondition: they were put outside");
            Assert.That(SEntMan.Deleted(viewer), Is.True, "the viewer goes with the vehicle");
            Assert.That(SubscribedViewers(), Is.Empty);
            Assert.That(ServerSession!.ViewSubscriptions, Does.Not.Contain(viewer));
            Assert.That(SEntMan.HasComponent<KsCrewVehicleOutsideViewComponent>(mob), Is.False);
        });
    }
}
