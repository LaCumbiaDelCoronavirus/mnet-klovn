using System.Numerics;
using Content.Shared._KS14.CrewVehicle;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._KS14.CrewVehicle;

/// <summary>
///     How a hull's body lines up with its cabin for a round going through: outline on outline, the half turn between
///         their frames, and back again.
/// </summary>
[TestFixture]
[TestOf(typeof(KsCrewVehiclePassThroughFrame))]
public sealed class KsCrewVehiclePassThroughFrameTest
{
    // A hull smaller than its cabin, off centre in its own frame, and a cabin of another shape, away from the origin.
    private static readonly Box2 HullBox = new(-0.65f, -1.2f, 0.65f, 1.4f);
    private static readonly Box2 CabinBox = new(1f, 2f, 6f, 5f);

    private static readonly double[] Degrees = [0d, 30d, 90d, 135d, 180d, 270d];

    private static void AssertClose(Vector2 actual, Vector2 expected, string message)
    {
        Assert.That(actual.X, Is.EqualTo(expected.X).Within(1e-4f), $"X: {message}");
        Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(1e-4f), $"Y: {message}");
    }

    /// <summary>
    ///     The hull faces south and the cabin north: the hull's corners land on the cabin's opposite ones.
    /// </summary>
    [Test]
    public void HullOutlineLandsOnTheCabinsHalfATurnRound()
    {
        AssertClose(KsCrewVehiclePassThroughFrame.HullToInterior(HullBox, CabinBox, HullBox.TopRight), CabinBox.BottomLeft, "top right");
        AssertClose(KsCrewVehiclePassThroughFrame.HullToInterior(HullBox, CabinBox, HullBox.BottomLeft), CabinBox.TopRight, "bottom left");
        AssertClose(KsCrewVehiclePassThroughFrame.HullToInterior(HullBox, CabinBox, HullBox.Center), CabinBox.Center, "centre");

        // Halfway along the hull's east side is halfway along the cabin's west side.
        var eastMiddle = new Vector2(HullBox.Right, HullBox.Center.Y);
        AssertClose(KsCrewVehiclePassThroughFrame.HullToInterior(HullBox, CabinBox, eastMiddle),
            new Vector2(CabinBox.Left, CabinBox.Center.Y),
            "east side");
    }

    [Test]
    public void PointsAndDirectionsGoBackTheyCame([ValueSource(nameof(Degrees))] double degrees)
    {
        var direction = Angle.FromDegrees(degrees).ToVec();
        var point = new Vector2(0.3f, -0.7f);

        var inside = KsCrewVehiclePassThroughFrame.HullToInterior(HullBox, CabinBox, point);
        AssertClose(KsCrewVehiclePassThroughFrame.InteriorToHull(HullBox, CabinBox, inside), point, $"point at {degrees}");

        var insideDirection = KsCrewVehiclePassThroughFrame.HullDirectionToInterior(HullBox, CabinBox, direction);
        Assert.That(insideDirection.Length(), Is.EqualTo(1f).Within(1e-4f), $"a unit direction inside at {degrees}");
        AssertClose(KsCrewVehiclePassThroughFrame.InteriorDirectionToHull(HullBox, CabinBox, insideDirection), direction, $"direction at {degrees}");
    }

    /// <summary>
    ///     A round's straight line through the hull is a straight line through the cabin: where the mapped line from the
    ///         mapped entry leaves the cabin maps back to where the line leaves the hull.
    /// </summary>
    [Test]
    public void ALineThroughTheHullIsALineThroughTheCabin([ValueSource(nameof(Degrees))] double degrees)
    {
        var direction = Angle.FromDegrees(degrees).ToVec();
        var hullEntry = KsCrewVehiclePassThroughFrame.GetEntry(HullBox, new Vector2(0.1f, 0.2f), direction);
        var hullExit = KsCrewVehiclePassThroughFrame.GetExit(HullBox, new Vector2(0.1f, 0.2f), direction);

        var cabinEntry = KsCrewVehiclePassThroughFrame.HullToInterior(HullBox, CabinBox, hullEntry);
        var cabinDirection = KsCrewVehiclePassThroughFrame.HullDirectionToInterior(HullBox, CabinBox, direction);
        var cabinExit = KsCrewVehiclePassThroughFrame.GetExit(CabinBox, cabinEntry, cabinDirection);

        AssertClose(KsCrewVehiclePassThroughFrame.InteriorToHull(HullBox, CabinBox, cabinExit), hullExit, $"exit at {degrees}");
        Assert.That(Vector2.Dot(hullExit - hullEntry, direction), Is.GreaterThan(0f), $"the exit is ahead of the entry at {degrees}");
    }
}
