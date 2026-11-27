#nullable enable
using System.Threading.Tasks;
using Content.Client.Damage;
using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.CrewVehicle;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Humanoid;
using NUnit.Framework;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.CrewVehicle;

/// <summary>
///     The humvee's battered frame and wrecked wheels come up past its damage threshold whatever kind of damage put it
///         there; and a mob's wounds, drawn by the same visualiser by damage group, still come up as before.
/// </summary>
[TestFixture]
[TestOf(typeof(DamageVisualsSystem))]
public sealed class KsCrewVehicleDamageVisualsTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static readonly EntProtoId Humvee = "KsCrewVehicleHumvee";
    private static readonly EntProtoId Human = "MobHuman";
    private static readonly ProtoId<DamageTypePrototype> Heat = "Heat";
    private static readonly ProtoId<DamageTypePrototype> Shock = "Shock";
    private static readonly ProtoId<DamageTypePrototype> Structural = "Structural";
    private static readonly ProtoId<DamageTypePrototype> Blunt = "Blunt";
    private static readonly ProtoId<DamageGroupPrototype> Brute = "Brute";

    /// <summary>
    ///     The overlay layer the visualiser puts over a target layer tracking all damage.
    /// </summary>
    private static string OverlayKey(KsCrewVehicleVisualLayers layer) => $"{layer}trackDamage";

    private (bool Visible, string? State) Overlay(EntityUid clientUid, KsCrewVehicleVisualLayers layer)
    {
        var spriteSystem = Client.System<SpriteSystem>();
        var sprite = CComp<SpriteComponent>(clientUid);
        Assert.That(spriteSystem.LayerMapTryGet((clientUid, sprite), OverlayKey(layer), out var index, false), Is.True,
            $"the humvee has a damage overlay over its {layer} layer");
        return (sprite[index].Visible, spriteSystem.LayerGetRsiState((clientUid, sprite), index).Name);
    }

    [TestCase("Heat")]
    [TestCase("Shock")]
    [TestCase("Structural")]
    [TestCase("Blunt")]
    public async Task DamagedLooksShowForEveryDamageType(string damageType)
    {
        var type = damageType switch
        {
            "Heat" => Heat,
            "Shock" => Shock,
            "Structural" => Structural,
            _ => Blunt,
        };

        var map = await Pair.CreateTestMap();
        EntityUid hullUid = default;
        await Server.WaitPost(() => hullUid = SSpawnAtPosition(Humvee, map.GridCoords));
        await Pair.RunTicksSync(10);
        var clientHullUid = ToClientUid(hullUid);

        var enough = FixedPoint2.Zero;
        await Client.WaitAssertion(() =>
        {
            var visuals = CComp<DamageVisualsComponent>(clientHullUid);
            Assert.That(visuals.Valid, Is.True, "the humvee's damage visuals are set up");
            enough = visuals.Thresholds[^1] * visuals.Divisor;
            foreach (var layer in new[] { KsCrewVehicleVisualLayers.Hull, KsCrewVehicleVisualLayers.Wheels })
                Assert.That(Overlay(clientHullUid, layer).Visible, Is.False, $"an undamaged humvee shows no damaged {layer}");
        });

        await Server.WaitPost(() => SEntMan.System<DamageableSystem>().TryChangeDamage(hullUid,
            new DamageSpecifier(SProtoMan.Index(type), enough), ignoreResistances: true));
        await Pair.RunTicksSync(10);

        await Client.WaitAssertion(() =>
        {
            foreach (var layer in new[] { KsCrewVehicleVisualLayers.Hull, KsCrewVehicleVisualLayers.Wheels })
            {
                var (visible, state) = Overlay(clientHullUid, layer);
                Assert.That(visible, Is.True, $"{damageType} damage past the threshold should show the damaged {layer}");
                Assert.That(state, Is.EqualTo($"{layer}_{CComp<DamageVisualsComponent>(clientHullUid).Thresholds[^1]}"));
            }
        });
    }

    /// <summary>
    ///     A mob's wounds, an overlay per damage group over its body layers, come up as they always did.
    /// </summary>
    [Test]
    public async Task MobWoundsStillShow()
    {
        var map = await Pair.CreateTestMap();
        EntityUid mobUid = default;
        await Server.WaitPost(() => mobUid = SSpawnAtPosition(Human, map.GridCoords));
        await Pair.RunTicksSync(10);
        var clientMobUid = ToClientUid(mobUid);

        var enough = FixedPoint2.Zero;
        await Client.WaitPost(() =>
        {
            var visuals = CComp<DamageVisualsComponent>(clientMobUid);
            enough = visuals.Thresholds[1] * visuals.Divisor;
        });

        await Server.WaitPost(() => SEntMan.System<DamageableSystem>().TryChangeDamage(mobUid,
            new DamageSpecifier(SProtoMan.Index(Blunt), enough), ignoreResistances: true));
        await Pair.RunTicksSync(10);

        await Client.WaitAssertion(() =>
        {
            var spriteSystem = Client.System<SpriteSystem>();
            var sprite = CComp<SpriteComponent>(clientMobUid);
            Assert.That(CComp<DamageVisualsComponent>(clientMobUid).LastThresholdPerGroup[Brute], Is.GreaterThan(FixedPoint2.Zero));
            Assert.That(spriteSystem.LayerMapTryGet((clientMobUid, sprite), $"{HumanoidVisualLayers.Chest}{Brute}", out var index, false), Is.True,
                "the mob has a brute overlay over its chest");
            Assert.That(sprite[index].Visible, Is.True, "the chest shows its bruises");
        });
    }
}
