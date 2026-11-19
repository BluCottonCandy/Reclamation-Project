using System.Linq;
using Content.Server._NC.Trade;
using Content.Server._ReclamationProject.Supplies;
using Content.Server.Roles;
using Content.Server.Station.Systems;
using Content.Shared._Misfits.Special;
using Content.Shared._NC.Trade;
using Content.Shared._ReclamationProject.PowerArmor;
using Content.Shared.Clothing.Loadouts.Prototypes;
using Content.Shared.Clothing.Loadouts.Systems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Inventory;
using Content.Shared.Mind;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture]
public sealed class ModularArmorRequisitionsTest
{
    [Test]
    public async Task FrameIsAnOptionalFifteenPointKnightAndPaladinOuterwearChoice()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
        await pair.Server.WaitAssertion(() =>
        {
            const string choiceId = "LoadoutBoSOuterPowerArmorFrame";
            var choice = prototypes.Index<LoadoutPrototype>(choiceId);
            Assert.That(choice.Cost, Is.EqualTo(15));
            Assert.That(choice.Category.Id, Is.EqualTo("Outer"));
            var funded = HumanoidCharacterProfile.DefaultWithSpecies().WithSpecial(new SpecialProfile { Charisma = 6 });
            var selected = funded.WithLoadoutPreference(choiceId, true);

            void Check(string jobId, HumanoidCharacterProfile profile, bool expectFrame)
            {
                var job = prototypes.Index<JobPrototype>(jobId);
                var wearer = em.SpawnEntity("MobHuman", map.GridCoords);
                em.System<StationSpawningSystem>().EquipStartingGear(wearer, job.StartingGear);
                var inventory = em.System<InventorySystem>();
                inventory.TryGetSlotEntity(wearer, "outerClothing", out var issued);
                var (_, granted) = em.System<SharedLoadoutSystem>().ApplyCharacterLoadout(
                    wearer, job, profile, [], false, out _);
                inventory.TryGetSlotEntity(wearer, "outerClothing", out var worn);
                var frames = granted.Where(item => item.Item2.LoadoutName == choiceId).ToArray();
                Assert.That(frames.Length, Is.EqualTo(expectFrame ? 1 : 0), jobId);
                if (expectFrame)
                {
                    Assert.That(worn, Is.EqualTo(frames[0].Item1), jobId);
                    Assert.That(em.GetComponent<MetaDataComponent>(worn!.Value).EntityPrototype!.ID, Is.EqualTo("ReclamationPowerArmorFrame"));
                    var frame = em.GetComponent<ModularPowerArmorComponent>(worn.Value);
                    Assert.That(frame.Rating, Is.EqualTo(20));
                    foreach (var slot in PowerArmorPlates.Slots)
                        Assert.That(em.System<ItemSlotsSystem>().GetItemOrNull(worn.Value, PowerArmorPlates.Container(slot)), Is.Null);
                }
                else
                    Assert.That(worn, Is.EqualTo(issued), jobId);
                em.DeleteEntity(wearer);
            }

            foreach (var rank in new[] { "BoSKnight", "BoSLancerKnight", "BoSMidKnight", "BoSHeadKnight",
                         "BoSPaladin", "BoSMidPaladin", "BoSMidPaladinCommander" })
                Check(rank, selected, true);
            foreach (var rank in new[] { "BoSInitiate", "BoSScribe", "BoSHonorGuard", "BoSWestElderCommander", "Wastelander" })
                Check(rank, selected, false);
            Check("BoSKnight", funded, false);
            Check("BoSPaladin", selected.WithSpecial(new SpecialProfile { Charisma = 5 }), false);
            Check("BoSKnight", selected.WithLoadoutPreference("N14LoadoutOuterJacketLettermanRed", true), false);
        });
        await pair.CleanReturnAsync();
    }

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
