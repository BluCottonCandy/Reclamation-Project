using Content.Shared._Goobstation.Clothing.Components;
using Content.Shared._ReclamationProject.Institute.Augment;
using System.Linq;
using Content.Shared.Clothing.Components;
using Content.Shared.Clothing.EntitySystems;
using Content.Shared.Actions;
using Content.Shared.Inventory;
using Content.Shared.Overlays.Switchable;
using Robust.Shared.Containers;
using Content.Shared.PowerCell;
using Content.Shared.PowerCell.Components;
using Content.Shared.Weapons.Reflect;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture]
public sealed class InstituteSuitTest
{
    private const ReflectType Deflects =
        ReflectType.NonEnergy | ReflectType.SmallCaliber | ReflectType.MediumCaliber | ReflectType.Energy;

    [Test]
    [TestCase("ReclamationClothingModsuitInstitute")]
    [TestCase("InstituteResearchMODSuit")]
    [TestCase("InstituteSecurityMODSuit")]
    [TestCase("InstituteCommandMODSuit")]
    public async Task SuitDeflectsOnlyWhileSealedAndPowered(string suitId)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();

        EntityUid suit = default;

        await server.WaitAssertion(() =>
        {
            var wearer = entities.SpawnEntity("MobHuman", map.GridCoords);
            suit = entities.SpawnEntity(suitId, map.GridCoords);
            Assert.That(entities.System<InventorySystem>().TryEquip(wearer, suit, "back", silent: true, force: true), Is.True);

            var reflect = entities.GetComponent<ReflectComponent>(suit);
            Assert.That(reflect.ReflectProb, Is.EqualTo(0.35f).Within(0.001f), "bullet deflect isn't 35%");
            Assert.That(reflect.ReflectProbByType[ReflectType.Energy], Is.EqualTo(0.5f).Within(0.001f), "laser deflect isn't 50%");
            Assert.That(reflect.Reflects, Is.EqualTo(ReflectType.None), "deflecting while not sealed");

            // 30 minutes sealed on a full cell.
            var draw = entities.GetComponent<PowerCellDrawComponent>(suit);
            Assert.That(entities.System<Content.Server.PowerCell.PowerCellSystem>().TryGetBatteryFromSlot(suit, out var battery), Is.True);
            Assert.That(battery!.MaxCharge / draw.DrawRate / (float) draw.Delay.TotalSeconds, Is.EqualTo(1800f).Within(1f),
                "a full cell doesn't last 30 minutes");

            // Sealed with charge: deflect on.
            // Skip the multi-step seal do-afters and set the end state directly.
#pragma warning disable RA0002
            entities.GetComponent<SealableClothingControlComponent>(suit).IsCurrentlySealed = true;
            entities.GetComponent<SealableClothingRequiresPowerComponent>(suit).IsPowered = true;
#pragma warning restore RA0002
        });

        await server.WaitRunTicks(30);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<ReflectComponent>(suit).Reflects, Is.EqualTo(Deflects), "sealed + powered suit doesn't deflect");
            Assert.That(entities.GetComponent<InstituteSuitDeflectComponent>(suit).Active, Is.True);

            // Cell runs dry: deflect off, even though still sealed.
#pragma warning disable RA0002
            entities.GetComponent<SealableClothingRequiresPowerComponent>(suit).IsPowered = false;
#pragma warning restore RA0002
        });

        await server.WaitRunTicks(30);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<ReflectComponent>(suit).Reflects, Is.EqualTo(ReflectType.None), "dead suit still deflects");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CommandSuitDeploysOverTheDirectorCoat()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var inventory = entities.System<InventorySystem>();
            var toggles = entities.System<ToggleableClothingSystem>();
            var wearer = entities.SpawnEntity("MobHuman", map.GridCoords);
            var coat = entities.SpawnEntity("InstituteDirectorCoat", map.GridCoords);
            var suit = entities.SpawnEntity("InstituteCommandMODSuit", map.GridCoords);
            Assert.That(inventory.TryEquip(wearer, coat, "outerClothing", silent: true, force: true), Is.True);
            Assert.That(inventory.TryEquip(wearer, suit, "back", silent: true, force: true), Is.True);

            var toggleable = entities.GetComponent<ToggleableClothingComponent>(suit);
            var chest = toggleable.ClothingUids.First(kv => kv.Value == "outerClothing").Key;

            // Same call the suit's radial menu makes.
            var toggle = typeof(ToggleableClothingSystem).GetMethod("ToggleClothing",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public,
                null, new[] { typeof(EntityUid), typeof(Entity<ToggleableClothingComponent>), typeof(EntityUid) }, null)!;

            toggle.Invoke(toggles, new object[] { wearer, new Entity<ToggleableClothingComponent>(suit, toggleable), chest });
            Assert.That(inventory.TryGetSlotEntity(wearer, "outerClothing", out var worn) && worn == chest,
                "the chestplate didn't deploy over the coat");
            Assert.That(entities.System<SharedContainerSystem>().ContainsEntity(chest, coat), Is.True, "the coat wasn't tucked into the chestplate");

            toggle.Invoke(toggles, new object[] { wearer, new Entity<ToggleableClothingComponent>(suit, toggleable), chest });
            Assert.That(inventory.TryGetSlotEntity(wearer, "outerClothing", out var back) && back == coat,
                "the coat didn't come back when the chestplate folded away");

            // Helmet up: the wearer gets both the night vision and thermal toggles.
            var helmet = toggleable.ClothingUids.First(kv => kv.Value == "head").Key;
            toggle.Invoke(toggles, new object[] { wearer, new Entity<ToggleableClothingComponent>(suit, toggleable), helmet });
            Assert.That(inventory.TryGetSlotEntity(wearer, "head", out var head) && head == helmet, "the helmet didn't deploy");
            var actions = entities.System<SharedActionsSystem>().GetActions(wearer).Select(a => a.Id).ToHashSet();
            Assert.That(entities.GetComponent<NightVisionComponent>(helmet).ToggleActionEntity is { } nv && actions.Contains(nv),
                "no night vision button with the helmet up");
            Assert.That(entities.GetComponent<ThermalVisionComponent>(helmet).ToggleActionEntity is { } th && actions.Contains(th),
                "no thermal button with the helmet up");
        });

        await pair.CleanReturnAsync();
    }
}
