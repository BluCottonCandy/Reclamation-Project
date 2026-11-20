using System.Linq;
using Content.Server._NC.Trade;
using Content.Server._ReclamationProject.Supplies;
using Content.Server.Roles;
using Content.Server.Station.Systems;
using Content.Shared._Misfits.Special;
using Content.Shared._NC.Trade;
using Content.Shared._ReclamationProject.PowerArmor;
using Content.Shared._ReclamationProject.Supplies;
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
    public async Task AtomCatsBuyUnmodifiedArmorFromTheirSharedGarageBudget()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        var protos = pair.Server.ResolveDependency<IPrototypeManager>();
        await pair.Server.WaitAssertion(() =>
        {
            const string currency = "Supply points: Atom Cats";
            const string rrCurrency = "Supply points: Railroad";
            var machine = em.SpawnEntity("ReclamationSupplyAtomCatsVendor", map.GridCoords);
            var store = em.GetComponent<NcStoreComponent>(machine);
            var railroad = em.GetComponent<NcStoreComponent>(em.SpawnEntity("ReclamationSupplyRailroadVendor", map.GridCoords));
            var supplies = em.System<FactionSupplySystem>();
            var checkout = em.System<NcStoreLogicSystem>();
            var slots = em.System<ItemSlotsSystem>();
            var minds = em.System<SharedMindSystem>();
            const string frameId = "AtomCatsPowerArmorFrame";
            EntityUid Buyer(string job)
            {
                var user = em.SpawnEntity("MobHuman", map.GridCoords);
                var mind = minds.CreateMind(null, job);
                em.System<RoleSystem>().MindAddRole(mind, new JobComponent { Prototype = job }, silent: true);
                minds.TransferTo(mind, user);
                return user;
            }
            NcStoreListingDef Listing(NcStoreComponent shop, string product) =>
                shop.Listings.Single(l => l.Mode == StoreMode.Buy && l.ProductEntity == product);
            EntityUid Issued(string product) => em.EntityQuery<FactionSupplyPurchaseComponent, MetaDataComponent>()
                .Single(e => e.Item2.EntityPrototype?.ID == product).Item2.Owner;

            var lookout = Buyer("AtomCatsLookout");
            var outsider = Buyer("Wastelander");
            var gun = Listing(store, "AtomCatsRailroadRifle");
            Assert.That(supplies.CanPurchase(machine, lookout, gun.ProductEntity), Is.False);
            Assert.That(supplies.CanPurchase(machine, Buyer("AtomCatsHotRod"), gun.ProductEntity));
            Assert.That(checkout.TryBuy(Listing(store, "AtomCatsPlateHelmet").Id, machine, store, outsider), Is.False);
            foreach (var rank in protos.Index<DepartmentPrototype>("AtomCats").Roles)
            {
                var member = Buyer(rank.Id);
                Assert.That(supplies.CanPurchase(machine, member, frameId), Is.True, rank.Id);
                Assert.That(supplies.CanPurchase(machine, member, "WastelandPowerArmor"), Is.True, rank.Id);
            }
            Assert.That(store.Listings.Where(l => l.Mode == StoreMode.Buy)
                .Any(l => protos.Index<EntityPrototype>(l.ProductEntity).Components.ContainsKey("PowerArmorMod")), Is.False);
            Assert.That(store.Listings.Where(l => l.Mode == StoreMode.Sell).Select(l => l.Cost[currency]),
                Is.EquivalentTo(new[] { 20, 50, 100 }));
            Assert.That(Listing(store, frameId).Cost[currency], Is.EqualTo(200));
            Assert.That(Listing(store, "WastelandPowerArmor").Cost[currency], Is.EqualTo(150));
            Assert.That(Listing(railroad, frameId).Cost[rrCurrency], Is.EqualTo(200));
            Assert.That(Listing(railroad, "WastelandPowerArmor").Cost[rrCurrency], Is.EqualTo(200));

            supplies.Credit(currency, 200);
            Assert.That(checkout.TryBuy(Listing(store, "WastelandPowerArmor").Id, machine, store, lookout));
            Assert.That(supplies.Balance(currency), Is.EqualTo(50));
            Assert.That(checkout.TryBuy(Listing(store, frameId).Id, machine, store, lookout), Is.False);
            supplies.Credit(currency, 200);
            Assert.That(checkout.TryBuy(Listing(store, frameId).Id, machine, store, lookout));
            var frame = Issued(frameId);
            var armor = em.GetComponent<ModularPowerArmorComponent>(frame);
            Assert.That(armor.Rating, Is.EqualTo(20));
            Assert.That(armor.ModCapacity, Is.EqualTo(2));
            supplies.Credit(currency, 200);
            foreach (var slot in PowerArmorPlates.Slots)
            {
                var id = "AtomCatsPlate" + slot;
                Assert.That(Listing(store, id).Cost[currency], Is.EqualTo(50));
                Assert.That(Listing(railroad, id).Cost[rrCurrency], Is.EqualTo(100));
                Assert.That(checkout.TryBuy(Listing(store, id).Id, machine, store, lookout));
                var plate = Issued(id);
                Assert.That(em.GetComponent<PowerArmorPlateComponent>(plate).FittedMod, Is.Null);
                Assert.That(slots.TryInsert(frame, PowerArmorPlates.Container(slot), plate, null));
            }
            Assert.That(supplies.Balance(currency), Is.Zero);
            Assert.That(armor.Sealed);
            Assert.That(armor.Rating, Is.EqualTo(40));
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
