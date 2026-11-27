using System.Numerics;
using Content.Shared._KS14.CrewVehicle;
using Robust.Shared.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._KS14.CrewVehicle;

/// <summary>
///     The interior-to-world mapping that the backdrop behind a crew vehicle's interior is drawn with, checked against
///         the hull's own facing convention and the engine's view matrices.
/// </summary>
[TestFixture]
public sealed class KsCrewVehicleInteriorTransformTest
{
    private static readonly Vector2 RenderScale = new Vector2(32f, -32f);
    private static readonly Vector2 Anchor = new Vector2(2.5f, 1.5f);
    private static readonly Vector2 HullPosition = new Vector2(-14.25f, 37.5f);

    private static readonly double[] HullDegrees = [0d, 90d, 180d, 270d];

    private static Vector2 ToView(Eye eye, Vector2 point)
    {
        eye.GetViewMatrix(out var viewMatrix, RenderScale);
        return Vector2.Transform(point, viewMatrix);
    }

    private static void AssertClose(Vector2 actual, Vector2 expected, string message)
    {
        Assert.That(actual.X, Is.EqualTo(expected.X).Within(1e-4f), $"X: {message}");
        Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(1e-4f), $"Y: {message}");
    }

    [Test]
    public void AnchorMapsToHullCentre([ValueSource(nameof(HullDegrees))] double degrees)
    {
        var world = KsCrewVehicleInteriorTransform.InteriorToWorld(Anchor, Anchor, HullPosition, Angle.FromDegrees(degrees));
        AssertClose(world, HullPosition, $"anchor at hull rotation {degrees}");
    }

    /// <summary>
    ///     The interior's front is grid north: a point north of the anchor is ahead of the hull, whichever way it faces.
    ///         Left of the anchor is the hull's left too, which catches a mirrored mapping that still gets north right.
    /// </summary>
    [Test]
    public void NorthOfAnchorIsAheadOfHull([ValueSource(nameof(HullDegrees))] double degrees)
    {
        var hullRotation = Angle.FromDegrees(degrees);
        var forward = KsCrewVehicleHullFrame.Forward(hullRotation);

        var ahead = KsCrewVehicleInteriorTransform.InteriorToWorld(
            Anchor + new Vector2(0f, 2f), Anchor, HullPosition, hullRotation);
        AssertClose(ahead, HullPosition + forward * 2f, $"ahead at hull rotation {degrees}");

        // The hull's left, facing forward, is forward turned a quarter anticlockwise.
        var left = new Vector2(-forward.Y, forward.X);
        var leftPoint = KsCrewVehicleInteriorTransform.InteriorToWorld(
            Anchor + new Vector2(-1f, 0f), Anchor, HullPosition, hullRotation);
        AssertClose(leftPoint, HullPosition + left, $"left at hull rotation {degrees}");
    }

    [Test]
    public void RoundTrip([ValueSource(nameof(HullDegrees))] double degrees)
    {
        var hullRotation = Angle.FromDegrees(degrees + 17d);
        var interiorPoint = new Vector2(4.75f, -0.5f);

        var world = KsCrewVehicleInteriorTransform.InteriorToWorld(interiorPoint, Anchor, HullPosition, hullRotation);
        var back = KsCrewVehicleInteriorTransform.WorldToInterior(world, Anchor, HullPosition, hullRotation);
        AssertClose(back, interiorPoint, $"round trip at hull rotation {degrees + 17d}");
    }

    /// <summary>
    ///     Through the backdrop eye, the world point under an interior point is drawn on the pixel the interior eye draws
    ///         that interior point on, including with an eye offset and a turned eye.
    /// </summary>
    [Test]
    public void BackdropEyeDrawsTheWorldUnderTheInterior([ValueSource(nameof(HullDegrees))] double degrees)
    {
        var hullRotation = Angle.FromDegrees(degrees + 30d);
        var interiorEye = new Eye
        {
            Position = new MapCoordinates(new Vector2(3f, 2f), new MapId(1)),
            Offset = new Vector2(0.5f, -0.25f),
            Rotation = Angle.FromDegrees(10d),
            Scale = new Vector2(0.75f, 0.75f),
        };

        KsCrewVehicleInteriorTransform.GetBackdropEye(
            interiorEye.Position.Position + interiorEye.Offset,
            interiorEye.Rotation,
            Anchor,
            HullPosition,
            hullRotation,
            out var backdropCentre,
            out var backdropRotation);

        var backdropEye = new Eye
        {
            Position = new MapCoordinates(backdropCentre, new MapId(2)),
            Rotation = backdropRotation,
            Scale = interiorEye.Scale,
        };

        var points = new[] { Anchor, Anchor + new Vector2(1f, 0f), Anchor + new Vector2(0f, 1f), new Vector2(-1.25f, 4.5f) };
        foreach (var point in points)
        {
            var world = KsCrewVehicleInteriorTransform.InteriorToWorld(point, Anchor, HullPosition, hullRotation);
            var interiorPixel = ToView(interiorEye, point);
            var backdropPixel = ToView(backdropEye, world);

            Assert.That(backdropPixel.X, Is.EqualTo(interiorPixel.X).Within(1e-3f), $"X for {point}");
            Assert.That(backdropPixel.Y, Is.EqualTo(interiorPixel.Y).Within(1e-3f), $"Y for {point}");
        }
    }

    /// <summary>
    ///     The backdrop's FOV is cast from the hull: the eye's position is the hull's, and its offset keeps every point
    ///         on the pixel the centred eye put it on, at any turn of the eye, as the engine applies an offset unrotated.
    /// </summary>
    [Test]
    public void BackdropFovIsCastFromTheHullWithoutMovingTheView([ValueSource(nameof(HullDegrees))] double degrees)
    {
        var eyeCentre = HullPosition + new Vector2(1.75f, -0.5f);
        var eyeRotation = Angle.FromDegrees(degrees + 30d);
        KsCrewVehicleInteriorTransform.GetBackdropFovEye(HullPosition, eyeCentre, out var position, out var offset);

        AssertClose(position, HullPosition, "FOV is cast from the hull");

        var centred = new Eye { Position = new MapCoordinates(eyeCentre, new MapId(2)), Rotation = eyeRotation };
        var fromHull = new Eye { Position = new MapCoordinates(position, new MapId(2)), Offset = offset, Rotation = eyeRotation };
        foreach (var point in new[] { HullPosition, eyeCentre, HullPosition + new Vector2(-3f, 4.5f) })
        {
            var drawn = ToView(fromHull, point);
            var expected = ToView(centred, point);
            Assert.That(drawn.X, Is.EqualTo(expected.X).Within(1e-2f), $"X: {point} stays where it was drawn, eye at {eyeRotation}");
            Assert.That(drawn.Y, Is.EqualTo(expected.Y).Within(1e-2f), $"Y: {point} stays where it was drawn, eye at {eyeRotation}");
        }
    }
}
