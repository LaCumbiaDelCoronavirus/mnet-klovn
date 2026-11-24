#nullable enable
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server.Administration.Logs;
using Content.Shared._KS14.CrewVehicle.Gunner;
using Content.Shared.Database;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._KS14.CrewVehicle;

/// <summary>
///     A mounted weapon's shot is in the admin logs under the gunner who fired it, not only the weapon.
/// </summary>
[TestOf(typeof(KsCrewVehicleGunnerSystem))]
public sealed class KsCrewVehicleShotLogTest : KsCrewVehicleGunnerTestBase
{
    protected override PoolSettings Settings => new() { Connected = true, Dirty = true, AdminLogsEnabled = true, DummyTicker = false };

    [Test]
    public async Task MountedShotLogNamesTheGunner()
    {
        var (_, weapon) = await SeatGunnerAsync();
        var target = Vector2.Zero;
        await Server.WaitPost(() => target = MuzzlePosition(weapon) + new Vector2(0f, -2f));
        await RequestShotAsync(weapon, OutsideAt(target));

        var adminLogManager = Server.ResolveDependency<IAdminLogManager>();
        var gunnerName = SEntMan.GetComponent<MetaDataComponent>(SPlayer).EntityName;
        var weaponName = SEntMan.GetComponent<MetaDataComponent>(weapon).EntityName;
        string? message = null;
        await PoolManager.WaitUntil(Server, async () =>
        {
            var logs = await adminLogManager.CurrentRoundLogs(new LogFilter
            {
                Types = new() { LogType.AttackArmedClick },
            });
            message = logs.Select(log => log.Message).FirstOrDefault(text => text.Contains("mounted on"));
            return message != null;
        });

        Assert.That(message, Does.Contain(gunnerName), "the log names the gunner");
        Assert.That(message, Does.Contain(weaponName), "and the weapon");
    }
}
