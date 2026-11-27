#nullable enable
using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Shared._KS14.CrewVehicle.Engine;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using NUnit.Framework;
using Robust.Shared.Audio.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._KS14.CrewVehicle;

/// <summary>
///     A crew vehicle's engine: off at spawn, and the vehicle drives only once it is started and its start delay is over;
///         it idles audibly while it runs, inside and out; and while it is off and nobody drives, the vehicle can be
///         towed, slowly.
/// </summary>
[TestOf(typeof(KsCrewVehicleEngineSystem))]
public sealed class KsCrewVehicleEngineTest : KsCrewVehicleDriveTestBase
{
    private bool CanMove(EntityUid hull) => SComp<InputMoverComponent>(hull).CanMove;

    private async Task<HullScene> SeatedAsync()
    {
        var scene = await SpawnHullAsync(new Vector2(0f, 6f), EngineHullPrototype);
        await PutInsideAsync(scene);
        await SitAsync(scene.DriverSeat);
        return scene;
    }

    private async Task ToggleAsync(HullScene scene)
    {
        await Server.WaitPost(() =>
        {
            var action = SComp<KsCrewVehicleEngineComponent>(scene.Hull).ActionEntity!.Value;
            SEntMan.System<SharedActionsSystem>().PerformAction(SPlayer, SEntity<ActionComponent>(action));
        });
    }

    [Test]
    public async Task DrivesOnlyOnceStartedAndAfterItsDelay()
    {
        var scene = await SeatedAsync();
        await Server.WaitAssertion(() =>
            Assert.That(CanMove(scene.Hull), Is.False, "off at spawn, it can't be driven"));

        var delay = TimeSpan.Zero;
        await Server.WaitPost(() => delay = SComp<KsCrewVehicleEngineComponent>(scene.Hull).StartDelay);
        await ToggleAsync(scene);
        var startedAt = TimeSpan.Zero;
        await Server.WaitPost(() => startedAt = STiming.CurTime);
        var drivableAt = TimeSpan.Zero;
        for (var tick = 0; tick < TickRate * 3 && drivableAt == TimeSpan.Zero; tick++)
        {
            await RunTicks(1);
            await Server.WaitPost(() =>
            {
                if (CanMove(scene.Hull))
                    drivableAt = STiming.CurTime;
            });
        }

        Assert.That(drivableAt - startedAt, Is.GreaterThanOrEqualTo(delay - STiming.TickPeriod), "not before the start delay");
        Assert.That(drivableAt - startedAt, Is.LessThanOrEqualTo(delay + STiming.TickPeriod * 2), "and soon after it");

        await ToggleAsync(scene);
        await RunTicks(2);
        await Server.WaitAssertion(() => Assert.That(CanMove(scene.Hull), Is.False, "stopped, it can't be driven"));
    }

    [Test]
    public async Task KeepsRunningWhenTheDriverGetsUp()
    {
        var scene = await SeatedAsync();
        await ToggleAsync(scene);
        await RunTicks(TickRate);
        await StandAsync();
        await Server.WaitAssertion(() =>
            Assert.That(SComp<KsCrewVehicleEngineComponent>(scene.Hull).State, Is.EqualTo(KsCrewVehicleEngineState.Running)));
    }

    /// <summary>
    ///     The idle loop plays, at the engine's pitch, on the hull for those outside and everywhere inside for those in
    ///         it, only while it runs.
    /// </summary>
    [Test]
    public async Task IdlesWhileRunning()
    {
        var scene = await SeatedAsync();
        var clientHull = ToClient(scene.Hull);
        (int OnHull, int Elsewhere) IdleStreams()
        {
            const string idle = "vehicleengineidle";
            var onHull = 0;
            var elsewhere = 0;
            var query = CEntMan.AllEntityQueryEnumerator<AudioComponent, TransformComponent>();
            while (query.MoveNext(out _, out var audioComponent, out var transformComponent))
            {
                string fileName = audioComponent.FileName;
                var looping = audioComponent.Params.Loop;
                if (!fileName.Contains(idle) || !looping)
                    continue;

                if (transformComponent.ParentUid == clientHull)
                    onHull++;
                else
                    elsewhere++;
            }

            return (onHull, elsewhere);
        }

        var off = (0, 0);
        await Client.WaitPost(() => off = IdleStreams());
        await ToggleAsync(scene);
        await RunTicks(TickRate);
        var on = (0, 0);
        await Client.WaitPost(() => on = IdleStreams());
        Assert.Multiple(() =>
        {
            Assert.That(off, Is.EqualTo((0, 0)), "control: off, nothing idles");
            Assert.That(on.Item1 + on.Item2, Is.EqualTo(2), "running: one loop on the hull, and one inside for the seated driver");
        });
    }

    /// <summary>
    ///     Engine off and nobody driving, the vehicle can be towed, and it slows the one towing it as configured; with
    ///         the engine running, or someone at the wheel, it can't.
    /// </summary>
    [Test]
    public async Task CanBeTowedOnlyWhileOffAndNobodyDrives()
    {
        var scene = await SpawnHullAsync(new Vector2(0f, 6f), EngineHullPrototype);
        EntityUid tower = default;
        await Server.WaitPost(() => tower = SEntMan.SpawnAtPosition(Human, OriginCoordinates.Offset(new Vector2(0f, 4.4f))));
        await RunTicks(5);
        var pulling = SEntMan.System<PullingSystem>();
        bool TryTow() => pulling.TryStartPull(tower, scene.Hull);
        void StopTow()
        {
            if (SComp<PullableComponent>(scene.Hull).BeingPulled)
                pulling.TryStopPull(scene.Hull, SComp<PullableComponent>(scene.Hull));
        }

        var baseSpeed = 0f;
        await Server.WaitAssertion(() =>
        {
            baseSpeed = SComp<MovementSpeedModifierComponent>(tower).CurrentSprintSpeed;
            Assert.That(TryTow(), Is.True, "off, nobody driving: it can be towed");
        });
        await RunTicks(2);
        await Server.WaitAssertion(() =>
        {
            var modifier = SComp<Content.Shared.Item.HeldSpeedModifierComponent>(scene.Hull).SprintModifier;
            Assert.That(SComp<MovementSpeedModifierComponent>(tower).CurrentSprintSpeed, Is.EqualTo(baseSpeed * modifier).Within(0.01f),
                "and slows the one towing it as configured");
        });

        // A driver sitting down ends the tow, and while they sit it can't be towed.
        await PutInsideAsync(scene);
        await SitAsync(scene.DriverSeat);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SComp<PullableComponent>(scene.Hull).BeingPulled, Is.False, "a driver at the wheel ends the tow");
            Assert.That(TryTow(), Is.False, "and it can't be towed while they are");
            StopTow();
        });

        // Running, with nobody at the wheel: still not.
        await ToggleAsync(scene);
        await RunTicks(TickRate);
        await StandAsync();
        await Server.WaitAssertion(() =>
        {
            Assert.That(SComp<KsCrewVehicleEngineComponent>(scene.Hull).State, Is.EqualTo(KsCrewVehicleEngineState.Running), "precondition");
            Assert.That(TryTow(), Is.False, "running: it can't be towed");
            StopTow();
        });
    }}
