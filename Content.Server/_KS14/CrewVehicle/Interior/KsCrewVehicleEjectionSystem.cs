using System.Numerics;
using Content.Shared._KS14.CrewVehicle.Interior;
using Content.Shared.Buckle;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Stunnable;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;

namespace Content.Server._KS14.CrewVehicle.Interior;

/// <summary>
///     Puts what is inside a crew vehicle back in the world beside it: everything, when the vehicle is destroyed or
///         deleted, before its interior map goes; or one thing that has left the interior grid.
/// </summary>
/// <remarks>
///     Deletion happens inside the engine's own deletion, and destruction (which ends in one) can come from inside a
///         physics callback, a projectile's hit; so the vehicle is only queued, and <see cref="Update"/> does the moving.
/// </remarks>
public sealed partial class KsCrewVehicleEjectionSystem : EntitySystem
{
    [Dependency] private KsCrewVehicleInteriorSystem _interiorSystem = default!;
    [Dependency] private DamageableSystem _damageableSystem = default!;
    [Dependency] private SharedBuckleSystem _buckleSystem = default!;
    [Dependency] private SharedContainerSystem _containerSystem = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private SharedStunSystem _stunSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private EntityQuery<MapGridComponent> _gridQuery = default!;
    [Dependency] private EntityQuery<PhysicsComponent> _physicsQuery = default!;
    [Dependency] private EntityQuery<KsCrewVehicleInteriorPartComponent> _interiorPartQuery = default!;

    /// <summary>
    ///     Where, around the vehicle, things are put: rings of eight, nearest first.
    /// </summary>
    private static readonly Vector2[] LandingOffsets = CreateLandingOffsets();

    /// <summary>
    ///     A vehicle's interior emptied all at once, and how hard it throws mobs out.
    /// </summary>
    private readonly record struct PendingEjection(
        EntityUid Vehicle,
        EntityUid InteriorMap,
        MapCoordinates Centre,
        DamageSpecifier? Damage,
        TimeSpan Stun);

    private readonly List<PendingEjection> _pending = new();

    /// <summary>
    ///     What <see cref="Update"/> works through, as ejecting can queue more.
    /// </summary>
    private readonly List<PendingEjection> _draining = new();

    private readonly List<EntityUid> _toEject = new();

    private static Vector2[] CreateLandingOffsets()
    {
        var offsets = new List<Vector2>();
        foreach (var radius in new[] { 1.5f, 2.5f, 3.5f })
        {
            for (var i = 0; i < 8; i++)
            {
                offsets.Add(Angle.FromDegrees(45 * i).RotateVec(new Vector2(0f, radius)));
            }
        }

        return offsets.ToArray();
    }

    /// <summary>
    ///     Deleted: destroyed (destruction always ends in deletion), or deleted outright by an admin or the round
    ///         ending. Everything inside goes out beside the hull's last position.
    /// </summary>
    /// <remarks>
    ///     On termination rather than shutdown: by the time a deleted entity's components shut down, it has been taken
    ///         off its map, and the place to put everything would be lost.
    /// </remarks>
    [SubscribeLocalEvent]
    private void OnTerminating(Entity<KsCrewVehicleInteriorComponent> entity, ref EntityTerminatingEvent args)
    {
        QueueEjectAll(entity);
    }

    /// <summary>
    ///     The component alone removed: the vehicle stays where it is, and stops having an inside.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnShutdown(Entity<KsCrewVehicleInteriorComponent> entity, ref ComponentShutdown args)
    {
        if (!Terminating(entity.Owner))
            QueueEjectAll(entity);
    }

    private void QueueEjectAll(Entity<KsCrewVehicleInteriorComponent> vehicle)
    {
        // Both destruction and the deletion that follows it come here; the first takes the interior away.
        if (_interiorSystem.DetachInterior(vehicle) is not { } interiorMap)
            return;

        _pending.Add(new PendingEjection(vehicle,
            interiorMap,
            _transformSystem.GetMapCoordinates(vehicle.Owner),
            vehicle.Comp.EjectDamage,
            vehicle.Comp.EjectStun));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_pending.Count == 0)
            return;

        _draining.Clear();
        _draining.AddRange(_pending);
        _pending.Clear();

        foreach (var ejection in _draining)
        {
            EjectAll(ejection);
        }

        _draining.Clear();
    }

    private void EjectAll(PendingEjection ejection)
    {
        if (TerminatingOrDeleted(ejection.InteriorMap))
            return;

        // Nowhere to put anything when the vehicle's own map is going too: everything goes with the interior.
        if (_mapSystem.GetMapOrInvalid(ejection.Centre.MapId) is { Valid: true } outsideMap && !TerminatingOrDeleted(outsideMap))
        {
            _toEject.Clear();
            CollectEjected(ejection.InteriorMap, directlyOnTheFloor: true);

            var index = 0;
            foreach (var uid in _toEject)
            {
                if (TerminatingOrDeleted(uid))
                    continue;

                Eject(uid, ejection.Centre, index++);
                if (!_interiorSystem.CanBeOccupant(uid))
                    continue;

                if (ejection.Damage is { } damage)
                    _damageableSystem.TryChangeDamage(uid, damage, origin: ejection.Vehicle);

                if (ejection.Stun > TimeSpan.Zero)
                    _stunSystem.TryKnockdown(uid, ejection.Stun);
            }

            _toEject.Clear();
        }

        QueueDel(ejection.InteriorMap);
    }

    /// <summary>
    ///     Everyone, buckled or shut in furniture or not, and every loose thing on the floor or off it. Furniture
    ///         stays, to go with the map, and so does what it holds that is not a mob.
    /// </summary>
    private void CollectEjected(EntityUid parentUid, bool directlyOnTheFloor)
    {
        var children = Transform(parentUid).ChildEnumerator;
        while (children.MoveNext(out var childUid))
        {
            if (_gridQuery.HasComp(childUid))
            {
                CollectEjected(childUid, directlyOnTheFloor: true);
                continue;
            }

            // What is ejected takes what it carries along with it.
            if (_interiorSystem.CanBeOccupant(childUid) || directlyOnTheFloor && IsLoose(childUid))
            {
                _toEject.Add(childUid);
                continue;
            }

            CollectEjected(childUid, directlyOnTheFloor: false);
        }
    }

    /// <summary>
    ///     Whether <paramref name="uid"/> is something lying about, rather than part of the interior
    ///         (<see cref="KsCrewVehicleInteriorPartComponent"/>).
    /// </summary>
    public bool IsLoose(EntityUid uid)
    {
        // A seat or the ammo loader is unanchored to sit where the art has it, but is the interior's own.
        return !Transform(uid).Anchored && _physicsQuery.HasComp(uid) && !_interiorPartQuery.HasComp(uid);
    }

    /// <summary>
    ///     Puts <paramref name="uid"/> beside the vehicle, out of anything it is buckled to or shut in.
    /// </summary>
    public void EjectBesideVehicle(EntityUid uid, EntityUid vehicleUid)
    {
        if (TerminatingOrDeleted(vehicleUid))
            return;

        Eject(uid, _transformSystem.GetMapCoordinates(vehicleUid), 0);
    }

    private void Eject(EntityUid uid, MapCoordinates centre, int index)
    {
        _interiorSystem.UntrackOccupant(uid);
        _buckleSystem.Unbuckle(uid, null);
        _containerSystem.TryRemoveFromContainer(uid, force: true);
        _transformSystem.SetMapCoordinates(uid, FindLanding(centre, index));
    }

    /// <summary>
    ///     The <paramref name="index"/>th free place around <paramref name="centre"/>, so that what is thrown out at
    ///         once is scattered; the centre itself if there is none.
    /// </summary>
    private MapCoordinates FindLanding(MapCoordinates centre, int index)
    {
        for (var i = 0; i < LandingOffsets.Length; i++)
        {
            var candidate = centre.Offset(LandingOffsets[(index + i) % LandingOffsets.Length]);
            if (!_interiorSystem.IsBlockedForMobs(candidate))
                return candidate;
        }

        return centre;
    }
}
