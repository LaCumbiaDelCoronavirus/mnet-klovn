#nullable enable
using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.Perception;
using Content.Server.NPC;
using Content.Server.NPC.Queries;
using Content.Server.NPC.Systems;
using Content.Shared._KS14.NPC;
using Content.Shared.Vehicle.Components;
using Content.Shared.Vehicle.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;
using static Content.IntegrationTests.Tests._KS14.Vehicle.KsNpcVehicleTestPrototypes;

namespace Content.IntegrationTests.Tests._KS14.Vehicle;

/// <summary>
///     Melee NPCs go for a hostile driven vehicle: a hull has no mob state, which the melee target queries' alive check
///         alone never lets in.
/// </summary>
public sealed class KsNpcVehicleMeleeTargetTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    // The private module's operative melee query: run only where it is present.
    private readonly ProtoId<UtilityQueryPrototype> _operativeMeleeTargets = "KsOperativeNearbyMeleeTargets";

    private async Task<(EntityUid Owner, EntityUid Hull, EntityUid Crate, EntityUid Mob)> SetUpScene()
    {
        var map = await Pair.CreateTestMap();
        EntityUid ownerUid = default, hullUid = default, crateUid = default, mobUid = default;

        await Server.WaitPost(() =>
        {
            var gridUid = MakeGrid(SEntMan, Server.ResolveDependency<ITileDefinitionManager>(), map.MapId, map.Grid,
                new Vector2i(-8, -8), new Vector2i(8, 8)).Owner;
            ownerUid = SpawnAt(SEntMan, LonerMob, gridUid, 0, 0);
            hullUid = SpawnAt(SEntMan, FactionVehicle, gridUid, 4, 0);
            crateUid = SpawnAt(SEntMan, Crate, gridUid, 0, 4);
            mobUid = SpawnAt(SEntMan, NanoTrasenMob, gridUid, -4, 0);

            var driverUid = SEntMan.SpawnEntity(DriverNanoTrasen, MapCoordinates.Nullspace);
            Assert.That(SEntMan.System<VehicleSystem>().TrySetOperator((hullUid, SEntMan.GetComponent<VehicleComponent>(hullUid)), driverUid),
                Is.True, "precondition: driven by a hostile");
        });

        await Pair.RunTicksSync(5);
        return (ownerUid, hullUid, crateUid, mobUid);
    }

    /// <summary>
    ///     Content: the operative melee query scores a hostile driven hull above nothing, as it does a hostile mob, and
    ///         still turns away a body that is neither a mob nor a vehicle.
    /// </summary>
    [Test]
    public async Task TestOperativeMeleeQueryTakesAHostileHull()
    {
        if (!SProtoMan.HasIndex(_operativeMeleeTargets))
            Assert.Ignore("the operative HTN is in the private _KsModule submodule, which is not present");

        var (ownerUid, hullUid, crateUid, mobUid) = await SetUpScene();

        await Server.WaitAssertion(() =>
        {
            var perceptionSystem = SEntMan.System<NpcPerceptionSystem>();
            var now = Server.ResolveDependency<IGameTiming>().CurTime;

            foreach (var targetUid in new[] { hullUid, crateUid, mobUid })
            {
                perceptionSystem.SetContact(ownerUid, targetUid, new NpcContact(NpcContactState.Visible,
                    now, now, now, SEntMan.GetComponent<TransformComponent>(targetUid).Coordinates, default, null, Reacted: true));
            }

            var blackboard = new NPCBlackboard();
            blackboard.SetValue(NPCBlackboard.Owner, ownerUid);
            var scores = SEntMan.System<NPCUtilitySystem>().GetEntities(blackboard, _operativeMeleeTargets, bestOnly: false).Entities;

            Assert.That(scores.GetValueOrDefault(mobUid), Is.GreaterThan(0f), "precondition: a hostile mob is a target");
            Assert.That(scores.GetValueOrDefault(hullUid), Is.GreaterThan(0f), "a hostile driven hull should be one too");
            Assert.That(scores.GetValueOrDefault(crateUid), Is.Zero, "but a body that is neither a mob nor a vehicle is not");
        });
    }
}
