using System.Numerics;

namespace Content.Shared._KS14.CrewVehicle.Drive;

/// <summary>
///     What steering is given for one movement step.
/// </summary>
/// <param name="Wish">The mover's wish velocity, in world axes, as a speed: what the driver's held keys ask for.</param>
/// <param name="StickFrame">
///     The turn the mover applied to the keys to get <paramref name="Wish"/> (<c>GetParentGridAngle</c> of the hull's
///         mover): turning <paramref name="Wish"/> back by it gives the keys as pressed, whichever way the screen faces.
/// </param>
/// <param name="HullRotation">The hull's world rotation.</param>
/// <param name="Velocity">The hull's world velocity, as the mover read it this step: how a wheeled hull may turn.</param>
/// <param name="FrameTime">The step's length, in seconds.</param>
public readonly record struct KsCrewVehicleSteeringInput(
    Vector2 Wish,
    Angle StickFrame,
    Angle HullRotation,
    Vector2 Velocity,
    float FrameTime);

/// <summary>
///     What steering decides for one step: the hull's new rotation, and the wish velocity the mover should use instead.
/// </summary>
public readonly record struct KsCrewVehicleSteeringResult(Angle HullRotation, Vector2 Wish);

/// <summary>
///     Crew vehicle steering, as pure functions of the hull's rotation and velocity and this step's keys, for either drive
///         train. The wish always lies along the hull's facing, forward or back.
/// </summary>
public static class KsCrewVehicleSteering
{
    /// <summary>
    ///     Below this wish speed, in m/s, nothing is pressed.
    /// </summary>
    private const float NoInput = 0.0001f;

    /// <summary>
    ///     Tank controls. Up drives forward and down backs up, along the hull's facing and nothing else; left and right
    ///         turn it. Tracked, it turns on the spot; wheeled, only while rolling, and the other way backing up: a turn
    ///         key alone at rest does nothing. Only the keys and the hull's facing count, never which way the screen is
    ///         turned.
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
            rotation = new Angle(rotation.Theta -
                Math.Sign(stick.X) * drive.TurnRate.Theta * TurnFactor(input, drive) * input.FrameTime);
        }

        var forward = KsCrewVehicleHullFrame.Forward(rotation);
        if (stick.Y > threshold)
            return new KsCrewVehicleSteeringResult(rotation, forward * speed);

        if (stick.Y < -threshold)
            return new KsCrewVehicleSteeringResult(rotation, -forward * speed * drive.ReverseSpeedFraction);

        return new KsCrewVehicleSteeringResult(rotation, Vector2.Zero);
    }

    /// <summary>
    ///     How much of its turn rate the hull has, and which way round: 1 for tracks; for wheels its speed along its facing
    ///         over <see cref="KsCrewVehicleDriveComponent.FullTurnSpeed"/>, between -1 and 1, negative backing up.
    /// </summary>
    public static float TurnFactor(in KsCrewVehicleSteeringInput input, KsCrewVehicleDriveComponent drive)
    {
        if (drive.DriveTrain == KsCrewVehicleDriveTrain.Tracked || drive.FullTurnSpeed <= 0f)
            return 1f;

        var forwardSpeed = Vector2.Dot(input.Velocity, KsCrewVehicleHullFrame.Forward(input.HullRotation));
        return Math.Clamp(forwardSpeed / drive.FullTurnSpeed, -1f, 1f);
    }
}
