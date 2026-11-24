using System.Numerics;

namespace Content.Shared._KS14.CrewVehicle.Drive;

/// <summary>
///     What steering is given for one tick.
/// </summary>
/// <param name="Wish">The mover's wish velocity, in world axes, as a speed: what the driver's held keys ask for.</param>
/// <param name="StickFrame">
///     The turn the mover applied to the keys to get <paramref name="Wish"/> (<c>GetParentGridAngle</c> of the hull's
///         mover): turning <paramref name="Wish"/> back by it gives the keys as pressed.
/// </param>
/// <param name="HullRotation">The hull's world rotation.</param>
/// <param name="HeadingFrame">
///     The world rotation of the grid the hull is on, zero off grid: pressed directions are snapped to its axes.
/// </param>
/// <param name="FrameTime">The tick's length, in seconds.</param>
public readonly record struct KsCrewVehicleSteeringInput(
    Vector2 Wish,
    Angle StickFrame,
    Angle HullRotation,
    Angle HeadingFrame,
    float FrameTime);

/// <summary>
///     What steering decides for one tick: the hull's new rotation, and the wish velocity the mover should use instead.
/// </summary>
public readonly record struct KsCrewVehicleSteeringResult(Angle HullRotation, Vector2 Wish);

/// <summary>
///     The two steering modes, as pure functions of the hull's rotation and this tick's keys. Their wish always lies
///         along the hull's facing, forward or back.
/// </summary>
public static class KsCrewVehicleSteering
{
    /// <summary>
    ///     A pressed direction opposite the facing, give or take this much, backs up rather than turning round.
    /// </summary>
    public static readonly Angle ReverseCone = Angle.FromDegrees(45);

    /// <summary>
    ///     Below this wish speed, in m/s, nothing is pressed.
    /// </summary>
    private const float NoInput = 0.0001f;

    /// <summary>
    ///     Within this of a 45-degree split between two headings, the press counts as exactly diagonal.
    /// </summary>
    private const double DiagonalTolerance = 0.001;

    /// <summary>
    ///     Press a direction and the hull turns to face it, crawling while it turns, then drives that way; press the way
    ///         behind it and it backs up without turning. Directions snap to the four axes of the grid the hull is on.
    /// </summary>
    public static KsCrewVehicleSteeringResult PressDirection(in KsCrewVehicleSteeringInput input, KsCrewVehicleDriveComponent drive)
    {
        var speed = input.Wish.Length();
        if (speed < NoInput)
            return new KsCrewVehicleSteeringResult(input.HullRotation, Vector2.Zero);

        var heading = SnapHeading(input.Wish.ToWorldAngle(), input.HeadingFrame, input.HullRotation);
        var toHeading = Angle.ShortestDistance(input.HullRotation, heading);

        if (Math.Abs(toHeading.Theta) > Math.PI - ReverseCone.Theta)
        {
            return new KsCrewVehicleSteeringResult(input.HullRotation,
                -KsCrewVehicleHullFrame.Forward(input.HullRotation) * speed * drive.ReverseSpeedFraction);
        }

        var maxStep = drive.TurnRate.Theta * input.FrameTime;
        var step = Math.Clamp(toHeading.Theta, -maxStep, maxStep);
        var rotation = new Angle(input.HullRotation.Theta + step);
        var aligned = Math.Abs(toHeading.Theta - step) <= drive.AlignTolerance.Theta;

        return new KsCrewVehicleSteeringResult(rotation,
            KsCrewVehicleHullFrame.Forward(rotation) * speed * (aligned ? 1f : drive.TurningSpeedFraction));
    }

    /// <summary>
    ///     Left and right turn the hull on the spot or while moving; up drives forward and down backs up, whichever way
    ///         the hull faces.
    /// </summary>
    public static KsCrewVehicleSteeringResult Tank(in KsCrewVehicleSteeringInput input, KsCrewVehicleDriveComponent drive)
    {
        var speed = input.Wish.Length();
        if (speed < NoInput)
            return new KsCrewVehicleSteeringResult(input.HullRotation, Vector2.Zero);

        // The keys as pressed: +X right, +Y up.
        var stick = (-input.StickFrame).RotateVec(input.Wish);
        var threshold = speed * 0.01f;

        var rotation = input.HullRotation;
        if (Math.Abs(stick.X) > threshold)
        {
            // Right turns clockwise, which is a negative angle.
            rotation = new Angle(rotation.Theta - Math.Sign(stick.X) * drive.TurnRate.Theta * input.FrameTime);
        }

        var forward = KsCrewVehicleHullFrame.Forward(rotation);
        if (stick.Y > threshold)
            return new KsCrewVehicleSteeringResult(rotation, forward * speed);

        if (stick.Y < -threshold)
            return new KsCrewVehicleSteeringResult(rotation, -forward * speed * drive.ReverseSpeedFraction);

        return new KsCrewVehicleSteeringResult(rotation, Vector2.Zero);
    }

    /// <summary>
    ///     The axis of <paramref name="frame"/> nearest <paramref name="pressed"/>; on an exact diagonal, of the two
    ///         nearest, the one nearer <paramref name="hullRotation"/>, so holding two keys keeps going the way it goes.
    /// </summary>
    public static Angle SnapHeading(Angle pressed, Angle frame, Angle hullRotation)
    {
        var quarter = Math.PI / 2;
        var quarters = Angle.ShortestDistance(frame, pressed).Theta / quarter;
        var lower = new Angle(frame.Theta + Math.Floor(quarters) * quarter);
        var upper = new Angle(frame.Theta + Math.Ceiling(quarters) * quarter);
        var fraction = quarters - Math.Floor(quarters);

        if (Math.Abs(fraction - 0.5) < DiagonalTolerance)
        {
            var toLower = Math.Abs(Angle.ShortestDistance(hullRotation, lower).Theta);
            var toUpper = Math.Abs(Angle.ShortestDistance(hullRotation, upper).Theta);
            return toLower <= toUpper ? lower : upper;
        }

        return fraction < 0.5 ? lower : upper;
    }
}
