using System.Numerics;
using Content.Client._KS14.Particles;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.GroundEffect;
using Content.Shared._KS14.Particles;
using Content.Shared.Fluids;
using Content.Shared.Fluids.Components;
using Content.Shared.Maps;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using DependencyAttribute = Robust.Shared.IoC.DependencyAttribute;

namespace Content.Client._KS14.GroundEffect;

/// <summary>
///     Throws up the ground under every <see cref="KsGroundEffectEmitterComponent"/> as it moves: each frame, each
///         contact point that moved fast enough over a tile with a <see cref="KsGroundEffectPrototype"/> (or a puddle)
///         emits particles along the stretch it covered, as many as the distance and speed say. Client only, nothing
///         networked: the transform is read as it is drawn.
/// </summary>
/// <remarks>
///     Built to cost nearly nothing for what doesn't emit. An entity that stands still costs a compare; one on another
///         map, off a grid, in the air or far from every view drawn last frame is turned away before any contact
///         maths. The tile is looked up only when a contact crosses into another, or every
///         <see cref="RetileInterval"/> for a puddle that came or went. Nothing allocates once an entity's emitters
///         exist. Particles go to one client-side emitter per entity and effect, parented to the entity so its
///         culling box rides with it; they live in the map frame, so the wake stays behind.
/// </remarks>
public sealed partial class KsGroundEffectSystem : EntitySystem
{
    /// <summary>
    ///     The effect of any tile with a puddle on it, whatever the tile, tinted by the puddle.
    /// </summary>
    public static readonly ProtoId<KsGroundEffectPrototype> PuddleEffect = "KsGroundEffectPuddle";

    /// <summary>
    ///     Metres a contact may move in one frame before it counts as a jump (a teleport, a correction) and emits
    ///         nothing.
    /// </summary>
    public const float MaxJump = 2f;

    /// <summary>
    ///     The most particles one contact emits in one frame.
    /// </summary>
    public const int PerFrameCap = 8;

    /// <summary>
    ///     Metres beyond a view's edge an entity still emits: its wake's reach, and its size.
    /// </summary>
    public const float ViewMargin = 4f;

    /// <summary>
    ///     Seconds, the time constant of the contacts' speed smoothing.
    /// </summary>
    public const float SpeedSmoothing = 0.1f;

    /// <summary>
    ///     Seconds without movement after which an entity counts as stopped. Longer than the gap between two network
    ///         states, in which a remote entity holds still.
    /// </summary>
    public const float StillTimeout = 0.25f;

    /// <summary>
    ///     Seconds between lookups of the effect under a contact that stays on one tile.
    /// </summary>
    public const float RetileInterval = 0.5f;

    /// <summary>
    ///     Seconds an emitter may go unfed, once its particles are gone, before it is deleted.
    /// </summary>
    public const float IdleLifetime = 5f;

    // Boxed once: the appearance data is keyed by Enum, and boxing it on every lookup would allocate.
    private static readonly Enum SolutionColorKey = PuddleVisuals.SolutionColor;

    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private ITileDefinitionManager _tileDefinitionManager = default!;
    [Dependency] private KsParticlesSystem _particlesSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private SharedAppearanceSystem _appearanceSystem = default!;
    [Dependency] private EntityQuery<MapGridComponent> _gridQuery = default!;
    [Dependency] private EntityQuery<PuddleComponent> _puddleQuery = default!;
    [Dependency] private EntityQuery<AppearanceComponent> _appearanceQuery = default!;
    [Dependency] private EntityQuery<PhysicsComponent> _physicsQuery = default!;
    [Dependency] private EntityQuery<KsParticlesComponent> _particlesQuery = default!;
    [Dependency] private EntityQuery<KsParticleEmitterComponent> _particleEmitterQuery = default!;

    private bool _cull = true;

    /// <summary>
    ///     Every effect, densely, so a contact keeps a small index rather than a reference.
    /// </summary>
    private KsGroundEffectPrototype[] _effects = Array.Empty<KsGroundEffectPrototype>();

    /// <summary>
    ///     The effect index of each tile type id, or -1.
    /// </summary>
    private short[] _effectByTileId = Array.Empty<short>();

    private short _puddleEffect = -1;
    private bool _tablesDirty = true;

    /// <summary>
    ///     Particles emitted since the system started, for tests and VV.
    /// </summary>
    [ViewVariables]
    public long TotalEmitted { get; private set; }

    public override void Initialize()
    {
        base.Initialize();

        // Particles emitted this frame are stepped, bounded and drawn this frame.
        UpdatesBefore.Add(typeof(KsParticlesSystem));

        Subs.CVar(_configurationManager, KsCCVars.ParticlesGroundCull, value => _cull = value, true);
    }

    #region Lifecycle

    [SubscribeLocalEvent]
    private void OnEmitterStartup(Entity<KsGroundEffectEmitterComponent> entity, ref ComponentStartup args)
    {
        var state = EnsureComp<KsGroundEffectStateComponent>(entity.Owner);
        state.Contacts = new KsGroundContact[entity.Comp.ContactPoints.Count];
        state.Valid = false;
    }

    [SubscribeLocalEvent]
    private void OnEmitterShutdown(Entity<KsGroundEffectEmitterComponent> entity, ref ComponentShutdown args)
    {
        RemCompDeferred<KsGroundEffectStateComponent>(entity.Owner);
    }

    [SubscribeLocalEvent]
    private void OnStateShutdown(Entity<KsGroundEffectStateComponent> entity, ref ComponentShutdown args)
    {
        DeleteEmitters(entity.Comp);
    }

    [SubscribeLocalEvent]
    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (!args.WasModified<KsGroundEffectPrototype>() && !args.WasModified<ContentTileDefinition>())
            return;

        _tablesDirty = true;

        // Indices may now mean other effects: start every entity afresh.
        var query = AllEntityQuery<KsGroundEffectStateComponent>();
        while (query.MoveNext(out var state))
        {
            DeleteEmitters(state);
            state.Valid = false;
        }
    }

    private void DeleteEmitters(KsGroundEffectStateComponent state)
    {
        for (var i = 0; i < state.Emitters.Length; i++)
        {
            if (state.Emitters[i] is { } emitterUid && !TerminatingOrDeleted(emitterUid))
                QueueDel(emitterUid);

            state.Emitters[i] = null;
        }

        state.EmitterCount = 0;
    }

    /// <summary>
    ///     Builds the dense effect list and the tile lookup, the first time and after a reload.
    /// </summary>
    private void EnsureTables()
    {
        if (!_tablesDirty && _effectByTileId.Length == _tileDefinitionManager.Count)
            return;

        _tablesDirty = false;

        var effects = new List<KsGroundEffectPrototype>();
        var indexById = new Dictionary<string, short>();
        foreach (var effect in ProtoMan.EnumeratePrototypes<KsGroundEffectPrototype>())
        {
            indexById[effect.ID] = (short)effects.Count;
            effects.Add(effect);
        }

        _effects = effects.ToArray();
        _puddleEffect = indexById.TryGetValue(PuddleEffect, out var puddleIndex) ? puddleIndex : (short)-1;

        _effectByTileId = new short[_tileDefinitionManager.Count];
        Array.Fill(_effectByTileId, (short)-1);
        foreach (var definition in _tileDefinitionManager)
        {
            if (definition is not ContentTileDefinition { KsGroundEffect: { } effectId } ||
                definition.TileId >= _effectByTileId.Length)
                continue;

            if (indexById.TryGetValue(effectId, out var index))
                _effectByTileId[definition.TileId] = index;
            else
                Log.Error($"Tile '{definition.ID}' names unknown ground effect '{effectId}'.");
        }
    }

    #endregion

    #region Per frame

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (!_particlesSystem.Enabled || _particlesSystem.Density <= 0f)
            return;

        EnsureTables();
        if (_effects.Length == 0)
            return;

        var query = EntityQueryEnumerator<KsGroundEffectStateComponent, KsGroundEffectEmitterComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var state, out var emitterComponent, out var transformComponent))
        {
            UpdateEntity((uid, state, emitterComponent, transformComponent), frameTime);
        }
    }

    private void UpdateEntity(Entity<KsGroundEffectStateComponent, KsGroundEffectEmitterComponent, TransformComponent> entity, float frameTime)
    {
        var (uid, state, emitterComponent, transformComponent) = entity;

        if (state.EmitterCount > 0)
            AgeEmitters(state, frameTime);

        // Another map (a z-level, an interior, back from out of view) or another parent: start again there. World
        //      particles left on the old map would show at the same coordinates on the new one.
        if (transformComponent.MapID != state.PrevMap || transformComponent.ParentUid != state.PrevParent)
        {
            if (transformComponent.MapID != state.PrevMap)
                ClearEmitters(state);

            state.PrevMap = transformComponent.MapID;
            state.PrevParent = transformComponent.ParentUid;
            state.Valid = false;
            return;
        }

        // Standing on a grid, not in space, a container, a seat or someone's hands.
        if (transformComponent.GridUid is not { } gridUid || gridUid != transformComponent.ParentUid)
        {
            state.Valid = false;
            return;
        }

        var localPosition = transformComponent.LocalPosition;
        var localRotation = transformComponent.LocalRotation;
        state.SinceMoved += frameTime;

        if (state.Valid && localPosition == state.PrevLocalPosition && localRotation == state.PrevLocalRotation)
        {
            if (!state.Settled && state.SinceMoved > StillTimeout)
            {
                state.Settled = true;
                for (var i = 0; i < state.Contacts.Length; i++)
                {
                    state.Contacts[i].Speed = 0f;
                    state.Contacts[i].Accumulator = 0f;
                }
            }

            return;
        }

        // The time this movement took: since the last change, which for a remote entity is the last network state.
        var elapsed = MathF.Min(state.SinceMoved, StillTimeout);
        state.SinceMoved = 0f;
        state.Settled = false;
        state.PrevLocalPosition = localPosition;
        state.PrevLocalRotation = localRotation;

        var wasValid = state.Valid;
        state.Valid = true;

        // Flying, thrown or jumping: nothing touches the ground.
        var inAir = _physicsQuery.TryComp(uid, out var physicsComponent) && physicsComponent.BodyStatus == BodyStatus.InAir;
        if (inAir || !_gridQuery.TryComp(gridUid, out var gridComponent))
        {
            InvalidateContacts(state);
            return;
        }

        var (gridPosition, gridRotation) = _transformSystem.GetWorldPositionRotation(gridUid);
        var (gridSin, gridCos) = MathF.SinCos((float)gridRotation.Theta);

        if (_cull && !KsGroundEffectMath.IsInAnyView(
                _particlesSystem.RecordedViews,
                _particlesSystem.RecordedViewCount,
                _particlesSystem.RecordedViewsOverflowed,
                transformComponent.MapID,
                gridPosition + Rotate(localPosition, gridSin, gridCos),
                ViewMargin))
        {
            // Coming into view starts afresh rather than emitting everything since it left.
            InvalidateContacts(state);
            return;
        }

        if (!wasValid)
            InvalidateContacts(state);

        var points = emitterComponent.ContactPoints;
        if (state.Contacts.Length != points.Count)
            state.Contacts = new KsGroundContact[points.Count];

        var (sin, cos) = MathF.SinCos((float)localRotation.Theta);
        var tileSize = (float)gridComponent.TileSize;
        var density = _particlesSystem.Density * emitterComponent.Amount;

        for (var i = 0; i < points.Count; i++)
        {
            ref var contact = ref state.Contacts[i];
            var local = localPosition + Rotate(points[i], sin, cos);

            if (!contact.Valid)
            {
                contact.Valid = true;
                contact.PrevLocal = local;
                contact.Speed = 0f;
                contact.Accumulator = 0f;
                contact.Effect = -1;
                contact.RetileTimer = 0f;
                contact.Tile = new Vector2i(int.MinValue, int.MinValue);
                continue;
            }

            var delta = local - contact.PrevLocal;
            var distance = delta.Length();
            if (distance > MaxJump)
            {
                contact.PrevLocal = local;
                contact.Speed = 0f;
                contact.Accumulator = 0f;
                continue;
            }

            contact.Speed = KsGroundEffectMath.SmoothSpeed(contact.Speed, distance / elapsed, elapsed, SpeedSmoothing);

            var tile = new Vector2i((int)MathF.Floor(local.X / tileSize), (int)MathF.Floor(local.Y / tileSize));
            contact.RetileTimer -= elapsed;
            if (tile != contact.Tile || contact.RetileTimer <= 0f)
            {
                contact.Tile = tile;
                contact.RetileTimer = RetileInterval;
                contact.Effect = ResolveEffect(gridUid, gridComponent, tile, out contact.Tint);
            }

            var effectIndex = contact.Effect;
            if (effectIndex < 0 || effectIndex >= _effects.Length)
            {
                contact.Accumulator = 0f;
                contact.PrevLocal = local;
                continue;
            }

            var effect = _effects[effectIndex];
            var minSpeed = MathF.Max(effect.MinSpeed, emitterComponent.MinSpeed);
            var perMetre = KsGroundEffectMath.Density(effect.PerMetre, minSpeed, effect.FullSpeed, effect.SlowFactor, contact.Speed) * density;
            var count = KsGroundEffectMath.TakeEmitCount(ref contact.Accumulator, perMetre, distance, PerFrameCap);

            if (count > 0 && ResolveEmitter(uid, state, effectIndex, effect) is { } emitter)
            {
                var from = gridPosition + Rotate(contact.PrevLocal, gridSin, gridCos);
                var to = gridPosition + Rotate(local, gridSin, gridCos);
                var inherited = Rotate(delta * (-effect.Throw / elapsed), gridSin, gridCos);
                var speedScale = KsGroundEffectMath.ThrowSpeedScale(contact.Speed, effect.FullSpeed);
                TotalEmitted += _particlesSystem.Emit(emitter, from, to, inherited, speedScale, contact.Tint, count);
                state.IdleTime[effectIndex] = 0f;
            }

            contact.PrevLocal = local;
        }
    }

    private static Vector2 Rotate(Vector2 vector, float sin, float cos)
    {
        return new Vector2(vector.X * cos - vector.Y * sin, vector.X * sin + vector.Y * cos);
    }

    private static void InvalidateContacts(KsGroundEffectStateComponent state)
    {
        for (var i = 0; i < state.Contacts.Length; i++)
        {
            state.Contacts[i].Valid = false;
        }
    }

    /// <summary>
    ///     The effect index on a tile: a puddle's if there is one, else the tile's, else -1.
    /// </summary>
    private short ResolveEffect(EntityUid gridUid, MapGridComponent gridComponent, Vector2i tile, out Color tint)
    {
        tint = Color.White;

        if (_puddleEffect >= 0)
        {
            var anchored = _mapSystem.GetAnchoredEntities(gridUid, gridComponent, tile);
            while (anchored.MoveNext(out var anchoredUid))
            {
                if (!_puddleQuery.HasComp(anchoredUid.Value))
                    continue;

                if (_appearanceQuery.TryComp(anchoredUid.Value, out var appearanceComponent) &&
                    _appearanceSystem.TryGetData<Color>(anchoredUid.Value, SolutionColorKey, out var color, appearanceComponent))
                    tint = color.WithAlpha(1f);

                return _puddleEffect;
            }
        }

        if (!_mapSystem.TryGetTile(gridComponent, tile, out var tileData) || tileData.TypeId >= _effectByTileId.Length)
            return -1;

        return _effectByTileId[tileData.TypeId];
    }

    /// <summary>
    ///     The entity's emitter for an effect, spawned the first time it is needed.
    /// </summary>
    private Entity<KsParticlesComponent, KsParticleEmitterComponent>? ResolveEmitter(EntityUid uid, KsGroundEffectStateComponent state, short effectIndex, KsGroundEffectPrototype effect)
    {
        if (state.Emitters.Length != _effects.Length)
        {
            Array.Resize(ref state.Emitters, _effects.Length);
            Array.Resize(ref state.IdleTime, _effects.Length);
        }

        if (state.Emitters[effectIndex] is { } existingUid)
        {
            if (_particlesQuery.TryComp(existingUid, out var existingParticles) &&
                _particleEmitterQuery.TryComp(existingUid, out var existingEmitter))
                return (existingUid, existingParticles, existingEmitter);

            // Deleted from outside.
            state.Emitters[effectIndex] = null;
            state.EmitterCount--;
        }

        if (_particlesSystem.SpawnLocal(effect.Particles, new EntityCoordinates(uid, Vector2.Zero), once: true) is not { } emitterUid)
            return null;

        state.Emitters[effectIndex] = emitterUid;
        state.IdleTime[effectIndex] = 0f;
        state.EmitterCount++;

        if (!_particlesQuery.TryComp(emitterUid, out var particlesComponent) ||
            !_particleEmitterQuery.TryComp(emitterUid, out var emitterComponent))
            return null;

        return (emitterUid, particlesComponent, emitterComponent);
    }

    /// <summary>
    ///     Deletes emitters unfed for <see cref="IdleLifetime"/> whose particles are all gone.
    /// </summary>
    private void AgeEmitters(KsGroundEffectStateComponent state, float frameTime)
    {
        for (var i = 0; i < state.Emitters.Length; i++)
        {
            if (state.Emitters[i] is not { } emitterUid)
                continue;

            state.IdleTime[i] += frameTime;
            if (state.IdleTime[i] < IdleLifetime ||
                _particleEmitterQuery.TryComp(emitterUid, out var emitterComponent) && emitterComponent.Alive > 0)
                continue;

            if (!TerminatingOrDeleted(emitterUid))
                QueueDel(emitterUid);

            state.Emitters[i] = null;
            state.EmitterCount--;
        }
    }

    private void ClearEmitters(KsGroundEffectStateComponent state)
    {
        for (var i = 0; i < state.Emitters.Length; i++)
        {
            if (state.Emitters[i] is { } emitterUid)
                _particlesSystem.Clear(emitterUid);
        }
    }

    #endregion
}
