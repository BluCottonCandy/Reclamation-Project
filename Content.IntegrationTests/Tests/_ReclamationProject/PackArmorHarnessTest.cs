using System.Numerics;
using Content.Client.Inventory;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.Hands;
using Content.Client.UserInterface.Systems.Hands.Controls;
using Content.Client.UserInterface.Systems.Hotbar.Widgets;
using Content.Client.UserInterface.Systems.Inventory.Controls;
using Content.Shared.Hands.Components;
using Robust.Client.UserInterface;
using Robust.Shared.Maths;
using Content.Server._ReclamationProject.Pack;
using Content.Shared._ReclamationProject.Pack;
using Robust.Shared.Prototypes;
using Robust.Client.GameObjects;
using Content.Shared.Inventory;
using Content.Shared.Storage;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture, NonParallelizable]
public sealed class PackArmorHarnessTest
{
    [TestPrototypes]
    private const string Prototypes = """
        - type: entity
          id: PackHarnessTestWearer
          components:
          - type: Inventory
          - type: ContainerContainer
        """;

    [Test]
    public async Task HarnessUsesSeparateFilteredSlots()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();

        await pair.Server.WaitAssertion(() =>
        {
            var inv = entities.System<InventorySystem>();
            EntityUid Spawn(string id) => entities.SpawnEntity(id, map.GridCoords);
            var wearer = Spawn("PackHarnessTestWearer");
            var armor = Spawn("PackThemedVest2Raven");
            var bow = Spawn("PackHuntingBow");
            var quiver = Spawn("PackArrowQuiver");
            var backpack = Spawn("N14ClothingBackpackSatchelExplorer");
            var belt = Spawn("PackPunkBelt");
            const string sling = PackArmorHarnessComponent.BowSlot;
            const string loop = PackArmorHarnessComponent.QuiverSlot;

            Assert.That(inv.CanEquip(wearer, bow, sling, out _), Is.False);
            Assert.That(inv.CanEquip(wearer, quiver, loop, out _), Is.False);
            Assert.That(inv.TryEquip(wearer, backpack, "back"), Is.True);
            Assert.That(inv.TryEquip(wearer, belt, "belt"), Is.True);
            Assert.That(inv.TryEquip(wearer, armor, "outerClothing"), Is.True);
            Assert.That(inv.CanEquip(wearer, Spawn("PackArrowBundle"), sling, out _), Is.False);
            Assert.That(inv.CanEquip(wearer, Spawn("PackPunkBelt"), loop, out _), Is.False);
            Assert.That(inv.TryEquip(wearer, bow, sling), Is.True);
            Assert.That(inv.TryEquip(wearer, quiver, loop), Is.True);
            var arrow = Spawn("PackHuntingArrow");
            var storage = entities.GetComponent<StorageComponent>(quiver);
            Assert.That(entities.System<SharedContainerSystem>().Insert(arrow, storage.Container), Is.True);
            Assert.That(entities.System<PackBowAutoloadSystem>().TryLoadNext(wearer, bow), Is.True);
            Assert.That(entities.GetComponent<TransformComponent>(arrow).ParentUid, Is.EqualTo(bow));

            Assert.That(inv.TryUnequip(wearer, "outerClothing"), Is.True);
            Assert.That(inv.TryGetSlotEntity(wearer, sling, out _), Is.False);
            Assert.That(inv.TryGetSlotEntity(wearer, loop, out _), Is.False);
            Assert.That(entities.EntityExists(bow) && entities.EntityExists(quiver), Is.True);
            Assert.That(inv.TryGetSlotEntity(wearer, "back", out var back) && back == backpack, Is.True);
            Assert.That(inv.TryGetSlotEntity(wearer, "belt", out var waist) && waist == belt, Is.True);
        });
        await pair.CleanReturnAsync();
    }

    [TestCase("PackHuntingBow")]
    [TestCase("PackCompoundBow")]
    public async Task HarnessWornSpritesReplicate(string bowId)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var map = await pair.CreateTestMap();
        var server = pair.Server.ResolveDependency<IEntityManager>();
        var client = pair.Client.ResolveDependency<IEntityManager>();
        EntityUid wearer = default;
        NetEntity netWearer = default;

        await pair.Server.WaitAssertion(() =>
        {
            var inventory = server.System<InventorySystem>();
            wearer = server.SpawnEntity("MobHuman", map.GridCoords);
            netWearer = server.GetNetEntity(wearer);
            void Equip(string id, string slot) =>
                Assert.That(inventory.TryEquip(wearer, server.SpawnEntity(id, map.GridCoords), slot, force: true), Is.True);

            Equip("PackPunkArmorGreen", "outerClothing");
            Equip("N14ClothingBackpackSatchelExplorer", "back");
            Equip("PackPunkBelt", "belt");
            Equip(bowId, PackArmorHarnessComponent.BowSlot);
            Equip("PackArrowQuiver", PackArmorHarnessComponent.QuiverSlot);
        });

        await pair.RunTicksSync(20);
        await pair.Client.WaitAssertion(() =>
        {
            var sprite = client.GetComponent<SpriteComponent>(client.GetEntity(netWearer));
            foreach (var (slot, state) in new[] { ("pack_bow", "equipped-BACKPACK"), ("pack_quiver", "equipped-BELT") })
            {
                Assert.That(sprite.LayerMapTryGet(slot + "-0", out var layer), Is.True, slot);
                Assert.That(sprite[layer].RsiState.Name, Is.EqualTo(state));
            }
        });
        await pair.Server.WaitAssertion(() =>
            Assert.That(server.System<InventorySystem>().TryUnequip(wearer, "outerClothing", force: true), Is.True));
        await pair.RunTicksSync(20);
        await pair.Client.WaitAssertion(() =>
        {
            var sprite = client.GetComponent<SpriteComponent>(client.GetEntity(netWearer));
            Assert.That(sprite.LayerMapTryGet("pack_bow-0", out _), Is.False);
            Assert.That(sprite.LayerMapTryGet("pack_quiver-0", out _), Is.False);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task HarnessButtonsStackAboveTheMatchingSlot()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;
        await client.WaitAssertion(() =>
        {
            var prototypes = client.ResolveDependency<IPrototypeManager>();
            var human = prototypes.Index<InventoryTemplatePrototype>("human");
            var harness = prototypes.Index<InventoryTemplatePrototype>("PackArmorHarnessSlots");
            using var hotbar = new EquipmentHotbarContainer();
            foreach (var def in human.Slots.Where(s => s.Name == "back" || s.Name == "belt"))
                hotbar.AddButton(new SlotButton(new ClientInventorySystem.SlotData(def)));

            foreach (var def in harness.Slots)
            {
                var button = new SlotButton(new ClientInventorySystem.SlotData(def));
                hotbar.AddButton(button);
                var anchor = hotbar.GetButton(def.AboveSlot!);
                Assert.That(button.Parent, Is.SameAs(anchor!.Parent));
                Assert.That(button.GetPositionInParent(), Is.LessThan(anchor.GetPositionInParent()));
                hotbar.RemoveButton(button);
                Assert.That(hotbar.GetButton(def.Name), Is.Null);
                Assert.That(hotbar.GetButton(def.AboveSlot!), Is.SameAs(anchor));
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task HarnessKeepsHandButtonsAtTheirNormalScreenPositions()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            var prototypes = pair.Client.ResolveDependency<IPrototypeManager>();
            using var hud = new HotbarGui();
            var gear = hud.FindControl<EquipmentHotbarContainer>("SecondHotbar");
            var left = new HandButton("left", HandLocation.Left);
            var right = new HandButton("right", HandLocation.Right);
            hud.HandContainer.AddButton(left);
            hud.HandContainer.AddButton(right);
            var human = prototypes.Index<InventoryTemplatePrototype>("human");
            foreach (var slot in human.Slots.Where(slot => slot.Name is "back" or "belt"))
                gear.AddButton(new SlotButton(new ClientInventorySystem.SlotData(slot)));

            var bounds = UIBox2.FromDimensions(Vector2.Zero, new Vector2(1280, 720));
            void Layout()
            {
                // This standalone widget is outside the normal root layout queue.
                static void InvalidateTree(Control control)
                {
                    foreach (var child in control.Children)
                        InvalidateTree(child);
                    control.InvalidateMeasure();
                }
                InvalidateTree(hud);
                hud.Measure(bounds.Size);
                hud.Arrange(bounds);
            }

            try
            {
                Layout();
                var leftRect = left.GlobalRect;
                var rightRect = right.GlobalRect;
                Assert.That(leftRect.Height, Is.GreaterThan(0));
                Assert.That(rightRect.Height, Is.GreaterThan(0));

                var harness = prototypes.Index<InventoryTemplatePrototype>("PackArmorHarnessSlots");
                foreach (var slot in harness.Slots)
                    gear.AddButton(new SlotButton(new ClientInventorySystem.SlotData(slot)));
                Layout();
                foreach (var slot in harness.Slots)
                {
                    var button = gear.GetButton(slot.Name)!;
                    var anchor = gear.GetButton(slot.AboveSlot!)!;
                    Assert.That(button.GlobalRect.Bottom, Is.LessThanOrEqualTo(anchor.GlobalRect.Top));
                    Assert.That(button.GlobalRect.Left, Is.EqualTo(anchor.GlobalRect.Left));
                }
                Assert.That(left.GlobalRect, Is.EqualTo(leftRect), "Left hand moved when wearing armor.");
                Assert.That(right.GlobalRect, Is.EqualTo(rightRect), "Right hand moved when wearing armor.");

                foreach (var slot in harness.Slots)
                    gear.RemoveButton(slot.Name);
                Layout();
                Assert.That(left.GlobalRect, Is.EqualTo(leftRect));
                Assert.That(right.GlobalRect, Is.EqualTo(rightRect));
            }
            finally
            {
                pair.Client.ResolveDependency<IUserInterfaceManager>().GetUIController<HandsUIController>()
                    .RemoveHandContainer(hud.HandContainer.Indexer!);
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MinutemenVestsHaveOfficerStorage()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var officer = entities.SpawnEntity("MinutemenClothingOuterVestOfficer", map.GridCoords);
            var storage = entities.GetComponent<StorageComponent>(officer);
            foreach (var id in new[] { "MinutemenClothingOuterVestEnlisted", "MinutemenClothingOuterVestNCO",
                         "MinutemenLightTrooperArmor", "MinutemenMediumTrooperArmor", "MinutemenHeavyTrooperArmor" })
            {
                var vest = entities.SpawnEntity(id, map.GridCoords);
                var pouch = entities.GetComponent<StorageComponent>(vest);
                Assert.That(pouch.Grid, Is.EqualTo(storage.Grid), id);
                Assert.That(pouch.Whitelist, Is.Null, id);
                Assert.That(entities.System<SharedContainerSystem>().TryGetContainer(vest, "storagebase", out _), Is.True, id);
            }
        });
        await pair.CleanReturnAsync();
    }
}
