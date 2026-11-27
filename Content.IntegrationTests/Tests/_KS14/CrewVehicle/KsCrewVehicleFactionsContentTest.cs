#nullable enable
using System.Linq;
using System.Numerics;
using Content.Server._KS14.Vehicle.Factions;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Prototypes;
using Content.Shared.NPC.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.CrewVehicle;

/// <summary>
///     The humvee is on the side of whoever sits in its driver seat, and nobody's once they get up.
/// </summary>
[TestOf(typeof(KsVehicleOperatorFactionsSystem))]
public sealed class KsCrewVehicleFactionsContentTest : KsCrewVehicleDriveTestBase
{
    private static readonly EntProtoId Humvee = "KsCrewVehicleHumvee";
    private static readonly ProtoId<NpcFactionPrototype> NanoTrasenFaction = "NanoTrasen";

    private string[] Factions(EntityUid uid)
    {
        return SEntMan.TryGetComponent<NpcFactionMemberComponent>(uid, out var memberComponent)
            ? memberComponent.Factions.Select(faction => faction.Id).ToArray()
            : [];
    }

    [Test]
    public async Task TestHumveeTakesItsDriversSide()
    {
        var scene = await SpawnHullAsync(new Vector2(0f, 4f), Humvee);
        await Server.WaitPost(() =>
        {
            var npcFactionSystem = SEntMan.System<NpcFactionSystem>();
            npcFactionSystem.ClearFactions(SPlayer, dirty: false);
            npcFactionSystem.AddFaction(SPlayer, NanoTrasenFaction);
        });

        await Server.WaitAssertion(() => Assert.That(Factions(scene.Hull), Is.Empty, "precondition: nobody's, parked"));

        await PutInsideAsync(scene);
        await SitAsync(scene.DriverSeat);
        await Server.WaitAssertion(() =>
            Assert.That(Factions(scene.Hull), Is.EqualTo(new[] { NanoTrasenFaction.Id }), "the driver's side, from the driver seat"));

        await StandAsync();
        await Server.WaitAssertion(() => Assert.That(Factions(scene.Hull), Is.Empty, "nobody's once the driver gets up"));
    }
}
