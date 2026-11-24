#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Shared.Localizations;
using Content.Shared._KS14.RadioLocalization;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Localization;

/// <summary>
///     Localisation tests that load prototypes into the running game. A prototype loaded at runtime stays registered
///     for the life of the pair, and later fixtures that reuse the pair would meet it (<c>EntityTest</c> spawns every
///     entity prototype), so these tests throw the pair away afterwards.
/// </summary>
public sealed class KsLocalizationPrototypeReloadTests : GameTest
{
    // Destructive, not just dirty: cleaning a dirty pair resets entities and cvars, never the prototype manager, so
    // KsLocaleCacheTest and KsTestFrenchRadio would stay registered for every test that reuses it.
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true, Destructive = true };

    [Test]
    public async Task PrototypeReloadInvalidatesLocalizedNames()
    {
        EntProtoId cacheTestPrototype = "KsLocaleCacheTest";
        var originalName = "";
        var changedName = "";
        await Server.WaitPost(() =>
        {
            var prototypeManager = Server.ResolveDependency<IPrototypeManager>();
            var localizationManager = Server.ResolveDependency<ILocalizationManager>();
            var contentLocalizationManager = Server.ResolveDependency<ContentLocalizationManager>();
            var prototypes = prototypeManager.EnumeratePrototypeKinds().ToDictionary(
                kind => kind,
                kind => prototypeManager.EnumeratePrototypes(kind).Select(prototype => prototype.ID).ToHashSet());
            Assert.That(prototypeManager.ValidateStaticFields(
                typeof(KsClientLocalizationTests), prototypes), Is.Empty);
            var changes = new Dictionary<Type, HashSet<string>>();
            prototypeManager.LoadString("""
                - type: entity
                  id: KsLocaleCacheTest
                  name: original test name
                """, overwrite: true, changed: changes);
            prototypeManager.ResolveResults();
            prototypeManager.ReloadPrototypes(changes);
            foreach (var prototype in prototypeManager.EnumeratePrototypes<EntityPrototype>())
                localizationManager.GetEntityData(prototype.ID);
            var culture = System.Globalization.CultureInfo.GetCultureInfo("ru-RU");
            contentLocalizationManager.LoadAdditionalCulture(culture);
            using (new Content.Server._KS14.Localization.KsExamineLocaleScope(localizationManager, culture))
            {
                originalName = contentLocalizationManager.GetLocalizedPrototypeName(
                    prototypeManager.Index(cacheTestPrototype));
            }

            changes = new Dictionary<Type, HashSet<string>>();
            prototypeManager.LoadString("""
                - type: entity
                  id: KsLocaleCacheTest
                  name: updated test name
                """, overwrite: true, changed: changes);
            prototypeManager.ResolveResults();
            prototypeManager.ReloadPrototypes(changes);
            foreach (var prototype in prototypeManager.EnumeratePrototypes<EntityPrototype>())
                localizationManager.GetEntityData(prototype.ID);
            using (new Content.Server._KS14.Localization.KsExamineLocaleScope(localizationManager, culture))
            {
                changedName = contentLocalizationManager.GetLocalizedPrototypeName(
                    prototypeManager.Index(cacheTestPrototype));
            }
        });
        Assert.That(originalName, Is.EqualTo("original test name"));
        Assert.That(changedName, Is.EqualTo("updated test name"));
    }

    [Test]
    public async Task RadioMapsFollowAddedCulturesParentCulturesAndReloads()
    {
        await Client.WaitPost(() =>
        {
            var prototypes = Client.ResolveDependency<IPrototypeManager>();
            var radio = Client.ResolveDependency<KsRadioLocalization>();
            // Cache an initially unmapped regional culture; a prototype reload must
            // invalidate that negative result as well as existing mappings.
            Assert.That(radio.GetLocalizedKey("fr-FR", 'e'), Is.EqualTo('e'));
            var changes = new Dictionary<Type, HashSet<string>>();
            prototypes.LoadString("""
                - type: ksRadioLocale
                  id: KsTestFrenchRadio
                  culture: fr
                  keys:
                    e: é
                """, overwrite: true, changed: changes);
            prototypes.ReloadPrototypes(changes);
            Assert.Multiple(() =>
            {
                Assert.That(radio.GetLocalizedKey("fr-FR", 'e'), Is.EqualTo('é'));
                Assert.That(radio.NormalizePrefix("fr-FR", ":É salut :é"), Is.EqualTo(":e salut :é"));
                Assert.That(radio.NormalizePrefix("en-US", ":é salut"), Is.EqualTo(":é salut"));
            });
            changes = new Dictionary<Type, HashSet<string>>();
            prototypes.LoadString("""
                - type: ksRadioLocale
                  id: KsTestFrenchRadio
                  culture: fr
                  keys:
                    e: ê
                """, overwrite: true, changed: changes);
            prototypes.ReloadPrototypes(changes);
            Assert.Multiple(() =>
            {
                Assert.That(radio.GetLocalizedKey("fr-FR", 'e'), Is.EqualTo('ê'));
                Assert.That(radio.NormalizePrefix("fr-FR", ".Ê salut"), Is.EqualTo(".e salut"));
                Assert.That(radio.NormalizePrefix("fr-FR", ":é salut"), Is.EqualTo(":é salut"));
            });
        });
    }
}
