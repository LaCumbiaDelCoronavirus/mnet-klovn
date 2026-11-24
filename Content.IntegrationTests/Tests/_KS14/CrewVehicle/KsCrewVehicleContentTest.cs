#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.CrewVehicle.Interior;
using Content.Shared._KS14.CrewVehicle.Interior;
using Content.Shared.Damage.Components;
using Content.Shared.Guidebook;
using Content.Shared.Mobs.Components;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.CrewVehicle;

/// <summary>
///     The crew vehicles that ship: every one of them loads its interior, nothing inside one can be damaged, and the
///         guidebook can be found.
/// </summary>
[TestFixture]
[TestOf(typeof(KsCrewVehicleInteriorSystem))]
public sealed class KsCrewVehicleContentTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false, Dirty = true };

    private static readonly ProtoId<GuideEntryPrototype> SecurityGuide = "Security";
    private static readonly ProtoId<GuideEntryPrototype> HumveeGuide = "KsHumvee";

    private KsCrewVehicleInteriorSystem Interiors => SEntMan.System<KsCrewVehicleInteriorSystem>();

    /// <summary>
    ///     Every crew vehicle prototype that ships, test ones left out: they include interiors meant to be refused.
    /// </summary>
    private List<EntProtoId> ShippedVehicles()
    {
        var vehicles = new List<EntProtoId>();
        foreach (var prototype in SProtoMan.EnumeratePrototypes<EntityPrototype>())
        {
            if (!prototype.Abstract &&
                !Pair.IsTestPrototype(prototype) &&
                prototype.TryComp<KsCrewVehicleInteriorComponent>(out _, SEntMan.ComponentFactory))
                vehicles.Add(prototype.ID);
        }

        Assert.That(vehicles, Is.Not.Empty, "precondition: some crew vehicle ships");
        return vehicles;
    }

    /// <summary>
    ///     Spawns <paramref name="vehicle"/> on the test map and loads its interior, failing with why if it is refused.
    /// </summary>
    private (EntityUid Hull, EntityUid InteriorMap) SpawnWithInterior(TestMapData testMap, EntProtoId vehicle)
    {
        var hullUid = SEntMan.SpawnAtPosition(vehicle, new EntityCoordinates(testMap.Grid, new Vector2(0.5f, 0.5f)));
        var errors = new List<string>();
        Assert.That(Interiors.EnsureInterior((hullUid, SEntMan.GetComponent<KsCrewVehicleInteriorComponent>(hullUid)), out var interiorMap, errors),
            Is.True, $"{vehicle}'s interior should load: {string.Join("; ", errors)}");
        return (hullUid, interiorMap!.Value);
    }

    /// <summary>
    ///     Each one loads its interior, and deleting the vehicle takes the interior with it.
    /// </summary>
    [Test]
    public async Task EveryCrewVehicleLoadsItsInterior()
    {
        var testMap = await Pair.CreateTestMap();
        var interiors = new Dictionary<EntProtoId, EntityUid>();
        var mapsBefore = 0;
        await Server.WaitPost(() =>
        {
            mapsBefore = SEntMan.AllEntities<MapComponent>().Count();
            foreach (var vehicle in ShippedVehicles())
            {
                var (hullUid, interiorMap) = SpawnWithInterior(testMap, vehicle);
                interiors[vehicle] = interiorMap;
                SEntMan.DeleteEntity(hullUid);
            }
        });

        await RunTicksSync(5);

        await Server.WaitAssertion(() =>
        {
            foreach (var (vehicle, interiorMap) in interiors)
            {
                Assert.That(SEntMan.EntityExists(interiorMap), Is.False, $"{vehicle}'s interior should go with it");
            }

            Assert.That(SEntMan.AllEntities<MapComponent>().Count(), Is.EqualTo(mapsBefore), "no interior map is left behind");
        });
    }

    /// <summary>
    ///     Nothing placed in an interior can be damaged, the people in it aside: that is all that keeps an interior from
    ///         being taken apart.
    /// </summary>
    [Test]
    public async Task ShippedInteriorsAreInert()
    {
        var testMap = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            foreach (var vehicle in ShippedVehicles())
            {
                var (hullUid, interiorMap) = SpawnWithInterior(testMap, vehicle);
                var placed = 0;
                var query = SEntMan.AllEntityQueryEnumerator<TransformComponent>();
                while (query.MoveNext(out var uid, out var transformComponent))
                {
                    if (transformComponent.MapUid != interiorMap || uid == interiorMap)
                        continue;

                    placed++;
                    if (SEntMan.HasComponent<MobStateComponent>(uid))
                        continue;

                    Assert.That(SEntMan.HasComponent<DamageableComponent>(uid), Is.False,
                        $"{SEntMan.ToPrettyString(uid)} in {vehicle}'s interior can be damaged");
                }

                Assert.That(placed, Is.GreaterThan(1), $"precondition: {vehicle}'s interior has things in it");
                SEntMan.DeleteEntity(hullUid);
            }
        });
    }

    /// <summary>
    ///     The humvee's page hangs under upstream's Security entry, through a partial prototype that must leave upstream's
    ///         own children where they are.
    /// </summary>
    [Test]
    public async Task HumveeGuideIsUnderSecurity()
    {
        await Server.WaitAssertion(() =>
        {
            var children = SProtoMan.Index(SecurityGuide).Children;
            Assert.That(children, Does.Contain(HumveeGuide));
            Assert.That(children, Has.Count.GreaterThan(1), "upstream's own Security pages are still there");
        });
    }
}
