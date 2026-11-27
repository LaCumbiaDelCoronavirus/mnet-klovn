using Content.Shared._KS14.CrewVehicle.Engine;
using Content.Shared._KS14.CrewVehicle.Interior;
using Robust.Client.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;

namespace Content.Client._KS14.CrewVehicle.Engine;

/// <summary>
///     The engine's idle loop, played by each client for itself from the engine's state: on the hull while it runs, and,
///         for whoever is on that vehicle's interior map, quieter and everywhere inside, since sound doesn't cross maps.
/// </summary>
/// <remarks>
///     Not the stock ambient sound, which plays with parameters of its own and so can't take the engine's pitch.
/// </remarks>
public sealed partial class KsCrewVehicleEngineAudioSystem : EntitySystem
{
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private SharedAudioSystem _audioSystem = default!;
    [Dependency] private SharedKsCrewVehicleInteriorSystem _interiorSystem = default!;

    [Dependency] private EntityQuery<KsCrewVehicleEngineComponent> _engineQuery = default!;

    /// <summary>The interior loop, for the local player, and the map it is for.</summary>
    private EntityUid? _interiorStream;
    private EntityUid? _interiorStreamMap;
    private EntityUid? _localMap;

    [SubscribeLocalEvent]
    private void OnStateChanged(Entity<KsCrewVehicleEngineComponent> vehicle, ref KsCrewVehicleEngineStateChangedEvent args)
    {
        Refresh(vehicle);
    }

    [SubscribeLocalEvent]
    private void OnAfterState(Entity<KsCrewVehicleEngineComponent> vehicle, ref AfterAutoHandleStateEvent args)
    {
        Refresh(vehicle);
    }

    [SubscribeLocalEvent]
    private void OnShutdown(Entity<KsCrewVehicleEngineComponent> vehicle, ref ComponentShutdown args)
    {
        if (TryComp<KsCrewVehicleEngineAudioComponent>(vehicle.Owner, out var audioComponent))
            audioComponent.Outside = _audioSystem.Stop(audioComponent.Outside);
        RefreshInterior();
    }

    private void Refresh(Entity<KsCrewVehicleEngineComponent> vehicle)
    {
        var running = vehicle.Comp.State == KsCrewVehicleEngineState.Running;
        var audioComponent = EnsureComp<KsCrewVehicleEngineAudioComponent>(vehicle.Owner);
        if (running && audioComponent.Outside == null)
        {
            var audioParams = (vehicle.Comp.IdleSound?.Params ?? AudioParams.Default)
                .WithLoop(true)
                .WithPitchScale(vehicle.Comp.Pitch)
                .WithVolume(vehicle.Comp.IdleVolume)
                .WithMaxDistance(vehicle.Comp.IdleRange);
            audioComponent.Outside = _audioSystem.PlayEntity(vehicle.Comp.IdleSound, Filter.Local(), vehicle.Owner, false, audioParams)?.Entity;
        }
        else if (!running)
        {
            audioComponent.Outside = _audioSystem.Stop(audioComponent.Outside);
        }

        RefreshInterior();
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        // One comparison a frame, for the local player only: whether they have moved in or out of an interior.
        var map = _playerManager.LocalEntity is { } local ? Transform(local).MapUid : null;
        if (map != _localMap)
            RefreshInterior();
    }

    private void RefreshInterior()
    {
        _localMap = _playerManager.LocalEntity is { } local ? Transform(local).MapUid : null;
        Entity<KsCrewVehicleEngineComponent>? running = null;
        if (_interiorSystem.TryGetInteriorMap(_localMap, out var interiorMap) &&
            _engineQuery.TryComp(interiorMap.Value.Comp.Vehicle, out var engineComponent) &&
            engineComponent.State == KsCrewVehicleEngineState.Running)
        {
            running = (interiorMap.Value.Comp.Vehicle, engineComponent);
        }

        if (running == null || _interiorStreamMap != _localMap)
        {
            _interiorStream = _audioSystem.Stop(_interiorStream);
            _interiorStreamMap = null;
        }

        if (running is not { } vehicle || _interiorStream != null)
            return;

        var audioParams = (vehicle.Comp.IdleSound?.Params ?? AudioParams.Default)
            .WithLoop(true)
            .WithPitchScale(vehicle.Comp.Pitch)
            .WithVolume(vehicle.Comp.InteriorIdleVolume);
        _interiorStream = _audioSystem.PlayGlobal(vehicle.Comp.IdleSound, Filter.Local(), false, audioParams)?.Entity;
        _interiorStreamMap = _localMap;
    }
}

/// <summary>
///     The engine's idle loop this client plays on the hull. Client only.
/// </summary>
[RegisterComponent]
public sealed partial class KsCrewVehicleEngineAudioComponent : Component
{
    [ViewVariables]
    public EntityUid? Outside;
}
