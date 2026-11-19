using System.Linq;
using Content.Server._NC.Trade;
using Content.Server._ReclamationProject.Supplies;
using Content.Server.Roles;
using Content.Server.Station.Systems;
using Content.Shared._NC.Trade;
using Content.Shared._ReclamationProject.PowerArmor;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Inventory;
using Content.Shared.Mind;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture]
public sealed class ModularArmorRequisitionsTest
{
    [Test]
    public async Task HonorGuardStartsInTheFactoryAssembledSuit()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
        await pair.Server.WaitAssertion(() =>
        {
            const string donor = "MisfitsClothingOuterPowerArmorBoS509HonorGuard";
            var user = em.SpawnEntity("MobHuman", map.GridCoords);
            var job = prototypes.Index<JobPrototype>("BoSHonorGuard");
            em.System<StationSpawningSystem>().EquipStartingGear(user, job.StartingGear);
            var inv = em.System<InventorySystem>();
            Assert.That(inv.TryGetSlotEntity(user, "outerClothing", out var suit));
            var frame = suit!.Value;
            Assert.That(em.GetComponent<MetaDataComponent>(frame).EntityPrototype!.ID, Is.EqualTo(donor));
            var armor = em.GetComponent<ModularPowerArmorComponent>(frame);
            Assert.That(armor.Rating, Is.EqualTo(60));
            Assert.That(armor.Sealed);
            Assert.That(inv.TryGetSlotEntity(user, "head", out var helmet));
            Assert.That(em.GetComponent<MetaDataComponent>(helmet!.Value).EntityPrototype!.ID, Is.EqualTo("N14ClothingHeadHelmetPowerArmorT51BC"));
            foreach (var slot in PowerArmorPlates.Slots)
            {
                var item = em.System<ItemSlotsSystem>().GetItemOrNull(frame, PowerArmorPlates.Container(slot))!.Value;
                Assert.That(em.GetComponent<MetaDataComponent>(item).EntityPrototype!.ID, Is.EqualTo("ReclamationHonorGuardPlate" + slot));
                Assert.That(em.GetComponent<PowerArmorPlateComponent>(item).Model.Id, Is.EqualTo(donor));
                Assert.That(em.GetComponent<DamageableComponent>(item).TotalDamage.Float(), Is.Zero);
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ShelfPricesAndRankRestrictionsApplyToPurchases()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            const string bosPoints = "Supply points: Brotherhood";
            const string rrPoints = "Supply points: Railroad";
            var bos = em.SpawnEntity("ReclamationSupplyBrotherhoodVendor", map.GridCoords);
            var rr = em.SpawnEntity("ReclamationSupplyRailroadVendor", map.GridCoords);
            var bosStore = em.GetComponent<NcStoreComponent>(bos);
            var rrStore = em.GetComponent<NcStoreComponent>(rr);
            var supplies = em.System<FactionSupplySystem>();
            var checkout = em.System<NcStoreLogicSystem>();
            EntityUid Buyer(string job)
            {
                var user = em.SpawnEntity("MobHuman", map.GridCoords);
                var minds = em.System<SharedMindSystem>();
                var mind = minds.CreateMind(null, job);
                em.System<RoleSystem>().MindAddRole(mind, new JobComponent { Prototype = job }, silent: true);
                minds.TransferTo(mind, user);
                return user;
            }
            var knight = Buyer("BoSKnight");
            var headKnight = Buyer("BoSHeadKnight");
            var paladin = Buyer("BoSPaladin");
            var guard = Buyer("BoSHonorGuard");
            var elder = Buyer("BoSWestElderCommander");
            var initiate = Buyer("BoSInitiate");
            var intern = Buyer("RailroadSafehouse1Intern");
            NcStoreListingDef Listing(NcStoreComponent store, string product) =>
                store.Listings.Single(l => l.Mode == StoreMode.Buy && l.ProductEntity == product);
            Assert.That(Listing(bosStore, "ReclamationPowerArmorFrame").Cost[bosPoints], Is.EqualTo(150));
            Assert.That(Listing(rrStore, "ReclamationPowerArmorFrame").Cost[rrPoints], Is.EqualTo(200));
            Assert.That(supplies.CanPurchase(bos, initiate, "ReclamationPowerArmorFrame"), Is.False);
            foreach (var slot in PowerArmorPlates.Slots)
            {
                var t45 = "ReclamationPlate" + slot;
                var t51 = "ReclamationT51Plate" + slot;
                var honorGuard = "ReclamationHonorGuardPlate" + slot;
                Assert.That(Listing(bosStore, t45).Cost[bosPoints], Is.EqualTo(50));
                Assert.That(Listing(bosStore, t51).Cost[bosPoints], Is.EqualTo(75));
                Assert.That(Listing(bosStore, honorGuard).Cost[bosPoints], Is.EqualTo(100));
                Assert.That(Listing(rrStore, t45).Cost[rrPoints], Is.EqualTo(100));
                Assert.That(Listing(rrStore, t51).Cost[rrPoints], Is.EqualTo(150));
                Assert.That(rrStore.Listings.Any(l => l.ProductEntity == honorGuard), Is.False);
                Assert.That(supplies.CanPurchase(bos, knight, t45));
                Assert.That(supplies.CanPurchase(bos, paladin, t45));
                Assert.That(supplies.CanPurchase(bos, headKnight, t51), Is.False);
                Assert.That(supplies.CanPurchase(bos, knight, t51), Is.False);
                Assert.That(supplies.CanPurchase(bos, paladin, t51));
                Assert.That(supplies.CanPurchase(bos, guard, t51));
                Assert.That(supplies.CanPurchase(bos, guard, honorGuard));
                Assert.That(supplies.CanPurchase(bos, elder, honorGuard), Is.False);
                Assert.That(supplies.CanPurchase(bos, paladin, honorGuard), Is.False);
            }
            Assert.That(rrStore.Listings.Where(l => l.Mode == StoreMode.Buy).All(l => supplies.CanPurchase(rr, intern, l.ProductEntity)));
            Assert.That(bosStore.Listings.Concat(rrStore.Listings).Any(l => l.ProductEntity == "N14ClothingOuterPowerArmorT45"), Is.False);

            supplies.Credit(bosPoints, 200);
            var balance = supplies.Balance(bosPoints);
            var helmet = Listing(bosStore, "ReclamationT51PlateHelmet");
            Assert.That(checkout.TryBuy(helmet.Id, bos, bosStore, knight), Is.False);
            Assert.That(supplies.Balance(bosPoints), Is.EqualTo(balance));
            Assert.That(checkout.TryBuy(helmet.Id, bos, bosStore, paladin));
            Assert.That(supplies.Balance(bosPoints), Is.EqualTo(balance - 75));
            supplies.Credit(rrPoints, 200);
            Assert.That(checkout.TryBuy(Listing(rrStore, "ReclamationPowerArmorFrame").Id, rr, rrStore, intern));
            Assert.That(supplies.Balance(rrPoints), Is.Zero);
        });
        await pair.CleanReturnAsync();
    }
}
