using Content.Server.Access.Components;
using Content.Shared.Humanoid;
using Content.Shared.Inventory;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Verbs;
using Robust.Shared.Localization;
using Content.Shared.Item.ItemToggle;
using Robust.Shared.Prototypes;
using Content.Server.Storage.Components;
using Content.Shared.Armor;
using Content.Shared.Clothing.Components;
using Content.Shared.Radio.Components;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests;

[TestFixture]
public sealed class InstituteFieldEquipmentTest
{
    [Test]
    public async Task LockerSpawnsCompleteDisguiseKitAndBothRadioKeys()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var locker = entities.SpawnEntity("InstituteFieldLocker", map.GridCoords);
            var contents = entities.GetComponent<EntityStorageComponent>(locker).Contents.ContainedEntities;
            Assert.That(contents.Count, Is.EqualTo(12));
            var items = contents.ToDictionary(e => entities.GetComponent<MetaDataComponent>(e).EntityPrototype!.ID);
            Assert.That(items.ContainsKey("BarberScissors"), Is.True);
            var tags = items["InstituteChameleonDogtags"];
            Assert.That(entities.HasComponent<AgentIDCardComponent>(tags), Is.True);
            foreach (var item in contents.Where(e => e != items["BarberScissors"]))
                Assert.That(entities.HasComponent<ChameleonClothingComponent>(item), Is.True);
            var armor = entities.GetComponent<ArmorComponent>(items["ClothingOuterChameleon"]);
            foreach (var damage in new[] { "Blunt", "Slash", "Piercing", "Heat" })
                Assert.That(armor.Modifiers.Coefficients[damage], Is.EqualTo(0.8f));
            var headset = items["InstituteChameleonScrapHeadset"];
            var keys = entities.System<SharedContainerSystem>().GetContainer(headset, "key_slots").ContainedEntities;
            Assert.That(keys.Count, Is.EqualTo(2));
            var channels = keys.SelectMany(e => entities.GetComponent<EncryptionKeyComponent>(e).Channels).ToArray();
            Assert.That(channels, Is.EquivalentTo(new[] { "InstituteRadio", "WastelandGlobal" }));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AllInstituteModControlsAndAttachedPiecesCanDisguise()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            foreach (var id in new[] { "ReclamationClothingModsuitInstitute", "InstituteResearchMODSuit", "InstituteSecurityMODSuit" })
            {
                var suit = entities.SpawnEntity(id, map.GridCoords);
                Assert.That(entities.HasComponent<ChameleonClothingComponent>(suit), Is.True);
                var parts = entities.GetComponent<ToggleableClothingComponent>(suit).ClothingUids;
                Assert.That(parts.Count, Is.EqualTo(4));
                foreach (var part in parts.Keys)
                    Assert.That(entities.HasComponent<ChameleonClothingComponent>(part), Is.True);
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ExpandedDisguisesAndSealedHelmetHairBlockerWork()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var disguises = entities.System<Content.Server.Clothing.Systems.ChameleonClothingSystem>();
            var tags = entities.SpawnEntity("InstituteChameleonDogtags", map.GridCoords);
            var dogtags = entities.GetComponent<ChameleonClothingComponent>(tags);
            Assert.That(dogtags.RestrictTargets, Is.True);
            Assert.That(dogtags.AdditionalTargets, Does.Contain((EntProtoId) "MinutemenIDDogtagsPrivate"));
            Assert.That(entities.GetComponent<AgentIDCardComponent>(tags).AllowJobIconChange, Is.False);
            disguises.SetSelectedPrototype(tags, "CaptainIDCard");
            Assert.That(dogtags.Default, Is.EqualTo((EntProtoId?) "N14ClothingNeckDogtags"));
            foreach (var target in dogtags.AdditionalTargets)
            {
                Assert.That(prototypes.HasIndex<EntityPrototype>(target), Is.True, target.ToString());
                disguises.SetSelectedPrototype(tags, target);
                Assert.That(dogtags.Default, Is.EqualTo((EntProtoId?) target));
            }
            foreach (var id in new[] { "ReclamationClothingModsuitInstitute", "InstituteResearchMODSuit", "InstituteSecurityMODSuit" })
            {
                var suit = entities.SpawnEntity(id, map.GridCoords);
                var chameleon = entities.GetComponent<ChameleonClothingComponent>(suit);
                Assert.That(chameleon.AdditionalTargets.Count, Is.EqualTo(25));
                foreach (var target in chameleon.AdditionalTargets)
                {
                    disguises.SetSelectedPrototype(suit, target);
                    Assert.That(chameleon.Default, Is.EqualTo((EntProtoId?) target));
                }
                foreach (var part in entities.GetComponent<ToggleableClothingComponent>(suit).ClothingUids.Keys)
                {
                    if (!entities.HasComponent<Content.Shared._Goobstation.Clothing.Components.SealableClothingComponent>(part)
                        || !entities.HasComponent<HideLayerClothingComponent>(part))
                        continue;
                    var wearer = entities.SpawnEntity("MobHuman", map.GridCoords);
                    Assert.That(entities.System<InventorySystem>().TryEquip(wearer, part, "head", force: true), Is.True);
                    var appearance = entities.GetComponent<HumanoidAppearanceComponent>(wearer);
                    Assert.That(appearance.HiddenLayers, Does.Not.Contain(HumanoidVisualLayers.Hair));
                    var sealable = entities.GetComponent<Content.Shared._Goobstation.Clothing.Components.SealableClothingComponent>(part);
#pragma warning disable RA0002 // Simulate the seal system completing its state transition in this fixture.
                    sealable.IsSealed = true;
                    var sealedEvent = new Content.Shared._Goobstation.Clothing.Systems.ClothingPartSealCompleteEvent(true);
                    entities.EventBus.RaiseLocalEvent(part, ref sealedEvent, true);
                    Assert.That(appearance.HiddenLayers, Does.Contain(HumanoidVisualLayers.Hair));
                    sealable.IsSealed = false;
#pragma warning restore RA0002
                    var openEvent = new Content.Shared._Goobstation.Clothing.Systems.ClothingPartSealCompleteEvent(false);
                    entities.EventBus.RaiseLocalEvent(part, ref openEvent, true);
                    Assert.That(appearance.HiddenLayers, Does.Not.Contain(HumanoidVisualLayers.Hair));
                }
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LockerDisguisesOfferControlsWhileHeld()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var wearer = entities.SpawnEntity("MobHuman", map.GridCoords);
            var locker = entities.SpawnEntity("InstituteFieldLocker", map.GridCoords);
            var contents = entities.GetComponent<EntityStorageComponent>(locker).Contents.ContainedEntities;
            foreach (var stored in contents)
            {
                if (!entities.HasComponent<ChameleonClothingComponent>(stored))
                    continue;
                var id = entities.GetComponent<MetaDataComponent>(stored).EntityPrototype!.ID;
                var item = entities.SpawnEntity(id, map.GridCoords);
                Assert.That(entities.System<SharedHandsSystem>().TryPickupAnyHand(wearer, item), Is.True, id);
                var verbs = entities.System<SharedVerbSystem>().GetLocalVerbs(item, wearer, typeof(InteractionVerb), force: true);
                Assert.That(verbs.Any(verb => verb.Text == Loc.GetString("chameleon-component-verb-text")), Is.True, id);
                Assert.That(entities.System<SharedHandsSystem>().TryDrop(wearer, item), Is.True);
            }
        });
        await pair.CleanReturnAsync();
    }
}
