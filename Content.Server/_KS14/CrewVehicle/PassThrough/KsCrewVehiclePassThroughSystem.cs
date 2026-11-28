using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Shared._KS14.CrewVehicle;
using Content.Shared._KS14.CrewVehicle.Interior;
using Content.Shared._KS14.CrewVehicle.PassThrough;
using Content.Shared._KS14.Projectiles;
using Content.Shared._KS14.Vehicle;
using Content.Shared._Trauma.Projectiles;
using Content.Shared.Damage;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server._KS14.CrewVehicle.PassThrough;

/// <summary>
///     Carries rounds that went through a crew vehicle's hull into its interior, and out of the far side if they can still
///         go on.
/// </summary>
/// <remarks>
///     <para>
///         Each leg is a fresh entity, not the round moved: the server's copy of a player's shot is hidden from that
///             player, matched to their client's kept copy, and a moved one would stay hidden; and a fresh one leaves the
///             old map's contacts behind. Systems copy what they stamped on the round (<see cref="KsProjectileCarriedEvent"/>).
///     </para>
///     <para>
///         Entry is queued, from a hit that may be in a physics callback or the look-ahead's walk over every round, and
///             carried out after physics the same tick: the round, queued for deletion by its hit, is still there to copy.
///     </para>
/// </remarks>
public sealed partial class KsCrewVehiclePassThroughSystem : SharedKsCrewVehiclePassThroughSystem
{
    [Dependency] private KsRiderProjectileSystem _riderProjectileSystem = default!;
    [Dependency] private PredictedProjectileSystem _predictedProjectileSystem = default!;
    [Dependency] private SharedGunSystem _gunSystem = default!;
    [Dependency] private SharedPhysicsSystem _physicsSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private EntityQuery<FixturesComponent> _fixturesQuery = default!;
    [Dependency] private EntityQuery<MapGridComponent> _gridQuery = default!;
    [Dependency] private EntityQuery<PhysicsComponent> _physicsQuery = default!;
    [Dependency] private EntityQuery<ProjectileComponent> _projectileQuery = default!;
    [Dependency] private EntityQuery<KsCrewVehicleInteriorPartComponent> _interiorPartQuery = default!;

    /// <summary>
    ///     How far inside the cabin's outline a round coming in is put, along its way.
    /// </summary>
    private const float EntryInset = 0.05f;

    /// <summary>
    ///     How far outside the hull's body a round coming out is put, along its way.
    /// </summary>
    private const float ExitMargin = 0.05f;

    private readonly record struct PendingEntry(EntityUid Round, EntityUid Hull, Vector2 Velocity, MapCoordinates Point);

    private readonly List<PendingEntry> _pending = new();

    /// <summary>
    ///     What <see cref="Update"/> works through, as carrying a round can queue another.
    /// </summary>
    private readonly List<PendingEntry> _draining = new();

    private readonly List<EntityUid> _leaving = new();

    public override void Initialize()
    {
        base.Initialize();

        // After the step that brought the round to the hull, in the tick it hit it: it is deleted at the end of it.
        UpdatesAfter.Add(typeof(SharedPhysicsSystem));
    }

    protected override void QueueEntry(Entity<KsCrewVehicleInteriorComponent> hull, KsProjectilePenetrateAttemptEvent args)
    {
        _pending.Add(new PendingEntry(args.Projectile, hull.Owner, args.WorldVelocity, args.Point));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_pending.Count > 0)
        {
            _draining.Clear();
            _draining.AddRange(_pending);
            _pending.Clear();

            foreach (var entry in _draining)
            {
                Enter(entry);
            }

            _draining.Clear();
        }

        // Few: only what is flying through a cabin.
        _leaving.Clear();
        var query = EntityQueryEnumerator<KsCrewVehiclePassThroughRoundComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var roundComponent, out var transformComponent))
        {
            if (roundComponent.LifeStage <= ComponentLifeStage.Running && IsOutside((uid, roundComponent), transformComponent))
                _leaving.Add(uid);
        }

        foreach (var uid in _leaving)
        {
            Leave(uid);
        }

        _leaving.Clear();
    }

    private bool IsOutside(Entity<KsCrewVehiclePassThroughRoundComponent> round, TransformComponent transformComponent)
    {
        if (TerminatingOrDeleted(round.Comp.Grid))
            return false;

        var local = Vector2.Transform(_transformSystem.GetWorldPosition(transformComponent), _transformSystem.GetInvWorldMatrix(round.Comp.Grid));
        return !round.Comp.Bounds.Contains(local);
    }

    /// <summary>
    ///     The vehicle's loaded interior grid, if it has been loaded; never loads it.
    /// </summary>
    private bool TryGetInteriorGrid(Entity<KsCrewVehicleInteriorComponent> hull, [NotNullWhen(true)] out EntityUid? gridUid)
    {
        gridUid = null;
        if (hull.Comp.InteriorMap is not { } mapUid || TerminatingOrDeleted(mapUid))
            return false;

        var children = Transform(mapUid).ChildEnumerator;
        while (children.MoveNext(out var childUid))
        {
            if (!_gridQuery.HasComp(childUid))
                continue;

            gridUid = childUid;
            return true;
        }

        return false;
    }

    /// <summary>
    ///     The cabin's outline on its grid: the vehicle's own, or every tile and every solid piece of the interior.
    /// </summary>
    private Box2 GetCabinBounds(Entity<KsCrewVehicleInteriorComponent> hull, Entity<MapGridComponent> grid)
    {
        if (hull.Comp.PassThroughBounds is { } bounds)
            return bounds;

        var cabin = grid.Comp.LocalAABB;
        var children = Transform(grid).ChildEnumerator;
        while (children.MoveNext(out var childUid))
        {
            if (!_interiorPartQuery.HasComp(childUid) || !_fixturesQuery.TryComp(childUid, out var fixturesComponent))
                continue;

            var childTransform = Transform(childUid);
            var transform = new Robust.Shared.Physics.Transform(childTransform.LocalPosition, (float)childTransform.LocalRotation.Theta);
            foreach (var fixture in fixturesComponent.Fixtures.Values)
            {
                if (fixture.Hard)
                    cabin = cabin.Union(fixture.Shape.ComputeAABB(transform, 0));
            }
        }

        return cabin;
    }

    private void Enter(PendingEntry entry)
    {
        var (roundUid, hullUid, velocity, point) = entry;
        if (TerminatingOrDeleted(roundUid) || !_projectileQuery.HasComp(roundUid))
            return;

        // Whatever happens to it next, it goes on no further here.
        QueueDel(roundUid);

        var speed = velocity.Length();
        if (TerminatingOrDeleted(hullUid) ||
            EntityManager.IsQueuedForDeletion(hullUid) ||
            !TryComp<KsCrewVehicleInteriorComponent>(hullUid, out var interiorComponent) ||
            speed < 0.001f ||
            !TryGetHullBox(hullUid, out var hullBox) ||
            point.MapId != Transform(hullUid).MapID)
            return;

        Entity<KsCrewVehicleInteriorComponent> hull = (hullUid, interiorComponent);
        var (hullPosition, hullRotation) = _transformSystem.GetWorldPositionRotation(hullUid);
        var hullDirection = (-hullRotation).RotateVec(velocity / speed);
        var hullEntry = KsCrewVehiclePassThroughFrame.GetEntry(hullBox,
            KsCrewVehicleHullFrame.WorldToHull(hullPosition, hullRotation, point.Position),
            hullDirection);

        if (!TryGetInteriorGrid(hull, out var gridUid) || !_gridQuery.TryComp(gridUid, out var gridComponent))
        {
            // Nobody has been inside: an empty cabin, which it crosses at once to the far side.
            PassEmptyCabin(roundUid, hull, hullBox, hullEntry, hullDirection, speed);
            return;
        }

        // Where it reaches the crew compartment, past the armour or machinery between it and the hull's outline.
        var compartment = GetCompartment(interiorComponent, hullBox);
        var compartmentEntry = KsCrewVehiclePassThroughFrame.GetEntry(compartment, hullEntry, hullDirection);
        var cabin = GetCabinBounds(hull, (gridUid.Value, gridComponent));
        var direction = KsCrewVehiclePassThroughFrame.HullDirectionToInterior(compartment, cabin, hullDirection);
        var inside = KsCrewVehiclePassThroughFrame.HullToInterior(compartment, cabin, compartmentEntry) + direction * EntryInset;
        inside = KsCrewVehiclePassThroughFrame.Clamp(cabin, inside, inset: 0.01f);

        var (gridPosition, gridRotation) = _transformSystem.GetWorldPositionRotation(gridUid.Value);
        var mapId = Transform(gridUid.Value).MapID;
        var copyUid = SpawnCopy(roundUid,
            new MapCoordinates(gridPosition + gridRotation.RotateVec(inside), mapId),
            gridRotation.RotateVec(direction),
            speed);
        if (copyUid != null)
            MarkRound(copyUid.Value, hullUid, gridUid.Value, cabin);
    }

    /// <summary>
    ///     A round crossing a cabin that was never loaded: put at the far side of the hull, outside, and charged for going
    ///         out through it there.
    /// </summary>
    private void PassEmptyCabin(EntityUid roundUid,
        Entity<KsCrewVehicleInteriorComponent> hull,
        Box2 hullBox,
        Vector2 hullEntry,
        Vector2 hullDirection,
        float speed)
    {
        var hullExit = KsCrewVehiclePassThroughFrame.GetExit(hullBox, hullEntry, hullDirection);
        if (SpawnOutside(roundUid, GetHullPlace(hull.Owner), hullExit, hullDirection, speed) is not { } copyUid)
            return;

        // Marked as leaving this hull for the hit, so the hull lets it go on rather than taking it in again.
        MarkRound(copyUid, hull.Owner, EntityUid.Invalid, default);
        _predictedProjectileSystem.DoHit(copyUid, hull.Owner);
        RemComp<KsCrewVehiclePassThroughRoundComponent>(copyUid);
        DeleteIfSpent(copyUid);
    }

    /// <summary>
    ///     A round that reached the far side of the cabin hits the hull from inside: the exit hole. If it goes through, it
    ///         comes out of the far side of the hull.
    /// </summary>
    private void Leave(EntityUid roundUid)
    {
        if (TerminatingOrDeleted(roundUid) ||
            EntityManager.IsQueuedForDeletion(roundUid) ||
            !TryComp<KsCrewVehiclePassThroughRoundComponent>(roundUid, out var roundComponent) ||
            !_physicsQuery.TryComp(roundUid, out var physicsComponent))
            return;

        var hullUid = roundComponent.Vehicle;
        var gridUid = roundComponent.Grid;
        var cabin = roundComponent.Bounds;
        var velocity = _physicsSystem.GetMapLinearVelocity(roundUid, physicsComponent);
        var speed = velocity.Length();

        // Its vehicle is going, and the interior with it, or it has stopped somehow: it ends inside.
        if (TerminatingOrDeleted(hullUid) ||
            EntityManager.IsQueuedForDeletion(hullUid) ||
            !TryComp<KsCrewVehicleInteriorComponent>(hullUid, out var interiorComponent) ||
            TerminatingOrDeleted(gridUid) ||
            speed < 0.001f ||
            !TryGetHullBox(hullUid, out var hullBox))
        {
            QueueDel(roundUid);
            return;
        }

        var (gridPosition, gridRotation) = _transformSystem.GetWorldPositionRotation(gridUid);
        var local = (-gridRotation).RotateVec(_transformSystem.GetWorldPosition(roundUid) - gridPosition);
        var localDirection = (-gridRotation).RotateVec(velocity / speed);
        var leftAt = KsCrewVehiclePassThroughFrame.GetExit(cabin, local, localDirection);

        // Where the hull is before the exit hole is made in it, which may destroy it.
        var hullPlace = GetHullPlace(hullUid);
        _predictedProjectileSystem.DoHit(roundUid, hullUid);
        if (TerminatingOrDeleted(roundUid) ||
            EntityManager.IsQueuedForDeletion(roundUid) ||
            !_projectileQuery.TryComp(roundUid, out var projectileComponent) ||
            projectileComponent.ProjectileSpent)
        {
            DeleteIfSpent(roundUid);
            return;
        }

        var compartment = GetCompartment(interiorComponent, hullBox);
        var hullPoint = KsCrewVehiclePassThroughFrame.InteriorToHull(compartment, cabin, leftAt);
        var hullDirection = KsCrewVehiclePassThroughFrame.InteriorDirectionToHull(compartment, cabin, localDirection);
        var hullExit = KsCrewVehiclePassThroughFrame.GetExit(hullBox, hullPoint, hullDirection);
        SpawnOutside(roundUid, hullPlace, hullExit, hullDirection, speed);
        QueueDel(roundUid);
    }

    /// <summary>
    ///     A copy of <paramref name="roundUid"/> just outside the hull at <paramref name="hullExit"/>, on its outline,
    ///         flying <paramref name="hullDirection"/> (hull-local), that passes through the hull it comes out of.
    /// </summary>
    private EntityUid? SpawnOutside(EntityUid roundUid,
        HullPlace hull,
        Vector2 hullExit,
        Vector2 hullDirection,
        float speed)
    {
        var outside = KsCrewVehicleHullFrame.HullToWorld(hull.Position, hull.Rotation, hullExit + hullDirection * ExitMargin);
        var copyUid = SpawnCopy(roundUid,
            new MapCoordinates(outside, hull.MapId),
            hull.Rotation.RotateVec(hullDirection),
            speed);
        if (copyUid != null)
            _riderProjectileSystem.SetVehicle(copyUid.Value, hull.Uid);

        return copyUid;
    }

    /// <summary>
    ///     Where a hull stands, kept across a hit that may destroy it.
    /// </summary>
    private readonly record struct HullPlace(EntityUid Uid, Vector2 Position, Angle Rotation, MapId MapId);

    private HullPlace GetHullPlace(EntityUid hullUid)
    {
        var (position, rotation) = _transformSystem.GetWorldPositionRotation(hullUid);
        return new HullPlace(hullUid, position, rotation, Transform(hullUid).MapID);
    }

    /// <summary>
    ///     A fresh round like <paramref name="originalUid"/>, at <paramref name="coordinates"/>, flying
    ///         <paramref name="direction"/> at <paramref name="speed"/>, with what it carried: its damage, shooter and
    ///         weapon, what it has left to go through, and whatever other systems stamped on it.
    /// </summary>
    private EntityUid? SpawnCopy(EntityUid originalUid, MapCoordinates coordinates, Vector2 direction, float speed)
    {
        if (MetaData(originalUid).EntityPrototype is not { } prototype ||
            !_projectileQuery.TryComp(originalUid, out var originalComponent))
            return null;

        var copyUid = Spawn(prototype.ID, coordinates);
        var copyComponent = EnsureComp<ProjectileComponent>(copyUid);
        copyComponent.Damage = new DamageSpecifier(originalComponent.Damage);
        copyComponent.IgnoreResistances = originalComponent.IgnoreResistances;
        copyComponent.IgnoreShooter = originalComponent.IgnoreShooter;
        copyComponent.DeleteOnCollide = originalComponent.DeleteOnCollide;
        copyComponent.PenetrationThreshold = originalComponent.PenetrationThreshold;
        copyComponent.PenetrationAmount = originalComponent.PenetrationAmount;
        copyComponent.PenetrationDamageTypeRequirement = originalComponent.PenetrationDamageTypeRequirement == null
            ? null
            : new List<string>(originalComponent.PenetrationDamageTypeRequirement);

        // No user: nobody fired the copy, so nothing that stamps a player's shot (lag compensation, credit) runs again.
        _gunSystem.ShootProjectile(copyUid, direction, Vector2.Zero, originalComponent.Weapon, user: null, speed: speed);
        copyComponent.Shooter = originalComponent.Shooter;
        Dirty(copyUid, copyComponent);

        var ev = new KsProjectileCarriedEvent(originalUid, copyUid);
        RaiseLocalEvent(ref ev);
        return copyUid;
    }

    private void DeleteIfSpent(EntityUid roundUid)
    {
        if (!TerminatingOrDeleted(roundUid) &&
            _projectileQuery.TryComp(roundUid, out var projectileComponent) &&
            projectileComponent.ProjectileSpent)
            QueueDel(roundUid);
    }
}
