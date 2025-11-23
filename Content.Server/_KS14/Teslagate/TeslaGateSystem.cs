// SPDX-FileCopyrightText: 2025 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2025 github_actions[bot]
//
// SPDX-License-Identifier: MPL-2.0

using Content.Shared._KS14.TeslaGate;
using Content.Server.AlertLevel;
using Content.Server.Station.Systems;

namespace Content.Server._KS14.TeslaGate;

public sealed class TeslaGateSystem : SharedTeslaGateSystem
{
    [Dependency] private readonly StationSystem _stationSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AlertLevelChangedEvent>(OnAlertLevelChanged);
    }

    private void OnAlertLevelChanged(AlertLevelChangedEvent alertEvent)
    {
        if (!TryComp<AlertLevelComponent>(alertEvent.Station, out var _))
            return;

        var alertLevel = alertEvent.AlertLevel;

        var query = EntityQueryEnumerator<TeslaGateComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var teslaGateComponent, out var transformComponent))
        {
            // only process this teslagate if: it's on a station, and the station that it's on is the one that this AlertLevelChangedEvent is directed at
            if (transformComponent.GridUid == null ||
                _stationSystem.GetOwningStation(uid, transformComponent) is not { } owningStationUid ||
                owningStationUid != alertEvent.Station)
                continue;

            if (teslaGateComponent.IsForceHacked)
                continue;

            var teslaGate = (uid, teslaGateComponent);
            if (teslaGateComponent.EnabledAlertLevels.Contains(alertLevel))
                Enable(teslaGate);
            else
                Disable(teslaGate);
        }
    }
}
