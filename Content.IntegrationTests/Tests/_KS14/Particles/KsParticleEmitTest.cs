#nullable enable
using System.Numerics;
using Content.Client._KS14.Particles;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.Particles;
using NUnit.Framework;
using Robust.Shared;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Particles;

/// <summary>
///     <see cref="KsParticlesSystem.Emit"/>: particles placed by the caller rather than by the emitter's own rate.
/// </summary>
[TestFixture]
[TestOf(typeof(KsParticlesSystem))]
public sealed class KsParticleEmitTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private readonly ProtoId<KsParticlesPrototype> _world = "KsParticleEmitTestWorld";
    private readonly ProtoId<KsParticlesPrototype> _local = "KsParticleEmitTestLocal";

    [TestPrototypes]
    private const string Prototypes = @"
- type: ksParticles
  id: KsParticleEmitTestWorld
  space: World
  count: 4
  spawning: 0
  lifespan: 0
  velocity: [ 1, 0 ]
  color: ""#808080""
  tickRate: 20

- type: ksParticles
  id: KsParticleEmitTestLocal
  count: 4
  spawning: 0
  lifespan: 0
  tickRate: 20
";

    private KsParticlesSystem ClientSystem => CEntMan.System<KsParticlesSystem>();

    private async Task<Entity<KsParticlesComponent, KsParticleEmitterComponent>> SpawnAsync(ProtoId<KsParticlesPrototype> prototype)
    {
        var testMap = await Pair.CreateTestMap();
        Entity<KsParticlesComponent, KsParticleEmitterComponent> emitter = default;
        await Client.WaitPost(() =>
        {
            var uid = ClientSystem.SpawnLocal(prototype, new EntityCoordinates(testMap.CMapUid, Vector2.Zero), once: true)!.Value;
            emitter = (uid, CEntMan.GetComponent<KsParticlesComponent>(uid), CEntMan.GetComponent<KsParticleEmitterComponent>(uid));
        });

        return emitter;
    }

    [Test]
    public async Task EmitSpreadsAlongTheSegmentWithTheGivenVelocityAndTint()
    {
        var emitter = await SpawnAsync(_world);

        await Client.WaitAssertion(() =>
        {
            var spawned = ClientSystem.Emit(emitter, new Vector2(0f, 2f), new Vector2(8f, 2f), new Vector2(0f, -3f), speedScale: 2f, new Color(0.5f, 1f, 1f), count: 4);

            Assert.That(spawned, Is.EqualTo(4));
            Assert.That(emitter.Comp2.Alive, Is.EqualTo(4));
            for (var i = 0; i < 4; i++)
            {
                var particle = emitter.Comp2.Particles[i];
                Assert.Multiple(() =>
                {
                    Assert.That(particle.Position.X, Is.EqualTo(1f + 2f * i).Within(1e-4f), "evenly along the segment, centred in each share");
                    Assert.That(particle.Position.Y, Is.EqualTo(2f).Within(1e-4f));
                    Assert.That(particle.Velocity.X, Is.EqualTo(2f).Within(1e-4f), "the prototype's velocity, scaled");
                    Assert.That(particle.Velocity.Y, Is.EqualTo(-3f).Within(1e-4f), "plus the inherited one");
                    Assert.That(particle.Color.R, Is.EqualTo(0.5f * (128f / 255f)).Within(1e-3f), "tinted");
                    Assert.That(particle.Color.G, Is.EqualTo(128f / 255f).Within(1e-3f));
                });
            }
        });
    }

    [Test]
    public async Task EmitHonoursCountAndDensity()
    {
        var emitter = await SpawnAsync(_world);

        await Client.WaitAssertion(() =>
        {
            Assert.That(ClientSystem.Emit(emitter, Vector2.Zero, Vector2.One, Vector2.Zero, 1f, Color.White, count: 10), Is.EqualTo(4), "no more than the count");
            Assert.That(emitter.Comp2.Alive, Is.EqualTo(4));
            ClientSystem.Clear(emitter.Owner);
            Assert.That(emitter.Comp2.Alive, Is.EqualTo(0), "cleared");
        });

        await OverrideCVar(Side.Client, KsCCVars.ParticlesDensity, 0.5f);

        await Client.WaitAssertion(() =>
        {
            ClientSystem.Clear(emitter.Owner);
            Assert.That(ClientSystem.Emit(emitter, Vector2.Zero, Vector2.One, Vector2.Zero, 1f, Color.White, count: 10), Is.EqualTo(2), "half the count at half density");
        });

        await OverrideCVar(Side.Client, KsCCVars.ParticlesMaxAlive, 0);
        await Client.WaitAssertion(() =>
        {
            // The budget is dealt out at the start of each frame.
            ClientSystem.FrameUpdate(0.01f);
            ClientSystem.Clear(emitter.Owner);
            Assert.That(ClientSystem.Emit(emitter, Vector2.Zero, Vector2.One, Vector2.Zero, 1f, Color.White, count: 10), Is.EqualTo(0), "nothing past the global budget");
        });
    }

    [Test]
    public async Task EmitRefusesALocalEmitter()
    {
        var emitter = await SpawnAsync(_local);

        await Client.WaitAssertion(() =>
        {
            Assert.That(ClientSystem.Emit(emitter, Vector2.Zero, Vector2.One, Vector2.Zero, 1f, Color.White, count: 2), Is.EqualTo(0));
            Assert.That(emitter.Comp2.Alive, Is.EqualTo(0));
        });
    }
}
