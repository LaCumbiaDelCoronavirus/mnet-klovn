#nullable enable
using System.Numerics;
using System.Threading.Tasks;
using Content.Shared._KS14.CrewVehicle.Interior;
using NUnit.Framework;
using Robust.Shared.GameObjects;
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

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: KsCrewVehicleDriveTestHull
  id: KsCrewVehicleBackdropTestHullSealed
  components:
  - type: KsCrewVehicleInterior
    outsideBackdrop: false
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
}
