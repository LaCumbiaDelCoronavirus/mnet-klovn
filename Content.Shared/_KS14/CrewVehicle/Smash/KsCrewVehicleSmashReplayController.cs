using Content.Shared._KS14.CrewVehicle.Drive;
using Robust.Shared.Network;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Controllers;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.CrewVehicle.Smash;

/// <summary>
///     On the driver's client, replaying the ticks since the last server state: a smash predicted on one of them costs
///         the hull its speed on that tick again, to what it was the first time (<see cref="KsCrewVehicleSmashedSystem.PredictedSmashes"/>).
/// </summary>
public sealed partial class KsCrewVehicleSmashReplayController : VirtualController
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private INetManager _netManager = default!;
    [Dependency] private KsCrewVehicleSmashedSystem _smashedSystem = default!;

    [Dependency] private EntityQuery<PhysicsComponent> _physicsQuery = default!;

    public override void Initialize()
    {
        UpdatesAfter.Add(typeof(KsCrewVehicleGripController));

        base.Initialize();
    }

    public override void UpdateBeforeSolve(bool prediction, float frameTime)
    {
        base.UpdateBeforeSolve(prediction, frameTime);

        // The first time through, the smash itself does it.
        if (!_netManager.IsClient || _gameTiming.IsFirstTimePredicted)
            return;

        foreach (var smash in _smashedSystem.PredictedSmashes)
        {
            if (smash.Tick != _gameTiming.CurTick ||
                !_physicsQuery.TryComp(smash.Hull, out var physicsComponent) ||
                !physicsComponent.Predict)
            {
                continue;
            }

            PhysicsSystem.SetLinearVelocity(smash.Hull, smash.Velocity, body: physicsComponent);
        }
    }
}
