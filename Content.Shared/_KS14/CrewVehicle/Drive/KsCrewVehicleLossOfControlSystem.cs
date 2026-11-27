using System.Numerics;
using Content.Shared._KS14.Random.Helpers;
using Content.Shared.Random.Helpers;
using Content.Shared.Vehicle;
using Robust.Shared.Physics.Events;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.CrewVehicle.Drive;

/// <summary>
///     Rolls, once per incapacitation of the driver, whether the hull goes out of control or rolls to a stop, and steers
///         it while out of control (<see cref="KsCrewVehicleLossOfControlComponent"/>).
/// </summary>
/// <remarks>
///     The roll and the wandering are seeded from what both sides agree on, the tick and the entities' net ids, so the
///         driver's client predicts the same outcome and the same path as the server, and every replay of a tick alike.
/// </remarks>
public sealed partial class KsCrewVehicleLossOfControlSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;

    /// <summary>Tells the wander's direction roll from its turn-this-step roll on the same tick.</summary>
    private const int TurnSalt = 7;

    [SubscribeLocalEvent]
    private void OnDriverSteer(Entity<KsCrewVehicleLossOfControlComponent> hull, ref KsCrewVehicleDriverSteerEvent args)
    {
        if (!args.Incapacitated)
        {
            // Recovered: the next incapacitation rolls again.
            Reset(hull);
            return;
        }

        if (!hull.Comp.Rolled)
        {
            hull.Comp.Rolled = true;
            hull.Comp.OutOfControl = SharedRandomExtensions.PredictedProb(_gameTiming, hull.Comp.Chance,
                GetNetEntity(hull.Owner), GetNetEntity(args.Driver));
            // The last throttle held, at the speed it was held at: none if nothing was.
            hull.Comp.Throttle = args.HeldKeys.Y;
            Dirty(hull);
        }

        if (!hull.Comp.OutOfControl)
            return;

        args.Keys = new Vector2(Wander(hull) * MathF.Max(MathF.Abs(hull.Comp.Throttle), 1f), hull.Comp.Throttle);
    }

    /// <summary>
    ///     This step's steering: a way picked for each <see cref="KsCrewVehicleLossOfControlComponent.WanderInterval"/>,
    ///         turned on a share of the steps.
    /// </summary>
    private float Wander(Entity<KsCrewVehicleLossOfControlComponent> hull)
    {
        var netId = KsSharedRandomExtensions.GetNetId(hull.Owner, EntityManager);
        var tick = (int)_gameTiming.CurTick.Value;
        var intervalTicks = Math.Max(1, (int)(hull.Comp.WanderInterval.TotalSeconds * _gameTiming.TickRate));

        var way = KsSharedRandomExtensions.RandomWithHashCodeCombinedSeed(tick / intervalTicks, netId).Next(-1, 2);
        var turns = KsSharedRandomExtensions.RandomWithHashCodeCombinedSeed(tick, netId, TurnSalt).NextDouble() < hull.Comp.WanderStrength;
        return turns ? way : 0f;
    }

    /// <summary>
    ///     Hitting something hard ends it: the hull rolls to a stop from there.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnStartCollide(Entity<KsCrewVehicleLossOfControlComponent> hull, ref StartCollideEvent args)
    {
        if (!hull.Comp.OutOfControl || !args.OurFixture.Hard || !args.OtherFixture.Hard)
            return;

        hull.Comp.OutOfControl = false;
        Dirty(hull);
    }

    /// <summary>
    ///     Someone else at the wheel, or nobody: whatever happened, it is over.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnOperatorSet(Entity<KsCrewVehicleLossOfControlComponent> hull, ref VehicleOperatorSetEvent args)
    {
        Reset(hull);
    }

    private void Reset(Entity<KsCrewVehicleLossOfControlComponent> hull)
    {
        if (!hull.Comp.Rolled && !hull.Comp.OutOfControl)
            return;

        hull.Comp.Rolled = false;
        hull.Comp.OutOfControl = false;
        hull.Comp.Throttle = 0f;
        Dirty(hull);
    }
}
