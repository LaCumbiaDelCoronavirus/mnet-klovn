using System.Numerics;
using Content.Client._KS14.GroundEffect;
using Content.Client._KS14.Particles;
using NUnit.Framework;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._KS14.GroundEffect;

/// <summary>
///     <see cref="KsGroundEffectMath"/> on its own, no pair: how many particles a speed and a distance owe, smoothing,
///         and the view cull.
/// </summary>
[TestFixture]
[TestOf(typeof(KsGroundEffectMath))]
public sealed class KsGroundParticleMathTest
{
    private const float PerMetre = 4f;
    private const float MinSpeed = 1f;
    private const float FullSpeed = 5f;
    private const float SlowFactor = 0.25f;

    private static float Density(float speed) => KsGroundEffectMath.Density(PerMetre, MinSpeed, FullSpeed, SlowFactor, speed);

    [Test]
    public void DensityIsZeroBelowTheMinimumSpeed()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Density(0f), Is.Zero);
            Assert.That(Density(MinSpeed * 0.99f), Is.Zero, "just below the minimum");
            Assert.That(Density(MinSpeed), Is.EqualTo(SlowFactor * PerMetre).Within(1e-5f), "the slow fraction at the minimum");
        });
    }

    [Test]
    public void DensityRisesMonotonicallyToTheFullRate()
    {
        Assert.That(Density(FullSpeed), Is.EqualTo(PerMetre).Within(1e-5f));
        Assert.That(Density(FullSpeed * 3f), Is.EqualTo(PerMetre).Within(1e-5f), "no more past full speed");

        var previous = Density(MinSpeed);
        for (var i = 1; i <= 50; i++)
        {
            var speed = MinSpeed + (FullSpeed - MinSpeed) * i / 50f;
            var density = Density(speed);
            Assert.That(density, Is.GreaterThanOrEqualTo(previous), $"at {speed} m/s");
            previous = density;
        }

        // Particles a second, speed x density, grow faster than the speed itself.
        Assert.That(FullSpeed * Density(FullSpeed), Is.GreaterThan(2f * (FullSpeed / 2f) * Density(FullSpeed / 2f)));
    }

    /// <summary>
    ///     The same distance in many short frames or a few long ones owes the same particles: the fraction is carried.
    /// </summary>
    [TestCase(240, 60)]
    [TestCase(144, 30)]
    public void EmitCountIsFrameRateIndependent(int fastFps, int slowFps)
    {
        const float speed = 3f;
        const float seconds = 4f;
        var density = Density(speed);

        int Total(int fps)
        {
            var accumulator = 0f;
            var total = 0;
            var frames = (int)(seconds * fps);
            for (var i = 0; i < frames; i++)
            {
                total += KsGroundEffectMath.TakeEmitCount(ref accumulator, density, speed / fps, cap: 100);
            }

            return total;
        }

        var expected = density * speed * seconds;
        Assert.Multiple(() =>
        {
            Assert.That(Total(fastFps), Is.EqualTo(expected).Within(1f));
            Assert.That(Total(slowFps), Is.EqualTo(expected).Within(1f));
        });
    }

    [Test]
    public void EmitCountRespectsTheCap()
    {
        var accumulator = 0f;
        Assert.That(KsGroundEffectMath.TakeEmitCount(ref accumulator, 10f, 5f, cap: 8), Is.EqualTo(8));
        Assert.That(accumulator, Is.LessThan(1f), "what the cap cut is dropped, not owed");
        Assert.That(KsGroundEffectMath.TakeEmitCount(ref accumulator, 10f, 5f, cap: 0), Is.Zero);
    }

    [Test]
    public void SpeedSmoothingIsFrameRateIndependent()
    {
        float After(int fps)
        {
            var speed = 0f;
            for (var i = 0; i < fps / 4; i++)
            {
                speed = KsGroundEffectMath.SmoothSpeed(speed, 6f, 1f / fps, 0.1f);
            }

            return speed;
        }

        Assert.Multiple(() =>
        {
            Assert.That(After(24), Is.EqualTo(After(144)).Within(0.05f), "a quarter second at 24 and 144 fps");
            Assert.That(After(60), Is.EqualTo(6f * (1f - MathF.Exp(-2.5f))).Within(0.05f), "the exponential's value");
        });

        var converged = 0f;
        for (var i = 0; i < 600; i++)
        {
            converged = KsGroundEffectMath.SmoothSpeed(converged, 4f, 1f / 60f, 0.1f);
        }

        Assert.That(converged, Is.EqualTo(4f).Within(1e-3f), "converges on a steady sample");
    }

    [Test]
    public void ViewCull()
    {
        var map = new MapId(5);
        var other = new MapId(6);
        var views = new KsParticleView[4];
        views[0] = new KsParticleView(other, new Box2(-100, -100, 100, 100));
        views[1] = new KsParticleView(map, new Box2(0, 0, 10, 10));

        bool Seen(Vector2 position, int count = 2, bool failOpen = false)
            => KsGroundEffectMath.IsInAnyView(views, count, failOpen, map, position, margin: 2f);

        Assert.Multiple(() =>
        {
            Assert.That(Seen(new Vector2(5, 5)), Is.True, "inside");
            Assert.That(Seen(new Vector2(11.9f, 5)), Is.True, "inside the margin");
            Assert.That(Seen(new Vector2(-1.9f, -1.9f)), Is.True, "inside the margin, at a corner");
            Assert.That(Seen(new Vector2(12.1f, 5)), Is.False, "past the margin");
            Assert.That(Seen(new Vector2(5, -2.1f)), Is.False, "past the margin, below");
            Assert.That(Seen(new Vector2(50, 50)), Is.False, "inside a view of another map only");
            Assert.That(Seen(new Vector2(5, 5), count: 0), Is.False, "nothing was drawn");
            Assert.That(Seen(new Vector2(500, 500), count: 0, failOpen: true), Is.True, "too many views to keep: everything is seen");
            Assert.That(Seen(new Vector2(5, 5), count: 99), Is.True, "a count past the array is clamped");
        });
    }
}
