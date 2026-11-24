#nullable enable
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server._KS14.CrewVehicle.Interior;
using Content.Shared._KS14.CrewVehicle.Interior;
using Content.Shared._KS14.RCD;
using Content.Shared.Tiles;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.CrewVehicle;

/// <summary>
///     Nothing is built inside a crew vehicle: each case is run inside an interior, and again outside as the control,
///         where the same action has to work.
/// </summary>
[TestOf(typeof(KsCrewVehicleNoBuildSystem))]
public sealed class KsCrewVehicleNoBuildTest : InteractionTest
{
    private const string Wall = "Wall";
    private const string Girder = "Girder";
    private const string Anchorable = "KsCrewVehicleNoBuildTestAnchorable";
    private readonly EntProtoId Hull = "KsCrewVehicleNoBuildTestHull";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsCrewVehicleNoBuildTestHull
  components:
  - type: KsCrewVehicleInterior
    interiorPath: /Maps/Test/_KS14/CrewVehicle/basic.yml
    doorways:
      left:
        offset: -1,0
      right:
        offset: 1,0

- type: entity
  id: KsCrewVehicleNoBuildTestAnchorable
  components:
  - type: Physics
  - type: Anchorable
    delay: 0
";

    private EntityUid _interiorMap;
    private EntityUid _interiorGrid;

    /// <summary>
    ///     Loads a vehicle's interior; with <paramref name="inside"/>, puts the player and the target tile in it, on the
    ///         interior floor (which covers tiles (0, 0) to (4, 5)).
    /// </summary>
    private async Task PlaceAsync(bool inside)
    {
        await Server.WaitPost(() =>
        {
            var hull = SEntMan.Spawn(Hull, new MapCoordinates(new Vector2(10f, 10f), MapId));
            var vehicle = new Entity<KsCrewVehicleInteriorComponent>(hull, SEntMan.GetComponent<KsCrewVehicleInteriorComponent>(hull));
            Assert.That(SEntMan.System<KsCrewVehicleInteriorSystem>().EnsureInterior(vehicle, out var interiorMap), Is.True);
            _interiorMap = interiorMap!.Value;
            _interiorGrid = SEntMan.AllEntities<MapGridComponent>()
                .Single(grid => SEntMan.GetComponent<TransformComponent>(grid).MapUid == _interiorMap)
                .Owner;

            if (!inside)
                return;

            Transform.SetCoordinates(SPlayer, new EntityCoordinates(_interiorGrid, new Vector2(1.5f, 1.5f)));
            TargetCoords = SEntMan.GetNetCoordinates(new EntityCoordinates(_interiorGrid, new Vector2(2.5f, 1.5f)));
        });

        await RunTicks(5);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task ConstructingAWall(bool inside)
    {
        await PlaceAsync(inside);

        await StartConstruction(Wall);
        await InteractUsing(Steel, 2);

        if (!inside)
        {
            Assert.That(HandSys.GetActiveItem((SPlayer, Hands)), Is.Null, "the control: the steel went into the girder");
            ClientAssertPrototype(Girder, Target);
            return;
        }

        await Server.WaitAssertion(() =>
        {
            Assert.That(HandSys.GetActiveItem((SPlayer, Hands)), Is.Not.Null, "the steel was not used");
            var built = SEntMan.AllEntities<MetaDataComponent>()
                .Where(entity => SEntMan.GetComponent<TransformComponent>(entity).MapUid == _interiorMap)
                .Count(entity => entity.Comp.EntityPrototype?.ID == Girder);
            Assert.That(built, Is.Zero, "no girder was built inside");
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task AnchoringWithAWrench(bool inside)
    {
        await PlaceAsync(inside);

        await SpawnTarget(Anchorable);
        AssertAnchored(false);
        await InteractUsing(Wrench);
        AssertAnchored(!inside);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task PlacingAFloorTile(bool inside)
    {
        await PlaceAsync(inside);

        await Server.WaitAssertion(() =>
        {
            // An empty tile beside the interior's floor, or beside the outside grid's.
            var gridUid = inside ? _interiorGrid : MapData.Grid.Owner;
            var gridComponent = SEntMan.GetComponent<MapGridComponent>(gridUid);
            var allowed = SEntMan.System<FloorTileSystem>().CanPlaceTile(gridUid, gridComponent, new Vector2i(6, 1), out _);
            Assert.That(allowed, Is.EqualTo(!inside));
        });
    }

    [Test]
    public async Task RcdsAreBlockedInside()
    {
        await PlaceAsync(inside: true);

        await Server.WaitAssertion(() =>
        {
            var noBuildZoneSystem = SEntMan.System<KsNoBuildZoneSystem>();
            Assert.That(noBuildZoneSystem.IsBlocked(SEntMan.GetComponent<MapComponent>(_interiorMap).MapId, out var message), Is.True);
            Assert.That(message, Is.EqualTo(KsCrewVehicleNoBuildSystem.NoBuildMessage));
            Assert.That(noBuildZoneSystem.IsBlocked(MapId, out _), Is.False, "the control: the outside map is not blocked");
        });
    }
}
