#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Shared._KS14.CrewVehicle.View;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Vehicle.Components;
using Content.Shared.Vehicle.Systems;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._KS14.CrewVehicle;

/// <summary>
///     Sitting in a view seat puts the occupant's eye outside on the vehicle, at the seat's zoom, cursor lean and PVS
///         scale; standing puts it all back.
/// </summary>
[TestFixture]
[TestOf(typeof(KsCrewVehicleViewSystem))]
public sealed class KsCrewVehicleSeatViewTest : KsCrewVehicleViewTestBase
{
    /// <summary>
    ///     The occupant's zoom before sitting: neither the default nor the seat's, so that restoring it cannot pass by
    ///         resetting it.
    /// </summary>
    private static readonly Vector2 ZoomBefore = new(1.2f, 1.2f);
    private static readonly Vector2 MaxZoomBefore = new(1.3f, 1.3f);

    private sealed record SeatScene(VehicleScene Vehicle, EntityUid Mob, Entity<KsCrewVehicleViewSeatComponent> ViewSeat, EntityUid PassengerSeat);

    /// <summary>
    ///     The player's mob, spawned inside between the two seats, with its zoom set to <see cref="ZoomBefore"/>.
    /// </summary>
    private async Task<SeatScene> SetUpSeatsAsync()
    {
        var vehicle = await SetUpVehicleAsync();
        EntityUid mob = default, passengerSeat = default;
        Entity<KsCrewVehicleViewSeatComponent> viewSeat = default;
        await Server.WaitPost(() =>
        {
            var interiorMap = SComp<TransformComponent>(vehicle.InteriorGrid).MapUid;
            viewSeat = SEntMan.AllEntities<KsCrewVehicleViewSeatComponent>()
                .Single(seat => SComp<TransformComponent>(seat).MapUid == interiorMap);
            passengerSeat = SEntMan.AllEntities<StrapComponent>()
                .Single(seat => SComp<TransformComponent>(seat).MapUid == interiorMap && seat.Owner != viewSeat.Owner)
                .Owner;

            mob = SEntMan.SpawnAtPosition(MobPrototype, new EntityCoordinates(vehicle.InteriorGrid, new Vector2(2f, 1.5f)));
            Server.PlayerMan.SetAttachedEntity(ServerSession!, mob);
            var contentEyeSystem = SEntMan.System<SharedContentEyeSystem>();
            contentEyeSystem.SetMaxZoom(mob, MaxZoomBefore);
            contentEyeSystem.SetZoom(mob, ZoomBefore);
        });
        await Pair.RunTicksSync(SettleTicks);

        await Server.WaitAssertion(() =>
            Assert.That(SubscribedViewers(), Has.Count.EqualTo(1), "precondition: the occupant is sent the outside"));
        return new SeatScene(vehicle, mob, viewSeat, passengerSeat);
    }

    private void Buckle(EntityUid mob, EntityUid seat)
    {
        Assert.That(SEntMan.System<SharedBuckleSystem>().TryBuckle(mob, null, seat, popup: false), Is.True, "precondition: sat down");
    }

    private void Unbuckle(EntityUid mob)
    {
        SEntMan.System<SharedBuckleSystem>().Unbuckle((mob, null), null);
    }

    private float ViewerPvsScale()
    {
        return SComp<EyeComponent>(SubscribedViewers().Single()).PvsScale;
    }

    [Test]
    public async Task SittingLooksOutside()
    {
        var scene = await SetUpSeatsAsync();
        var seat = scene.ViewSeat.Comp;
        var vehicle = scene.Vehicle;
        Assert.That(seat.PvsScale, Is.GreaterThanOrEqualTo(3f), "precondition: the seat's scale reaches past the normal range");

        // Out of reach at scale 1, in reach at the seat's.
        var wideDistance = vehicle.PvsRange * (seat.PvsScale + 1f) / 4f;
        var wide = await SpawnMarkerAsync(vehicle, wideDistance);
        await Pair.RunTicksSync(SettleTicks);
        Assert.That(await ClientHasAsync(wide), Is.False, "precondition: standing, the far side of the seat's range is not sent");

        await Server.WaitPost(() => Buckle(scene.Mob, scene.ViewSeat));
        await Pair.RunTicksSync(SettleTicks);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SComp<EyeComponent>(scene.Mob).Target, Is.EqualTo(vehicle.Hull), "seated, the eye should be on the vehicle");
            Assert.That(SComp<ContentEyeComponent>(scene.Mob).TargetZoom, Is.EqualTo(seat.Zoom), "at the seat's zoom");
            Assert.That(SComp<ContentEyeComponent>(scene.Mob).MaxZoom, Is.EqualTo(seat.Zoom), "and no further out");
            Assert.That(ViewerPvsScale(), Is.EqualTo(seat.PvsScale), "the viewer should take the seat's PVS scale");
        });
        await Client.WaitAssertion(() =>
        {
            Assert.That(CComp<EyeComponent>(ToClientUid(scene.Mob)).Target, Is.EqualTo(ToClientUid(vehicle.Hull)),
                "the client should keep the eye on the vehicle");
            Assert.That(CComp<ContentEyeComponent>(ToClientUid(scene.Mob)).TargetZoom, Is.EqualTo(seat.Zoom));
        });
        Assert.That(await ClientHasAsync(wide), Is.True, "seated, the seat's PVS scale should widen what is sent");

        await Server.WaitPost(() => Unbuckle(scene.Mob));
        await Pair.RunTicksSync(SettleTicks);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SComp<EyeComponent>(scene.Mob).Target, Is.Null, "standing, the eye should be back on the occupant");
            Assert.That(SComp<ContentEyeComponent>(scene.Mob).TargetZoom, Is.EqualTo(ZoomBefore), "with the zoom they had");
            Assert.That(SComp<ContentEyeComponent>(scene.Mob).MaxZoom, Is.EqualTo(MaxZoomBefore), "and the limit they had");
            Assert.That(ViewerPvsScale(), Is.EqualTo(1f), "the viewer should be back to scale 1");
            Assert.That(SEntMan.HasComponent<KsCrewVehicleViewerComponent>(scene.Mob), Is.False);
        });
        await Client.WaitAssertion(() =>
            Assert.That(CComp<EyeComponent>(ToClientUid(scene.Mob)).Target, Is.Null, "the client's eye too"));

        var near = await SpawnMarkerAsync(vehicle, 2f);
        await Pair.RunTicksSync(SettleTicks);
        Assert.That(await ClientHasAsync(near), Is.True,
            "standing, still inside, they should still be sent what is beside the vehicle: clearing the eye did not drop it");
    }

    /// <summary>
    ///     A driver seat is also a view seat, and the upstream vehicle system points its operator's eye at the vehicle
    ///         and clears it as well. Losing the operator while still seated must not take the view away.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task OperatorAndSeatShareTheTarget(bool seatFirst)
    {
        var scene = await SetUpSeatsAsync();
        var hull = scene.Vehicle.Hull;
        var vehicleSystem = SEntMan.System<VehicleSystem>();

        await Server.WaitPost(() =>
        {
            if (seatFirst)
                Buckle(scene.Mob, scene.ViewSeat);

            Assert.That(vehicleSystem.TrySetOperator(SEntity<VehicleComponent>(hull), scene.Mob), Is.True, "precondition: operating");

            if (!seatFirst)
                Buckle(scene.Mob, scene.ViewSeat);
        });
        await Pair.RunTicksSync(SettleTicks);
        await Server.WaitAssertion(() =>
            Assert.That(SComp<EyeComponent>(scene.Mob).Target, Is.EqualTo(hull), "seated and operating, the eye is on the vehicle"));

        await Server.WaitPost(() => Assert.That(vehicleSystem.TryRemoveOperator(SEntity<VehicleComponent>(hull)), Is.True));
        await Pair.RunTicksSync(SettleTicks);
        await Server.WaitAssertion(() =>
            Assert.That(SComp<EyeComponent>(scene.Mob).Target, Is.EqualTo(hull),
                "no longer operating but still seated, the eye should stay on the vehicle"));
        await Client.WaitAssertion(() =>
            Assert.That(CComp<EyeComponent>(ToClientUid(scene.Mob)).Target, Is.EqualTo(ToClientUid(hull))));

        await Server.WaitPost(() => Unbuckle(scene.Mob));
        await Pair.RunTicksSync(SettleTicks);
        await Server.WaitAssertion(() =>
            Assert.That(SComp<EyeComponent>(scene.Mob).Target, Is.Null, "standing, the eye should be back on the occupant"));
    }

    /// <param name="hadOneAlready">The occupant already leans toward the cursor before sitting, and keeps their own.</param>
    [TestCase(false)]
    [TestCase(true)]
    public async Task CursorLean(bool hadOneAlready)
    {
        var scene = await SetUpSeatsAsync();
        var seatOffset = scene.ViewSeat.Comp.CursorOffset;
        Assert.That(seatOffset, Is.Not.Null, "precondition: the seat leans");
        var ownOffset = seatOffset!.Value + 3f;
        var serverType = SEntMan.ComponentFactory.GetRegistration("EyeCursorOffset").Type;
        var clientType = CEntMan.ComponentFactory.GetRegistration("EyeCursorOffset").Type;

        if (hadOneAlready)
        {
            await Server.WaitPost(() =>
            {
                var own = (SharedEyeCursorOffsetComponent)SEntMan.ComponentFactory.GetComponent(serverType);
                own.MaxOffset = ownOffset;
                SEntMan.AddComponent(scene.Mob, own);
            });
        }

        await Server.WaitPost(() => Buckle(scene.Mob, scene.ViewSeat));
        await Pair.RunTicksSync(SettleTicks);

        var expected = hadOneAlready ? ownOffset : seatOffset.Value;
        await Server.WaitAssertion(() =>
            Assert.That(((SharedEyeCursorOffsetComponent)SEntMan.GetComponent(scene.Mob, serverType)).MaxOffset, Is.EqualTo(expected),
                "seated, the view should lean by the seat's offset, or keep their own"));
        // An offset of their own is the server's alone: EyeCursorOffset has no state to carry its values.
        if (!hadOneAlready)
        {
            await Client.WaitAssertion(() =>
                Assert.That(((SharedEyeCursorOffsetComponent)CEntMan.GetComponent(ToClientUid(scene.Mob), clientType)).MaxOffset,
                    Is.EqualTo(expected), "on the client too, which is where it leans"));
        }

        await Server.WaitPost(() => Unbuckle(scene.Mob));
        await Pair.RunTicksSync(SettleTicks);

        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.HasComponent(scene.Mob, serverType), Is.EqualTo(hadOneAlready),
                "standing takes away the lean the seat gave, and only that one"));
        await Client.WaitAssertion(() =>
            Assert.That(CEntMan.HasComponent(ToClientUid(scene.Mob), clientType), Is.EqualTo(hadOneAlready)));
    }

    [Test]
    public async Task PassengerSeatIsNotAView()
    {
        var scene = await SetUpSeatsAsync();
        var cursorOffsetType = SEntMan.ComponentFactory.GetRegistration("EyeCursorOffset").Type;

        await Server.WaitPost(() => Buckle(scene.Mob, scene.PassengerSeat));
        await Pair.RunTicksSync(SettleTicks);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SComp<BuckleComponent>(scene.Mob).BuckledTo, Is.EqualTo(scene.PassengerSeat), "precondition: seated");
            Assert.That(SEntMan.HasComponent<KsCrewVehicleViewerComponent>(scene.Mob), Is.False);
            Assert.That(SComp<EyeComponent>(scene.Mob).Target, Is.Null, "a passenger seat should leave the eye alone");
            Assert.That(SComp<ContentEyeComponent>(scene.Mob).TargetZoom, Is.EqualTo(ZoomBefore));
            Assert.That(SEntMan.HasComponent(scene.Mob, cursorOffsetType), Is.False);
            Assert.That(ViewerPvsScale(), Is.EqualTo(1f));
        });
    }

    [Test]
    public async Task NarrowViewSeatIsRefused()
    {
        var testMap = await Pair.CreateTestMap();
        var errors = new List<string>();
        await Server.WaitPost(() =>
        {
            var hull = SEntMan.SpawnAtPosition(NarrowSeatHullPrototype, testMap.GridCoords);

            // The refusal logs an error, which would fail the test: silence the interior system's log for the call only.
            var log = Interiors.Log;
            var level = log.Level;
            log.Level = LogLevel.Fatal;
            try
            {
                Assert.That(Interiors.EnsureInterior(Vehicle(hull), out _, errors), Is.False,
                    "an interior whose view seat cannot cover its zoom should be refused");
            }
            finally
            {
                log.Level = level;
            }
        });

        Assert.That(errors.Any(error => error.Contains("narrow view seat") && error.Contains("pvsScale")), Is.True,
            $"the error should name the seat and what is wrong with it; got: {string.Join("; ", errors)}");
    }
}
