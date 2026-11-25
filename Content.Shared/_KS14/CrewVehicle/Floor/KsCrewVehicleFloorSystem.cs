using System.Numerics;
using Content.Shared._KS14.CrewVehicle.Drive;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;

namespace Content.Shared._KS14.CrewVehicle.Floor;

/// <summary>
///     Keeps a crew vehicle on the floor: a hull that doesn't <see cref="KsCrewVehicleDriveComponent.DriveOffGrid"/> stops
///         at the edge of the floor as at a wall, whether driven, turned, pushed or coasting.
/// </summary>
/// <remarks>
///     <para>
///         Floor is a tile that isn't open to space: on the hull's own grid, on any other grid there (a seam between two
///             grids is floor), or on a planet map's own tiles. Lattice and space aren't.
///     </para>
///     <para>
///         The rule is that a move may not put more of the hull's rim over no floor: a hull on the floor never leaves it,
///             one left half off by other means can only get back on, and one whose centre is off can't drive at all.
///             Turns are checked by the drive system, steps by <see cref="KsCrewVehicleFloorController"/>. Tiles are
///             networked grid state, so both sides agree and it is predicted.
///     </para>
/// </remarks>
public sealed partial class KsCrewVehicleFloorSystem : EntitySystem
{
    [Dependency] private KsCrewVehicleFloorTileSystem _floorTileSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    [Dependency] private EntityQuery<FixturesComponent> _fixturesQuery = default!;
    [Dependency] private EntityQuery<PhysicsComponent> _physicsQuery = default!;

    /// <summary>
    ///     The most room between two of the rim's points, in tiles: less than a tile, so no gap goes between them.
    /// </summary>
    private const float SampleSpacing = 0.5f;

    /// <summary>
    ///     How far inside the rim the points sit, so that a rim lying exactly on a tile's edge isn't read off that edge.
    /// </summary>
    private const float SampleInset = 0.02f;

    /// <summary>
    ///     A hull is marked rolling while its body is awake, the only time it can move.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnWake(Entity<KsCrewVehicleDriveComponent> hull, ref PhysicsWakeEvent args)
    {
        // Not before its map starts: nothing moves on a map not yet running, and spawning there must change nothing.
        if (MetaData(hull.Owner).EntityLifeStage >= EntityLifeStage.MapInitialized)
            EnsureComp<KsCrewVehicleRollingComponent>(hull.Owner);
    }

    /// <summary>
    ///     A hull awake when its map starts, as a body is when it is spawned, is rolling from then.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnMapInit(Entity<KsCrewVehicleDriveComponent> hull, ref MapInitEvent args)
    {
        if (_physicsQuery.CompOrNull(hull.Owner)?.Awake == true)
            EnsureComp<KsCrewVehicleRollingComponent>(hull.Owner);
    }

    [SubscribeLocalEvent]
    private void OnSleep(Entity<KsCrewVehicleDriveComponent> hull, ref PhysicsSleepEvent args)
    {
        // Deferred: it falls asleep inside the physics step.
        RemCompDeferred<KsCrewVehicleRollingComponent>(hull.Owner);
    }

    /// <summary>
    ///     Whether <paramref name="hull"/> is held to the floor at all.
    /// </summary>
    public bool KeepsToFloor(Entity<KsCrewVehicleDriveComponent> hull)
    {
        return !hull.Comp.DriveOffGrid;
    }

    /// <summary>
    ///     How many of the hull's rim points would be over no floor with the hull at <paramref name="worldPosition"/>,
    ///         turned to <paramref name="worldRotation"/>.
    /// </summary>
    public int OffFloorCount(Entity<KsCrewVehicleDriveComponent> hull, Vector2 worldPosition, Angle worldRotation)
    {
        return OffFloorCount(hull, worldPosition, worldRotation, out _);
    }

    /// <inheritdoc cref="OffFloorCount(Entity{KsCrewVehicleDriveComponent}, Vector2, Angle)"/>
    /// <param name="overhang">
    ///     The sum of the off-floor points' offsets from the hull's centre, in world axes: which way the hull hangs over.
    /// </param>
    public int OffFloorCount(Entity<KsCrewVehicleDriveComponent> hull, Vector2 worldPosition, Angle worldRotation, out Vector2 overhang)
    {
        overhang = Vector2.Zero;
        var samples = ResolveSamples(hull);
        var transformComponent = Transform(hull.Owner);
        if (transformComponent.MapUid is not { } mapUid)
            return samples.Length;

        var count = 0;
        foreach (var sample in samples)
        {
            var offset = worldRotation.RotateVec(sample);
            if (IsFloor(mapUid, transformComponent.GridUid, worldPosition + offset))
                continue;

            count++;
            overhang += offset;
        }

        return count;
    }

    /// <summary>
    ///     Whether there is floor under the hull's centre: off it, the hull can't be driven.
    /// </summary>
    public bool IsCentreOnFloor(EntityUid hullUid)
    {
        var transformComponent = Transform(hullUid);
        return transformComponent.MapUid is { } mapUid &&
               IsFloor(mapUid, transformComponent.GridUid, _transformSystem.GetWorldPosition(transformComponent));
    }

    /// <summary>
    ///     Whether there is a floor tile at <paramref name="worldPoint"/>: on <paramref name="hintGridUid"/>, the grid the
    ///         hull is on, else on whichever grid is there.
    /// </summary>
    public bool IsFloor(EntityUid mapUid, EntityUid? hintGridUid, Vector2 worldPoint)
    {
        return _floorTileSystem.IsFloor(mapUid, hintGridUid, worldPoint);
    }

    private Vector2[] ResolveSamples(Entity<KsCrewVehicleDriveComponent> hull)
    {
        if (hull.Comp.FloorSamples is { } cached)
            return cached;

        if (!_fixturesQuery.TryComp(hull.Owner, out var fixturesComponent) ||
            !fixturesComponent.Fixtures.TryGetValue(hull.Comp.FloorFixture, out var fixture))
        {
            Log.Error($"{ToPrettyString(hull)} has no fixture {hull.Comp.FloorFixture} to keep on the floor");
            hull.Comp.FloorSamples = [Vector2.Zero];
            return hull.Comp.FloorSamples;
        }

        var bounds = fixture.Shape.ComputeAABB(Robust.Shared.Physics.Transform.Empty, 0).Enlarged(-SampleInset);
        var corners = new[] { bounds.BottomLeft, bounds.BottomRight, bounds.TopRight, bounds.TopLeft };
        var samples = new List<Vector2>();
        for (var i = 0; i < corners.Length; i++)
        {
            var from = corners[i];
            var to = corners[(i + 1) % corners.Length];
            var segments = Math.Max(1, (int)MathF.Ceiling((to - from).Length() / SampleSpacing));
            for (var segment = 0; segment < segments; segment++)
            {
                samples.Add(Vector2.Lerp(from, to, segment / (float)segments));
            }
        }

        hull.Comp.FloorSamples = samples.ToArray();
        return hull.Comp.FloorSamples;
    }
}
