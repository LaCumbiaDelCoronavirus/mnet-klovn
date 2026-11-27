#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Shared._KS14.CrewVehicle.View;
using Content.Shared._KS14.PopupLocalization;
using Content.Shared.Popups;
using Content.Shared.Effects;
using Content.IntegrationTests.Fixtures.Attributes;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Maths;
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

    /// <summary>Any locale id will do; the test looks at where the popup goes, not what it says.</summary>
    private const string PopupText = "ks-crew-vehicle-interior-no-build";

    private sealed record SeatScene(VehicleScene Vehicle, EntityUid Mob, Entity<KsCrewVehicleViewSeatComponent> ViewSeat, EntityUid PassengerSeat);

    /// <summary>
    ///     The player's mob, spawned inside between the two seats, with its zoom set to <see cref="ZoomBefore"/>.
    /// </summary>
    /// <param name="levelBelow">The vehicle is parked on a z-level with another under it.</param>
    private async Task<SeatScene> SetUpSeatsAsync(bool levelBelow = false)
    {
        var vehicle = await SetUpVehicleAsync(levelBelow: levelBelow);
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

    /// <summary>
    ///     From a view seat the player sees outside, so they are sent popups from outside, however far inside the
    ///         interior their body is.
    /// </summary>
    [Test]
    public async Task PopupsOutsideReachTheViewSeat()
    {
        var scene = await SetUpSeatsAsync();
        await Server.WaitPost(() => Buckle(scene.Mob, scene.ViewSeat));
        await Pair.RunTicksSync(SettleTicks);

        var near = await SpawnMarkerAsync(scene.Vehicle, 2f);
        var far = await SpawnMarkerAsync(scene.Vehicle, FarDistance * scene.Vehicle.PvsRange);
        await Pair.RunTicksSync(SettleTicks);
        await Server.WaitPost(() =>
        {
            var popupSystem = SEntMan.System<SharedPopupSystem>();
            popupSystem.PopupEntity(KsPopupMessage.Create(PopupText), SEntMan.GetEntity(near));
            popupSystem.PopupEntity(KsPopupMessage.Create(PopupText), SEntMan.GetEntity(far));
        });
        await Pair.RunTicksSync(5);

        // A label is placed where its entity stood, relative to what it stood on: found by position, not by uid.
        var nearPosition = Vector2.Zero;
        await Server.WaitPost(() => nearPosition = SEntMan.System<SharedTransformSystem>().GetWorldPosition(SEntMan.GetEntity(near)));
        await Client.WaitAssertion(() =>
        {
            var transformSystem = CEntMan.System<SharedTransformSystem>();
            var labelPositions = CEntMan.System<Content.Client.Popups.PopupSystem>().WorldLabels
                .Select(label => transformSystem.ToMapCoordinates(label.InitialPos).Position)
                .ToList();
            Assert.That(labelPositions.Count(position => (position - nearPosition).Length() < 0.5f), Is.EqualTo(1),
                "a popup on something beside the vehicle should reach the player looking out of it");
            Assert.That(labelPositions.Count(position => (position - nearPosition).Length() > FarDistance * scene.Vehicle.PvsRange / 2f), Is.Zero,
                "one far from the vehicle and the interior should not");
        });
    }

    /// <param name="seated">In the view seat; otherwise standing inside, seeing the outside behind the interior.</param>
    [TestCase(true)]
    [TestCase(false)]
    public async Task TheLevelBelowReachesTheCrew(bool seated)
    {
        var scene = await SetUpSeatsAsync(levelBelow: true);
        if (seated)
        {
            await Server.WaitPost(() => Buckle(scene.Mob, scene.ViewSeat));
            await Pair.RunTicksSync(SettleTicks);
        }

        var under = NetEntity.Invalid;
        var far = NetEntity.Invalid;
        await Server.WaitPost(() =>
        {
            var hullPosition = SEntMan.System<SharedTransformSystem>().GetWorldPosition(scene.Vehicle.Hull);
            under = SEntMan.GetNetEntity(SEntMan.SpawnEntity(null, new MapCoordinates(hullPosition, scene.Vehicle.LowerMapId)));
            far = SEntMan.GetNetEntity(SEntMan.SpawnEntity(null,
                new MapCoordinates(hullPosition + new Vector2(FarDistance * scene.Vehicle.PvsRange, 0f), scene.Vehicle.LowerMapId)));
        });
        await Pair.RunTicksSync(SettleTicks);

        Assert.That(await ClientHasAsync(under), Is.True,
            "the level under the vehicle should be sent to the crew, as to a mob standing where it is");
        Assert.That(await ClientHasAsync(far), Is.False, "but only around it");
    }

    [TestPrototypes]
    private const string FlashPrototypes = @"
- type: entity
  id: KsCrewVehicleViewTestFlashTarget
  components:
  - type: Sprite
";

    private readonly EntProtoId _flashTarget = "KsCrewVehicleViewTestFlashTarget";

    /// <summary>
    ///     Long enough for a network event to be dispatched: the client holds one back until it has applied the state of
    ///         the tick it was sent on.
    /// </summary>
    private const int FlashTicks = 10;

    /// <summary>
    ///     A hit's colour flash reaches the player looking out of a view seat, as popups do. Two controls must not get
    ///         one: something that has left their view, and the same player once they look from somewhere else.
    /// </summary>
    [Test]
    public async Task HitFlashesOutsideReachTheViewSeat()
    {
        var scene = await SetUpSeatsAsync();
        await Server.WaitPost(() => Buckle(scene.Mob, scene.ViewSeat));
        await Pair.RunTicksSync(SettleTicks);

        // What the client is sent, rather than what it then animates: that is the server's choice under test.
        var flashed = new HashSet<NetEntity>();
        EventHandler<object> capture = (_, message) =>
        {
            if (message is ColorFlashEffectEvent flashEvent)
                flashed.UnionWith(flashEvent.Entities);
        };
        await Client.WaitPost(() => CEntMan.EntityNetManager.ReceivedSystemMessage += capture);
        try
        {
            // Each flash on a target of its own, so that one arriving late cannot pass for another.
            EntityUid near = default, gone = default, nearLater = default;
            await Server.WaitPost(() =>
            {
                var hullPosition = SEntMan.System<SharedTransformSystem>().GetWorldPosition(scene.Vehicle.Hull);
                near = SEntMan.SpawnEntity(_flashTarget, new MapCoordinates(hullPosition + new Vector2(2f, 0f), scene.Vehicle.OutsideMapId));
                gone = SEntMan.SpawnEntity(_flashTarget, new MapCoordinates(hullPosition + new Vector2(-2f, 0f), scene.Vehicle.OutsideMapId));
                nearLater = SEntMan.SpawnEntity(_flashTarget, new MapCoordinates(hullPosition + new Vector2(0f, 2f), scene.Vehicle.OutsideMapId));
            });
            await Pair.RunTicksSync(SettleTicks);
            Assert.That(await ClientHasAsync(SEntMan.GetNetEntity(gone)), Is.True, "precondition: the client has been sent the control");
            Assert.That(await ClientHasAsync(SEntMan.GetNetEntity(nearLater)), Is.True, "precondition: and the later target");

            // The control leaves their view; the client keeps it, detached, sprite and all.
            await Server.WaitPost(() => SEntMan.System<SharedTransformSystem>().SetWorldPosition(gone,
                SEntMan.System<SharedTransformSystem>().GetWorldPosition(scene.Vehicle.Hull) + new Vector2(FarDistance * scene.Vehicle.PvsRange, 0f)));
            await Pair.RunTicksSync(SettleTicks);

            await Server.WaitPost(() =>
            {
                RaiseHitFlash(near);
                RaiseHitFlash(gone);
            });
            await Pair.RunTicksSync(FlashTicks);

            Assert.That(flashed, Does.Contain(SEntMan.GetNetEntity(near)), "a hit's flash beside the vehicle should reach the player looking out of it");
            Assert.That(flashed, Does.Not.Contain(SEntMan.GetNetEntity(gone)), "one on something they no longer see should not");

            // The control player: the same session, standing up and then looking from a mob on a map of its own, far
            //      from the vehicle. Standing first: the engine drops an eye target's view subscription on detach only
            //      while the eye's owner still has its actor, which the detach has already removed.
            await Server.WaitPost(() =>
            {
                Unbuckle(scene.Mob);
                SEntMan.System<SharedMapSystem>().CreateMap(out var elsewhereMapId);
                var elsewhereMob = SEntMan.SpawnEntity(MobPrototype, new MapCoordinates(Vector2.Zero, elsewhereMapId));
                Server.PlayerMan.SetAttachedEntity(ServerSession!, elsewhereMob);
            });
            await Pair.RunTicksSync(SettleTicks);
            await Server.WaitAssertion(() =>
            {
                Assert.That(ServerSession!.ViewSubscriptions.Where(uid => SComp<TransformComponent>(uid).MapID == scene.Vehicle.OutsideMapId),
                    Is.Empty, "precondition: the player no longer sees the vehicle's map from anywhere");
            });

            await Server.WaitPost(() => RaiseHitFlash(nearLater));
            await Pair.RunTicksSync(FlashTicks);

            Assert.That(flashed, Does.Not.Contain(SEntMan.GetNetEntity(nearLater)),
                "a player who no longer looks out of the vehicle should not get its flashes");
        }
        finally
        {
            await Client.WaitPost(() => CEntMan.EntityNetManager.ReceivedSystemMessage -= capture);
        }
    }

    /// <summary>As a weapon raises it: to everyone whose body is in PVS of what was hit. Call on the server thread.</summary>
    private void RaiseHitFlash(EntityUid targetUid)
    {
        SEntMan.System<SharedColorFlashEffectSystem>()
            .RaiseEffect(Color.Red, new List<EntityUid> { targetUid }, Filter.Pvs(targetUid, entityManager: SEntMan));
    }
}
