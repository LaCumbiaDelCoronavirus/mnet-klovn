using System.Numerics;
using Content.Server.Chat.Systems;
using Content.Shared._KS14.CrewVehicle;
using Content.Shared._KS14.CrewVehicle.Interior;
using Content.Shared.Ghost;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Server._KS14.CrewVehicle.Chat;

/// <summary>
///     Speech carries through a crew vehicle's hull: someone talking inside is heard by those around the vehicle
///         outside, and someone talking near a vehicle is heard by those inside it, as if the interior were laid over
///         the hull where it stands.
/// </summary>
/// <remarks>
///     <para>
///         Distances are measured in the outside world, from where each interior point sits over it
///             (<see cref="KsCrewVehicleInteriorTransform"/>), so a whisper's clear and muffled ranges work across the
///             hull as they do anywhere. Whispers heard across it never show the whisperer's name: the chat system's
///             line-of-sight check cannot see from one map to another.
///     </para>
///     <para>
///         Spoken voice (<c>KsVoiceSystem</c>) does not raise <see cref="ExpandICChatRecipientsEvent"/> and is not
///             carried.
///     </para>
/// </remarks>
public sealed partial class KsCrewVehicleChatRelaySystem : EntitySystem
{
    [Dependency] private EntityLookupSystem _entityLookupSystem = default!;
    [Dependency] private SharedKsCrewVehicleInteriorSystem _interiorSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private EntityQuery<GhostHearingComponent> _ghostHearingQuery = default!;

    private readonly HashSet<Entity<ActorComponent>> _nearbyListeners = new();
    private readonly HashSet<Entity<KsCrewVehicleInteriorComponent>> _nearbyVehicles = new();

    // ExpandICChatRecipientsEvent is a by-value class event.
    [SubscribeLocalEvent]
    private void OnExpandRecipients(ExpandICChatRecipientsEvent ev)
    {
        var speakerTransform = Transform(ev.Source);
        EntityUid? speakersVehicle = null;
        MapCoordinates speakerPosition;

        if (_interiorSystem.TryGetVehicle(ev.Source, out var vehicle))
        {
            // Inside: heard where the speaker sits over the world, by those around the hull.
            speakersVehicle = vehicle.Value.Owner;
            speakerPosition = InteriorToWorld(vehicle.Value, _transformSystem.GetWorldPosition(speakerTransform));
            AddOutsideListeners(ev, speakerPosition);
        }
        else
        {
            speakerPosition = _transformSystem.GetMapCoordinates(ev.Source, speakerTransform);
        }

        if (speakerPosition.MapId == MapId.Nullspace)
            return;

        // Then by those inside any other vehicle near enough. A component lookup in voice range: it costs what is
        //      within those few tiles, not the number of vehicles or players in the round.
        _nearbyVehicles.Clear();
        _entityLookupSystem.GetEntitiesInRange(speakerPosition, ev.VoiceRange, _nearbyVehicles);
        foreach (var nearby in _nearbyVehicles)
        {
            if (nearby.Owner != speakersVehicle)
                AddOccupantListeners(ev, nearby, speakerPosition);
        }

        _nearbyVehicles.Clear();
    }

    private void AddOutsideListeners(ExpandICChatRecipientsEvent ev, MapCoordinates speakerPosition)
    {
        if (speakerPosition.MapId == MapId.Nullspace)
            return;

        _nearbyListeners.Clear();
        _entityLookupSystem.GetEntitiesInRange(speakerPosition, ev.VoiceRange, _nearbyListeners);
        foreach (var listener in _nearbyListeners)
        {
            var distance = Vector2.Distance(_transformSystem.GetWorldPosition(listener.Owner), speakerPosition.Position);
            AddRecipient(ev, listener, distance);
        }

        _nearbyListeners.Clear();
    }

    private void AddOccupantListeners(ExpandICChatRecipientsEvent ev,
        Entity<KsCrewVehicleInteriorComponent> vehicle,
        MapCoordinates speakerPosition)
    {
        if (!_interiorSystem.TryGetInteriorMap(vehicle.Comp.InteriorMap, out var interiorMap))
            return;

        var query = EntityQueryEnumerator<KsCrewVehicleOccupantComponent, ActorComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var occupantComponent, out var actorComponent, out var transform))
        {
            if (occupantComponent.Vehicle != vehicle.Owner || transform.MapUid != interiorMap.Value.Owner)
                continue;

            var listenerPosition = InteriorToWorld(vehicle, _transformSystem.GetWorldPosition(transform));
            AddRecipient(ev, (uid, actorComponent), Vector2.Distance(listenerPosition.Position, speakerPosition.Position));
        }
    }

    private void AddRecipient(ExpandICChatRecipientsEvent ev, Entity<ActorComponent> listener, float distance)
    {
        if (distance > ev.VoiceRange)
            return;

        ev.Recipients.TryAdd(listener.Comp.PlayerSession,
            new ChatSystem.ICChatRecipientData(distance, _ghostHearingQuery.HasComp(listener)));
    }

    /// <summary>
    ///     Where a point of the vehicle's interior map sits over the world outside. Interiors are loaded at their map's
    ///         origin, unrotated, so a point's world position on the interior map is its interior-grid position.
    /// </summary>
    private MapCoordinates InteriorToWorld(Entity<KsCrewVehicleInteriorComponent> vehicle, Vector2 interiorPoint)
    {
        var (hullPosition, hullRotation) = _transformSystem.GetWorldPositionRotation(vehicle.Owner);
        // The interior's grid is loaded unrotated at the map's origin; the cabin on it may face any way.
        var world = KsCrewVehicleInteriorTransform.InteriorToWorld(interiorPoint,
            vehicle.Comp.InteriorAnchor,
            hullPosition,
            hullRotation,
            interiorRotation: KsCrewVehicleHullFrame.GetCabinRotation(vehicle.Comp.CabinFront));
        return new MapCoordinates(world, Transform(vehicle.Owner).MapID);
    }
}
