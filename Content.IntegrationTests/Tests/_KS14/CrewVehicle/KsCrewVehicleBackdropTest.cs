#nullable enable
using System.Numerics;
using System.Threading.Tasks;
using Content.Client._KS14.CrewVehicle.Interior;
using Content.Shared._KS14.CrewVehicle;
using Content.Shared._KS14.CrewVehicle.Interior;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.CrewVehicle;

/// <summary>
///     An interior that shows no outside (<see cref="KsCrewVehicleInteriorComponent.OutsideBackdrop"/>): its occupants'
///         client draws no backdrop behind it; an ordinary one does.
/// </summary>
[TestOf(typeof(SharedKsCrewVehicleInteriorSystem))]
public sealed class KsCrewVehicleBackdropTest : KsCrewVehicleDriveTestBase
{
    private readonly EntProtoId _sealedHull = "KsCrewVehicleBackdropTestHullSealed";

    /// <summary>
    ///     The same interior, laid out as a cabin whose front is grid east.
    /// </summary>
    private readonly EntProtoId _eastCabinHull = "KsCrewVehicleBackdropTestHullEastCabin";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: KsCrewVehicleDriveTestHull
  id: KsCrewVehicleBackdropTestHullSealed
  components:
  - type: KsCrewVehicleInterior
    outsideBackdrop: false

- type: entity
  parent: KsCrewVehicleDriveTestHull
  id: KsCrewVehicleBackdropTestHullEastCabin
  components:
  - type: KsCrewVehicleInterior
    cabinFront: East
";

    /// <summary>
    ///     Whether the player's client, standing inside, would draw the world outside behind the interior.
    /// </summary>
    private async Task<bool> ClientDrawsBackdropAsync()
    {
        var draws = true;
        await Client.WaitAssertion(() =>
        {
            var mapUid = CEntMan.GetComponent<TransformComponent>(CPlayer).MapUid;
            Assert.That(CEntMan.TryGetComponent<KsCrewVehicleInteriorMapComponent>(mapUid, out var interiorMapComponent),
                "precondition: the client knows it is on an interior map");
            draws = CEntMan.System<SharedKsCrewVehicleInteriorSystem>().DrawsOutsideBackdrop((mapUid!.Value, interiorMapComponent!));
        });
        return draws;
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task UnseatedSeeNoOutsideWhenOff(bool sealedHull)
    {
        var scene = await SpawnHullAsync(Vector2.Zero, sealedHull ? _sealedHull : HullPrototype);
        await PutInsideAsync(scene);
        await RunTicks(5);

        Assert.That(await ClientDrawsBackdropAsync(), Is.EqualTo(!sealedHull),
            sealedHull ? "a sealed interior shows its occupants no outside" : "an ordinary one does");
    }

    /// <summary>
    ///     The backdrop is drawn through the frame the client works out for the interior: the cabin's front, grid north or
    ///         grid east as it is laid out, is over the ground ahead of the hull, and its left over the hull's left. The
    ///         hull is turned off any quarter, so a frame wrong by any turn shows.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task BackdropFrameFollowsTheCabinsFront(bool eastCabin)
    {
        var scene = await SpawnHullAsync(Vector2.Zero, eastCabin ? _eastCabinHull : HullPrototype);
        await Server.WaitPost(() => Transform.SetWorldRotation(scene.Hull, Angle.FromDegrees(30d)));
        await PutInsideAsync(scene);
        await RunTicks(5);

        await Client.WaitAssertion(() =>
        {
            var mapId = CEntMan.GetComponent<TransformComponent>(CPlayer).MapID;
            Assert.That(CEntMan.System<KsCrewVehicleInteriorSystem>().TryGetBackdropFrame(mapId,
                    out _,
                    out _,
                    out _,
                    out var anchor,
                    out var interiorRotation,
                    out var hullPosition,
                    out var hullRotation),
                Is.True,
                "precondition: the client has a backdrop frame for the interior");

            var front = eastCabin ? new Vector2(1f, 0f) : new Vector2(0f, 1f);
            var left = eastCabin ? new Vector2(0f, 1f) : new Vector2(-1f, 0f);
            var forward = KsCrewVehicleHullFrame.Forward(hullRotation);
            var hullLeft = new Vector2(-forward.Y, forward.X);

            var ahead = KsCrewVehicleInteriorTransform.InteriorToWorld(anchor + front, anchor, hullPosition, hullRotation,
                interiorRotation: interiorRotation);
            var beside = KsCrewVehicleInteriorTransform.InteriorToWorld(anchor + left, anchor, hullPosition, hullRotation,
                interiorRotation: interiorRotation);
            Assert.That(Vector2.Distance(ahead, hullPosition + forward), Is.LessThan(1e-3f),
                $"the cabin's front, {front}, is over the ground ahead of the hull, not {ahead - hullPosition}");
            Assert.That(Vector2.Distance(beside, hullPosition + hullLeft), Is.LessThan(1e-3f),
                $"the cabin's left, {left}, is over the hull's left, not {beside - hullPosition}");
        });
    }
}
