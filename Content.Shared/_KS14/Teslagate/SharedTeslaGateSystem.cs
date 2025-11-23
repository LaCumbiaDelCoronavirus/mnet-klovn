// SPDX-FileCopyrightText: 2025 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2025 github_actions[bot]
//
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.CompilerServices;
using Content.Shared.Damage;
using Content.Shared.Power;
using Content.Shared.Power.EntitySystems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;
using DependencyAttribute = Robust.Shared.IoC.DependencyAttribute;


namespace Content.Shared._KS14.TeslaGate;

public abstract class SharedTeslaGateSystem : EntitySystem
{
    [Dependency] protected readonly SharedAudioSystem AudioSystem = default!;
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly SharedPhysicsSystem _physicsSystem = default!;
    [Dependency] private readonly DamageableSystem _damageableSystem = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearanceSystem = default!;
    [Dependency] private readonly SharedPointLightSystem _pointLight = default!;
    [Dependency] private readonly SharedPowerReceiverSystem _powerReceiverSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TeslaGateComponent, StartCollideEvent>(OnGateStartCollide);
        SubscribeLocalEvent<TeslaGateComponent, PowerChangedEvent>(OnPowerChange);
    }


    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<TeslaGateComponent>();
        while (query.MoveNext(out var uid, out var teslaGateComponent))
        {
            var teslaGate = (uid, teslaGateComponent);
            var canShock = CanWork(uid, teslaGateComponent, out var canStart);

            if (teslaGateComponent.IsTimerWireCut)
                continue;

            if (teslaGateComponent.CurrentlyShocking)
            {
                if (!canShock || IsFinishedShocking(teslaGateComponent))
                    QuitZappinEmAll(teslaGate);

                continue;
            }

            if (!canStart)
                continue;

            if (_gameTiming.CurTime < teslaGateComponent.NextPulse)
                continue;

            if (canShock)
                ZapEmAll(teslaGate);
        }
    }

    // HELP
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool CanWork(EntityUid uid, TeslaGateComponent teslaGateComponent, out bool canStart)
    {
        canStart = CanStartWork(uid);
        if (!teslaGateComponent.Enabled)
            return false;

        if (!canStart)
            return false;

        return true;
    }

    private void ZapEmAll(Entity<TeslaGateComponent> teslaGate)
    {
        var (uid, teslaGateComponent) = teslaGate;
        teslaGateComponent.LastShockTime = _gameTiming.CurTime;
        teslaGateComponent.NextPulse = _gameTiming.CurTime + teslaGate.Comp.PulseInterval;

        AudioSystem.PlayLocal(teslaGateComponent.ShockSound, uid, soundInitiator: null);

        UpdateAppearance(teslaGate, true);
        Dirty(teslaGate);

        teslaGateComponent.CurrentlyShocking = true;
        foreach (var entity in _physicsSystem.GetContactingEntities(uid))
            CollideAct(teslaGateComponent, entity);
    }

    private void QuitZappinEmAll(Entity<TeslaGateComponent> teslaGate)
    {
        var (uid, teslaGateComponent) = teslaGate;

        teslaGateComponent.CurrentlyShocking = false;
        teslaGateComponent.ThingsBeingShocked.Clear();

        UpdateAppearance(teslaGate, false);
        Dirty(teslaGate);

        AudioSystem.PlayLocal(teslaGateComponent.StartingSound, uid, soundInitiator: null);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Zap(EntityUid uid, DamageSpecifier damage)
    {
        _damageableSystem.TryChangeDamage(uid, damage, ignoreResistances: true);
    }

    private void CollideAct(TeslaGateComponent teslaGateComponent, EntityUid otherEntity)
    {
        if (!teslaGateComponent.ThingsBeingShocked.Add(GetNetEntity(otherEntity)))
            return;

        Zap(otherEntity, teslaGateComponent.ShockDamage);
    }

    private void OnGateStartCollide(Entity<TeslaGateComponent> teslaGate, ref StartCollideEvent args)
    {
        if (teslaGate.Comp.CurrentlyShocking)
            CollideAct(teslaGate, args.OtherEntity);
    }

    public bool IsFinishedShocking(TeslaGateComponent teslaGateComponent) => _gameTiming.CurTime > teslaGateComponent.LastShockTime + teslaGateComponent.ShockLength;

    protected void UpdateAppearance(Entity<TeslaGateComponent> teslaGate, bool active)
    {
        _appearanceSystem.SetData(teslaGate, TeslaGateVisuals.ShockingState, active ? TeslaGateVisualState.Active : TeslaGateVisualState.Inactive);
        _pointLight.SetEnabled(teslaGate.Owner, active);

        Dirty(teslaGate);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected bool CanStartWork(EntityUid uid)
    {
        if (!_powerReceiverSystem.IsPowered(uid))
            return false;

        return true;
    }

    public void Enable(Entity<TeslaGateComponent> teslaGate)
    {
        var (uid, teslaGateComponent) = teslaGate;

        if (CanStartWork(uid))
            AudioSystem.PlayPvs(teslaGateComponent.StartingSound, uid);

        ResetAccumulator(teslaGateComponent);
        teslaGateComponent.Enabled = true;

        Dirty(teslaGate);
    }

    public void Disable(Entity<TeslaGateComponent> teslaGate)
    {
        teslaGate.Comp.Enabled = false;
        ResetAccumulator(teslaGate);

        Dirty(teslaGate);
    }

    private void OnPowerChange(Entity<TeslaGateComponent> teslaGate, ref PowerChangedEvent args)
    {
        if (args.Powered)
        {
            if (teslaGate.Comp.WasDisabledByPower)
            {
                Enable(teslaGate);
                teslaGate.Comp.WasDisabledByPower = false;
            }
        }
        else
        {
            teslaGate.Comp.WasDisabledByPower |= teslaGate.Comp.Enabled;
            Disable(teslaGate);
        }
    }

    protected void ResetAccumulator(TeslaGateComponent teslaGateComponent)
    {
        teslaGateComponent.NextPulse = _gameTiming.CurTime + teslaGateComponent.PulseInterval;
        teslaGateComponent.LastShockTime = TimeSpan.MinValue;
    }
}
