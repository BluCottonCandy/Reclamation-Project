using Content.Server.Radio.Components;
using Content.Server.Radio.EntitySystems;
using Content.Server.Speech;
using Content.Shared.Inventory;
using Content.Shared._NC.Radio;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;
using Content.Shared.EntityTable;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests;

[TestFixture]
public sealed class HeadsetFrequencyRadioTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: HeadsetFrequencyWearer
  components:
  - type: Inventory
  - type: ContainerContainer
";

    [TestCase("ClothingHeadsetGrey", "ears")]
    [TestCase("N14ClothingHeadsetNCR", "ears")]
    [TestCase("MinutemenClothingHeadset", "ears")]
    [TestCase("N14ClothingHeadsetBrotherhoodOfSteelScribe", "ears")]
    [TestCase("N14ClothingHeadHatNCRHelmetMetalRadioWood", "head")]
    [TestCase("MisfitsClothingHeadsetWastelandScrap", "ears")]
    [TestCase("MisfitsClothingHeadsetTown", "ears")]
    [TestCase("MisfitsClothingHeadsetEighties", "ears")]
    [TestCase("N14ClothingHeadsetBrotherhoodOfSteel", "ears")]
    public async Task FrequencyCoexistsWithKeysAndOnlyListensToWearer(string prototype, string slot)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();

        await pair.Server.WaitAssertion(() =>
        {
            var wearer = entities.SpawnEntity("HeadsetFrequencyWearer", map.GridCoords);
            var bystander = entities.SpawnEntity("HeadsetFrequencyWearer", map.GridCoords);
            var headset = entities.SpawnEntity(prototype, map.GridCoords);
            var keys = entities.GetComponent<EncryptionKeyHolderComponent>(headset);
            var keyCount = keys.KeyContainer.ContainedEntities.Count;
            Assert.That(keyCount, prototype == "MisfitsClothingHeadsetWastelandScrap"
                ? Is.Zero : Is.GreaterThan(0));
            Assert.That(keys.Channels, Does.Not.Contain("WastelandGlobal"));
            Assert.That(entities.HasComponent<RadioSpeakerComponent>(headset), Is.False,
                "A headset must not relay its incoming messages into nearby speech.");
            Assert.That(entities.HasComponent<ActiveRadioComponent>(headset), Is.False);
            var inventory = entities.System<InventorySystem>();
            Assert.That(inventory.TryEquip(wearer, headset, slot), Is.True);
            var active = entities.GetComponent<ActiveRadioComponent>(headset);
            Assert.That(active.Channels, Does.Contain("Handheld"));
            Assert.That(active.Channels, Is.SupersetOf(keys.Channels));
            var radio = entities.System<RadioSystem>();
            Assert.That(radio.GetFrequency(headset, prototypes.Index<RadioChannelPrototype>("Handheld")), Is.EqualTo(1330));
            entities.EventBus.RaiseLocalEvent(headset,
                new SelectHandheldRadioFrequencyMessage(1777) { Actor = wearer });
            Assert.That(radio.GetFrequency(headset, prototypes.Index<RadioChannelPrototype>("Handheld")), Is.EqualTo(1777));
            entities.EventBus.RaiseLocalEvent(headset,
                new SelectHandheldRadioFrequencyMessage(999) { Actor = wearer });
            Assert.That(radio.GetFrequency(headset, prototypes.Index<RadioChannelPrototype>("Handheld")), Is.EqualTo(1777));
            entities.EventBus.RaiseLocalEvent(headset,
                new ToggleHandheldRadioSpeakerMessage(true) { Actor = wearer });
            Assert.That(entities.HasComponent<RadioSpeakerComponent>(headset), Is.False);
            foreach (var channel in keys.Channels)
            {
                var channelPrototype = prototypes.Index<RadioChannelPrototype>(channel);
                Assert.That(radio.GetFrequency(headset, channelPrototype), Is.EqualTo(channelPrototype.Frequency));
            }

            var devices = entities.System<RadioDeviceSystem>();
            var off = new ListenAttemptEvent(wearer);
            entities.EventBus.RaiseLocalEvent(headset, off);
            Assert.That(off.Cancelled, Is.True);
            devices.SetMicrophoneEnabled(headset, wearer, true, true);
            var ownSpeech = new ListenAttemptEvent(wearer);
            entities.EventBus.RaiseLocalEvent(headset, ownSpeech);
            Assert.That(ownSpeech.Cancelled, Is.False);
            var nearbySpeech = new ListenAttemptEvent(bystander);
            entities.EventBus.RaiseLocalEvent(headset, nearbySpeech);
            Assert.That(nearbySpeech.Cancelled, Is.True);

            Assert.That(inventory.TryUnequip(wearer, slot), Is.True);
            Assert.That(entities.HasComponent<ActiveRadioComponent>(headset), Is.False);
            var droppedSpeech = new ListenAttemptEvent(wearer);
            entities.EventBus.RaiseLocalEvent(headset, droppedSpeech);
            Assert.That(droppedSpeech.Cancelled, Is.True);
            Assert.That(keys.KeyContainer.ContainedEntities.Count, Is.EqualTo(keyCount));
            // A recovered key still grants the channel when installed.
            var recoveredKey = entities.SpawnEntity("EncryptionKeyWastelandGlobal", map.GridCoords);
            Assert.That(entities.System<SharedContainerSystem>().Insert(recoveredKey, keys.KeyContainer), Is.True);
            Assert.That(inventory.TryEquip(wearer, headset, slot), Is.True);
            Assert.That(entities.GetComponent<ActiveRadioComponent>(headset).Channels,
                Does.Contain("WastelandGlobal"));
            entities.DeleteEntity(headset);
            entities.DeleteEntity(wearer);
            entities.DeleteEntity(bystander);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task WastelandKeyIsRareElectronicsSalvage()
    {
        await using var pair = await PoolManager.GetServerClient();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var loot = prototypes.Index<EntityTablePrototype>("M14PartsLootTier1_JunkElectronics");
            var tables = entities.System<EntityTableSystem>();
            var random = new System.Random(7314);
            var keys = 0;
            for (var roll = 0; roll < 10000; roll++)
            {
                foreach (var spawn in tables.GetSpawns(loot.Table, random))
                {
                    if (spawn.Id == "EncryptionKeyWastelandGlobal")
                        keys++;
                }
            }
            Assert.That(keys, Is.InRange(400, 550), "Expected roughly 4.76% of electronics loot rolls.");
        });
        await pair.CleanReturnAsync();
    }
}
