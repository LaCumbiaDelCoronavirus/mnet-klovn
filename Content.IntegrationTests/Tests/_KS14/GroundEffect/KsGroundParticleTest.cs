#nullable enable
using System.Numerics;
using Content.Client._KS14.GroundEffect;
using Content.Client._KS14.Particles;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.GroundEffect;
using Content.Shared.Maps;
using NUnit.Framework;
using Robust.Shared;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.GroundEffect;

/// <summary>
///     <see cref="KsGroundEffectSystem"/> on client-side test hulls moved frame by frame over a strip of snow: what
///         emits, how much, when it stops, what it cleans up, and that it allocates nothing once running. The frames are
///         run by hand, so the counts follow from the snow effect's numbers and nothing else.
/// </summary>
[TestFixture]
[TestOf(typeof(KsGroundEffectSystem))]
public sealed class KsGroundParticleTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private const float Dt = 1f / 60f;

    /// <summary>
    ///     Snow covers x in [0, StripLength) and rows 0 to <see cref="StripRows"/>; plating either side.
    /// </summary>
    private const int StripLength = 20;

    private const int StripRows = 16;

    private readonly EntProtoId _hull = "KsGroundParticleTestHull";
    private readonly EntProtoId _quadHull = "KsGroundParticleTestQuadHull";
    // Tiles are registered once at startup, so a test can't bring its own: the strip is real snow, and every number
    //      below is read from the effect the snow tile names.
    private static readonly ProtoId<ContentTileDefinition> Snow = "FloorSnow";
    private static readonly ProtoId<ContentTileDefinition> Plating = "Plating";
    private static readonly ProtoId<KsGroundEffectPrototype> SnowEffect = "KsGroundEffectSnow";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsGroundParticleTestHull
  categories: [ HideSpawnMenu ]
  components:
  - type: KsGroundEffectEmitter

- type: entity
  id: KsGroundParticleTestQuadHull
  categories: [ HideSpawnMenu ]
  components:
  - type: KsGroundEffectEmitter
    contactPoints:
    - -0.5,-0.5
    - 0.5,-0.5
    - -0.5,0.5
    - 0.5,0.5
";

    private KsGroundEffectPrototype SnowEffectPrototype => CProtoMan.Index(CProtoMan.Index(Snow).KsGroundEffect!.Value);

    private KsGroundEffectSystem GroundSystem => CEntMan.System<KsGroundEffectSystem>();
    private KsParticlesSystem ParticlesSystem => CEntMan.System<KsParticlesSystem>();
    private SharedTransformSystem ClientTransform => CEntMan.System<SharedTransformSystem>();

    /// <summary>
    ///     A test map whose grid has snow on x in [0, StripLength) and plating from -8 to StripLength + 40.
    /// </summary>
    private async Task<TestMapData> CreateStripAsync()
    {
        var testMap = await Pair.CreateTestMap();
        await Server.WaitPost(() =>
        {
            var mapSystem = SEntMan.System<SharedMapSystem>();
            var testTile = new Tile(SProtoMan.Index(Snow).TileId);
            var plating = new Tile(SProtoMan.Index(Plating).TileId);
            for (var y = -1; y <= StripRows; y++)
            {
                for (var x = -8; x < StripLength + 40; x++)
                {
                    var onStrip = x >= 0 && x < StripLength && y >= 0 && y < StripRows;
                    mapSystem.SetTile(testMap.Grid, new Vector2i(x, y), onStrip ? testTile : plating);
                }
            }
        });

        await Pair.RunTicksSync(5);
        return testMap;
    }

    /// <summary>
    ///     One client frame by hand: the ground system, then the particles, as the frame loop runs them.
    /// </summary>
    private void Frame()
    {
        GroundSystem.FrameUpdate(Dt);
        ParticlesSystem.FrameUpdate(Dt);
    }

    /// <summary>
    ///     Moves <paramref name="hullUid"/> along +x at <paramref name="speed"/> from <paramref name="fromX"/> to
    ///         <paramref name="toX"/> on its row, a frame at a time; returns how many particles it emitted.
    /// </summary>
    private long Drive(EntityUid hullUid, float fromX, float toX, float speed, float y = 0.5f)
    {
        var before = GroundSystem.TotalEmitted;
        var x = fromX;
        ClientTransform.SetLocalPosition(hullUid, new Vector2(x, y));
        Frame();
        while (x < toX)
        {
            x = MathF.Min(toX, x + speed * Dt);
            ClientTransform.SetLocalPosition(hullUid, new Vector2(x, y));
            Frame();
        }

        return GroundSystem.TotalEmitted - before;
    }

    private async Task<EntityUid> SpawnHullAsync(TestMapData testMap, EntProtoId prototype, float x = -6f, float y = 0.5f)
    {
        EntityUid hullUid = default;
        await Client.WaitPost(() => hullUid = CSpawnAtPosition(prototype, new EntityCoordinates(testMap.CGridUid, x, y)));
        return hullUid;
    }

    [Test]
    public async Task EmitsOnTheMarkedTileOnlyAndScalesWithSpeed()
    {
        await OverrideCVar(Side.Client, KsCCVars.ParticlesGroundCull, false);
        var testMap = await CreateStripAsync();
        var hullUid = await SpawnHullAsync(testMap, _hull);

        await Client.WaitAssertion(() =>
        {
            var effect = SnowEffectPrototype;
            var fullSpeed = effect.FullSpeed;
            var halfSpeed = (effect.MinSpeed + effect.FullSpeed) / 2f;

            // Up to speed on plating first, so the smoothing has settled before the strip.
            Assert.That(Drive(hullUid, -6f, -0.01f, fullSpeed), Is.Zero, "plating throws up nothing");
            var atFull = Drive(hullUid, 0f, StripLength, fullSpeed);
            Assert.That(Drive(hullUid, StripLength + 0.01f, StripLength + 6f, fullSpeed), Is.Zero, "nothing once past the strip");

            var expectedFull = KsGroundEffectMath.Density(effect.PerMetre, effect.MinSpeed, effect.FullSpeed, effect.SlowFactor, fullSpeed) * StripLength;
            Assert.That(atFull, Is.EqualTo(expectedFull).Within(2), "density x distance over the strip, at full speed");

            Drive(hullUid, -6f, -0.01f, halfSpeed);
            var atHalf = Drive(hullUid, 0f, StripLength, halfSpeed);
            var expectedHalf = KsGroundEffectMath.Density(effect.PerMetre, effect.MinSpeed, effect.FullSpeed, effect.SlowFactor, halfSpeed) * StripLength;
            Assert.That(atHalf, Is.EqualTo(expectedHalf).Within(2), "fewer per metre, slower");
            Assert.That(atHalf, Is.LessThan(atFull));

            // The particles are where the hull was, thrown back.
            var state = CEntMan.GetComponent<KsGroundEffectStateComponent>(hullUid);
            var emitterUid = FirstEmitter(state);
            var emitterComponent = CEntMan.GetComponent<KsParticleEmitterComponent>(emitterUid);
            Assert.That(CEntMan.GetComponent<TransformComponent>(emitterUid).ParentUid, Is.EqualTo(hullUid), "the emitter rides the hull");
            Assert.That(emitterComponent.Alive, Is.GreaterThan(0));

            // Slow enough, and short enough that the emitter isn't left idle long enough to go.
            Drive(hullUid, -2f, -0.01f, effect.MinSpeed * 0.5f);
            Assert.That(Drive(hullUid, 0f, 1f, effect.MinSpeed * 0.5f), Is.Zero, "nothing below the minimum speed");
        });
    }

    [Test]
    public async Task NothingEmitsWhileCulledAndHeadlessDrawsNothing()
    {
        // The default: the headless client records no views, so nothing is in view.
        await OverrideCVar(Side.Client, KsCCVars.ParticlesGroundCull, true);
        var testMap = await CreateStripAsync();
        var hullUid = await SpawnHullAsync(testMap, _hull);

        await Client.WaitAssertion(() =>
        {
            var fullSpeed = SnowEffectPrototype.FullSpeed;
            Drive(hullUid, -6f, -0.01f, fullSpeed);
            Assert.That(Drive(hullUid, 0f, StripLength, fullSpeed), Is.Zero, "out of every view");

            // A view over the strip, as the floor overlay records while drawing.
            Drive(hullUid, -6f, -0.01f, fullSpeed);
            var emitted = 0L;
            var x = 0f;
            while (x < StripLength)
            {
                x += fullSpeed * Dt;
                ClientTransform.SetLocalPosition(hullUid, new Vector2(x, 0.5f));
                ParticlesSystem.RecordView(testMap.MapId, new Box2(-10, -10, 2 * StripLength, 10));
                var before = GroundSystem.TotalEmitted;
                Frame();
                emitted += GroundSystem.TotalEmitted - before;
            }

            Assert.That(emitted, Is.GreaterThan(0), "in view, it emits");
        });
    }

    [Test]
    public async Task JumpsAndMapChangesEmitNothingAndClear()
    {
        await OverrideCVar(Side.Client, KsCCVars.ParticlesGroundCull, false);
        var testMap = await CreateStripAsync();
        var otherMap = await Pair.CreateTestMap();
        var hullUid = await SpawnHullAsync(testMap, _hull);

        await Client.WaitAssertion(() =>
        {
            var fullSpeed = SnowEffectPrototype.FullSpeed;
            Drive(hullUid, -6f, -0.01f, fullSpeed);
            Assert.That(Drive(hullUid, 0f, StripLength / 2f, fullSpeed), Is.GreaterThan(0));

            var before = GroundSystem.TotalEmitted;
            ClientTransform.SetLocalPosition(hullUid, new Vector2(StripLength / 2f - 6f, 0.5f));
            Frame();
            Assert.That(GroundSystem.TotalEmitted, Is.EqualTo(before), "a 6 m jump back over the strip is a teleport, not a drive");

            var emitterUid = FirstEmitter(CEntMan.GetComponent<KsGroundEffectStateComponent>(hullUid));
            Assert.That(CEntMan.GetComponent<KsParticleEmitterComponent>(emitterUid).Alive, Is.GreaterThan(0));

            ClientTransform.SetCoordinates(hullUid, new EntityCoordinates(otherMap.CGridUid, 0.5f, 0.5f));
            Frame();
            Assert.That(CEntMan.GetComponent<KsParticleEmitterComponent>(emitterUid).Alive, Is.Zero, "another map clears what was left on the old one");
            Assert.That(GroundSystem.TotalEmitted, Is.EqualTo(before));
        });
    }

    [Test]
    public async Task EmittersAreDeletedWithTheHullAndWhenIdle()
    {
        await OverrideCVar(Side.Client, KsCCVars.ParticlesGroundCull, false);
        var testMap = await CreateStripAsync();
        var hullUid = await SpawnHullAsync(testMap, _hull);
        var otherHullUid = await SpawnHullAsync(testMap, _hull, y: 2.5f);
        EntityUid emitterUid = default;
        EntityUid otherEmitterUid = default;

        await Client.WaitPost(() =>
        {
            var fullSpeed = SnowEffectPrototype.FullSpeed;
            Drive(hullUid, -6f, StripLength / 2f, fullSpeed);
            Drive(otherHullUid, -6f, StripLength / 2f, fullSpeed, y: 2.5f);
            emitterUid = FirstEmitter(CEntMan.GetComponent<KsGroundEffectStateComponent>(hullUid));
            otherEmitterUid = FirstEmitter(CEntMan.GetComponent<KsGroundEffectStateComponent>(otherHullUid));

            CEntMan.DeleteEntity(hullUid);
        });

        await Pair.RunTicksSync(2);

        await Client.WaitAssertion(() =>
        {
            Assert.That(CEntMan.EntityExists(emitterUid), Is.False, "the hull's emitter goes with it");
            Assert.That(CEntMan.EntityExists(otherEmitterUid), Is.True);

            // Parked: its particles die, then the emitter is left unfed past its idle time.
            for (var t = 0f; t < KsGroundEffectSystem.IdleLifetime + 2f; t += Dt)
            {
                Frame();
            }
        });

        await Pair.RunTicksSync(2);
        await Client.WaitAssertion(() =>
        {
            Assert.That(CEntMan.EntityExists(otherEmitterUid), Is.False, "an idle emitter with nothing alive is deleted");
            Assert.That(CEntMan.GetComponent<KsGroundEffectStateComponent>(otherHullUid).EmitterCount, Is.Zero);
        });
    }

    /// <summary>
    ///     Many four-contact hulls driving over the strip: after a warm-up that spawns their emitters and grows the
    ///         particle arrays, the ground and particle systems' frames allocate nothing.
    /// </summary>
    [Test]
    public async Task SteadyStateAllocatesNothing()
    {
        const int hulls = 64;
        const int warmUpFrames = 120;
        const int measuredFrames = 300;
        const float speed = 3f;

        await OverrideCVar(Side.Client, KsCCVars.ParticlesGroundCull, false);
        var testMap = await CreateStripAsync();
        var hullUids = new EntityUid[hulls];
        for (var i = 0; i < hulls; i++)
        {
            hullUids[i] = await SpawnHullAsync(testMap, _quadHull, x: -2f - (i % 4) * 0.25f, y: 0.5f + i / 4);
        }

        await Client.WaitAssertion(() =>
        {
            var positions = new Vector2[hulls];
            for (var i = 0; i < hulls; i++)
            {
                positions[i] = CEntMan.GetComponent<TransformComponent>(hullUids[i]).LocalPosition;
            }

            void MoveAll()
            {
                for (var i = 0; i < hulls; i++)
                {
                    positions[i].X += speed * Dt;
                    ClientTransform.SetLocalPosition(hullUids[i], positions[i]);
                }
            }

            for (var frame = 0; frame < warmUpFrames; frame++)
            {
                MoveAll();
                Frame();
            }

            var emittedBefore = GroundSystem.TotalEmitted;
            var threadBytes = 0L;
            var processBytes = 0L;
            for (var frame = 0; frame < measuredFrames; frame++)
            {
                MoveAll();

                var threadStart = GC.GetAllocatedBytesForCurrentThread();
                var processStart = GC.GetTotalAllocatedBytes(precise: true);
                Frame();
                processBytes += GC.GetTotalAllocatedBytes(precise: true) - processStart;
                threadBytes += GC.GetAllocatedBytesForCurrentThread() - threadStart;
            }

            var emitted = GroundSystem.TotalEmitted - emittedBefore;
            TestContext.Out.WriteLine($"{hulls} hulls x 4 contacts, {measuredFrames} frames: {emitted} particles emitted, " +
                                      $"{ParticlesSystem.TotalAlive} alive at the end; allocated {threadBytes} B on the game thread, " +
                                      $"{processBytes} B process-wide (GC.GetTotalAllocatedBytes(precise: true)).");

            Assert.That(emitted, Is.GreaterThan(hulls * measuredFrames / 10), "the measured frames were emitting");
            Assert.That(threadBytes, Is.Zero, "the ground and particle frames allocate nothing in steady state");
        });
    }

    /// <summary>
    ///     The tile partial reaches the real tile, and leaves upstream's data on it.
    /// </summary>
    [Test]
    public async Task SnowTilePartialAppliesAndKeepsUpstreamData()
    {
        await Client.WaitAssertion(() =>
        {
            var snow = CProtoMan.Index(Snow);
            Assert.Multiple(() =>
            {
                Assert.That(snow.KsGroundEffect, Is.EqualTo(SnowEffect), "the partial set the effect");
                Assert.That(snow.FootstepSounds, Is.Not.Null, "upstream's footsteps are still there");
                Assert.That(snow.Sprite, Is.Not.Null, "and its sprite");
            });

            var tileDefinitionManager = Client.ResolveDependency<ITileDefinitionManager>();
            var registered = (ContentTileDefinition)tileDefinitionManager[Snow];
            Assert.That(registered.KsGroundEffect, Is.EqualTo(SnowEffect), "the registered tile definition has it too");
        });
    }

    private static EntityUid FirstEmitter(KsGroundEffectStateComponent state)
    {
        foreach (var emitterUid in state.Emitters)
        {
            if (emitterUid is { } uid)
                return uid;
        }

        Assert.Fail("the hull has no emitter");
        return default;
    }
}
