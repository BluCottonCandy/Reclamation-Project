using System;
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server._ReclamationProject.PowerArmor;
using Content.Server.Paint;
using Content.Shared._Misfits.WastelandMap;
using Content.Shared._ReclamationProject.PowerArmor;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.DoAfter;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Systems;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Paint;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture]
public sealed class AtomCatsGarageSystemsTest
{
    [Test]
    public async Task RustbucketRevHasOneTwentySecondProgressBarAndStopsWhenInterrupted()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid user = default, suit = default;
        await pair.Server.WaitAssertion(() =>
        {
            user = em.SpawnEntity("MobHuman", map.GridCoords);
            suit = em.SpawnEntity("AtomCatsT45Red", map.GridCoords);
            Assert.That(em.HasComponent<RustbucketEngineComponent>(em.SpawnEntity("ReclamationPowerArmorFrame", map.GridCoords)), Is.False);
            Assert.That(em.System<InventorySystem>().TryEquip(user, suit, "outerClothing", force: true));
            var engine = em.GetComponent<RustbucketEngineComponent>(suit);
            Assert.That(engine.Action, Is.Not.Null);
            var rev = new RevRustbucketEvent { Performer = user };
            em.EventBus.RaiseLocalEvent(suit, rev);
            Assert.That(rev.Handled);
            var timer = engine.Rev!.Value;
            Assert.That(em.GetComponent<DoAfterComponent>(user).DoAfters[timer.Index].Args.Delay.TotalSeconds, Is.EqualTo(20));
            Assert.That(engine.Audio, Is.Not.Null);
            em.EventBus.RaiseLocalEvent(suit, new RevRustbucketEvent { Performer = user });
            Assert.That(engine.Rev, Is.EqualTo(timer));
            Assert.That(em.System<InventorySystem>().TryUnequip(user, "outerClothing", force: true));
            Assert.That(engine.Rev, Is.Null);
            Assert.That(engine.Audio, Is.Null);
        });
        await pair.RunTicksSync(3);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.System<InventorySystem>().TryEquip(user, suit, "outerClothing", force: true));
            em.EventBus.RaiseLocalEvent(suit, new RevRustbucketEvent { Performer = user });
            Assert.That(em.GetComponent<RustbucketEngineComponent>(suit).Audio, Is.Not.Null);
        });
        await pair.RunTicksSync(pair.Server.ResolveDependency<IGameTiming>().TickRate * 21);
        await pair.Server.WaitAssertion(() =>
        {
            var engine = em.GetComponent<RustbucketEngineComponent>(suit);
            Assert.That(engine.Rev, Is.Null);
            Assert.That(engine.Audio, Is.Null);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PaintedPlatesKeepTheirWearAndFinishWhenTransferred()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            EntityUid Spawn(string id) => em.SpawnEntity(id, map.GridCoords);
            var wearer = Spawn("MobHuman");
            var suit = Spawn("AtomCatsT45Blue");
            var slots = em.System<ItemSlotsSystem>();
            var helmet = slots.GetItemOrNull(suit, "plate-helmet")!.Value;
            var plate = em.GetComponent<PowerArmorPlateComponent>(helmet);
            var shell = em.GetComponent<ModularPowerArmorComponent>(suit);
            em.System<DamageableSystem>().TryChangeDamage(helmet, new DamageSpecifier { DamageDict = { ["Blunt"] = 10 } }, ignoreResistances: true);
            void Spray(string canId, EntityUid target)
            {
                var can = Spawn(canId);
                Assert.That(em.System<OpenableSystem>().TryOpen(can, user: wearer));
                em.System<PaintSystem>().Paint((can, em.GetComponent<PaintComponent>(can)), target, wearer);
            }
            Spray("AtomCatsPaintMedic", suit);
            Assert.That(shell.Visuals.Values.All(path => path.EndsWith("/medic.rsi")));
            Assert.That(shell.PaintedHelmet);
            Assert.That(slots.TryEject(suit, "plate-helmet", null, out _));
            Assert.That(shell.PaintedHelmet, Is.False);
            Spray("AtomCatsPaintLowrider", helmet);
            var next = Spawn("ReclamationPowerArmorFrame");
            Assert.That(slots.TryInsert(next, "plate-helmet", helmet, null));
            Assert.That(em.GetComponent<ModularPowerArmorComponent>(next).Visuals[ArmorPlateSlot.Helmet], Does.EndWith("/lowrider.rsi"));
            Assert.That(plate.Model.Id, Is.EqualTo("AtomCatsT45Blue"));
            Assert.That(em.GetComponent<DamageableComponent>(helmet).TotalDamage.Int(), Is.EqualTo(10));
            Assert.That(plate.HitPoints, Is.EqualTo(80));
        });
        await pair.CleanReturnAsync();
    }

    [TestCase("N14Stimpak", "back")]
    [TestCase("N14SuperStimpak", "belt")]
    [TestCase("N14StimpakDirty", "pocket1")]
    [TestCase("N14UltraStimpak", "pocket2")]
    public async Task InjectorSpendsRegularFirstAndWaitsForMedicineToClear(string fallback, string location)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid wearer = default, regular = default, spare = default;
        await pair.Server.WaitAssertion(() =>
        {
            EntityUid Spawn(string id) => em.SpawnEntity(id, map.GridCoords);
            wearer = Spawn("MobHuman");
            var mechanic = Spawn("MobHuman");
            var suit = Spawn("AtomCatsT45Red");
            var mod = Spawn("AtomCatsModAutoInjector");
            var inventory = em.System<InventorySystem>();
            em.System<NpcFactionSystem>().AddFaction(mechanic, "AtomCats");
            Assert.That(em.System<SharedHandsSystem>().TryPickup(mechanic, mod));
            em.EventBus.RaiseLocalEvent(suit, new InteractUsingEvent(mechanic, mod, suit, map.GridCoords));
            Assert.That(inventory.TryEquip(wearer, suit, "outerClothing", force: true));
            Assert.That(em.HasComponent<ActiveArmorAutoInjectorComponent>(wearer));
            regular = Spawn("N14Stimpak");
            spare = Spawn(fallback);
            void Equip(string slot, EntityUid item) => Assert.That(inventory.TryEquip(wearer, item, slot, force: true));
            if (location is "back" or "belt")
            {
                if (inventory.TryGetSlotEntity(wearer, location, out _))
                    Assert.That(inventory.TryUnequip(wearer, location, force: true));
                var bag = Spawn(location == "back" ? "ClothingBackpack" : "ClothingBeltMedical");
                Equip(location, bag);
                Assert.That(em.System<SharedStorageSystem>().Insert(bag, spare, out _));
            }
            else
                Equip(location, spare);
            Equip(location == "pocket2" ? "pocket1" : "pocket2", regular);
            var solutions = em.System<SharedSolutionContainerSystem>();
            Assert.That(solutions.TryGetInjectableSolution(wearer, out var blood, out _));
            solutions.TryAddReagent(blood!.Value, "RobustHealingMixture", 1);
            em.System<MobStateSystem>().ChangeMobState(wearer, MobState.Critical);
            float Volume(EntityUid syringe)
            {
                Assert.That(solutions.TryGetSolution(syringe, "pen", out _, out var dose));
                return dose!.Volume.Float();
            }
            Assert.That(Volume(regular), Is.EqualTo(25));
            Assert.That(Volume(spare), Is.GreaterThan(0));
            void CritAgain()
            {
                em.System<MobStateSystem>().ChangeMobState(wearer, MobState.Alive);
                em.System<MobStateSystem>().ChangeMobState(wearer, MobState.Critical);
            }
            solutions.RemoveAllSolution(blood.Value);
            CritAgain();
            // Two ordinary syringes have equal priority.
            if (fallback == "N14Stimpak" && Volume(spare) == 0)
                (regular, spare) = (spare, regular);
            Assert.That(Volume(regular), Is.Zero);
            var saved = Volume(spare);
            Assert.That(saved, Is.Positive);
            CritAgain();
            Assert.That(Volume(spare), Is.EqualTo(saved));
            solutions.RemoveAllSolution(blood.Value);
            CritAgain();
            Assert.That(Volume(spare), Is.Zero);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MedicalHudTogglesFollowersCasualtiesAndClosesWithHelmet()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        var client = pair.Client.ResolveDependency<IEntityManager>();
        var session = pair.Server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);
        EntityUid medic = default, mod = default, suit = default;
        EntityUid display = default;
        NetEntity netDisplay = default;
        void Toggle() => em.EventBus.RaiseLocalEvent(mod, new ActivateArmorModEvent { Performer = medic });
        await pair.Server.WaitAssertion(() =>
        {
            EntityUid Spawn(string id) => em.SpawnEntity(id, map.GridCoords);
            medic = Spawn("MobHuman");
            suit = Spawn("AtomCatsT45Blue");
            mod = Spawn("AtomCatsModMedicalHud");
            var casualty = Spawn("MobHuman");
            var minds = em.System<SharedMindSystem>();
            var mind = minds.CreateMind(session.UserId);
            minds.TransferTo(mind, casualty);
            em.System<MobStateSystem>().ChangeMobState(casualty, MobState.Dead);
            em.System<NpcFactionSystem>().AddFaction(medic, "AtomCats");
            Assert.That(em.System<SharedHandsSystem>().TryPickup(medic, mod));
            em.EventBus.RaiseLocalEvent(suit, new InteractUsingEvent(medic, mod, suit, map.GridCoords));
            Assert.That(em.System<InventorySystem>().TryEquip(medic, suit, "outerClothing", force: true));
            pair.Server.PlayerMan.SetAttachedEntity(session, medic);
        });
        await pair.RunTicksSync(20);
        await pair.Server.WaitAssertion(() =>
        {
            Toggle();
            display = em.GetComponent<PowerArmorModComponent>(mod).CasualtyDisplay!.Value;
            netDisplay = em.GetNetEntity(display);
        });
        await pair.RunTicksSync(15);
        await pair.Client.WaitAssertion(() =>
        {
            var ui = client.System<SharedUserInterfaceSystem>();
            var item = client.GetEntity(netDisplay);
            Assert.That(ui.IsUiOpen(item, WastelandMapUiKey.Key));
            Assert.That(ui.TryGetUiState<WastelandMapBoundUserInterfaceState>(item, WastelandMapUiKey.Key, out var state));
            Assert.That(state!.CompactHud);
            Assert.That(state.TrackedBlips.Any(blip => blip.Kind == WastelandMapTrackedBlipKind.DeadBody));
        });
        await pair.Server.WaitAssertion(() =>
        {
            Toggle();
            Assert.That(em.System<SharedUserInterfaceSystem>().IsUiOpen(display, WastelandMapUiKey.Key), Is.False);
            Toggle();
            Assert.That(em.System<ItemSlotsSystem>().TryEject(suit, "plate-helmet", null, out _));
            Assert.That(em.System<SharedUserInterfaceSystem>().IsUiOpen(display, WastelandMapUiKey.Key), Is.False);
            pair.Server.PlayerMan.SetAttachedEntity(session, null);
        });
        await pair.CleanReturnAsync();
    }
}
