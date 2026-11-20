using System;
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server._ReclamationProject.PowerArmor;
using Content.Server.Paint;
using Content.Shared._Misfits.WastelandMap;
using Content.Shared._ReclamationProject.PowerArmor;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Clothing;
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
using Content.Shared.RadialSelector;
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

    [Test]
    public async Task GarageStencilsUseThePlateModelAndOnlySpendPaintOnCompatibleArmor()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            EntityUid Spawn(string id) => em.SpawnEntity(id, map.GridCoords);
            var armor = em.System<ModularPowerArmorSystem>();
            foreach (var model in new[] { "N14ClothingOuterPowerArmorT45", "MisfitsT45ClothingOuterPowerArmorBoS",
                         "N14ClothingOuterPowerArmorT45Sierra", "AtomCatsT45Red", "AtomCatsT45Blue", "N14ClothingOuterPowerArmorRaider" })
                Assert.That(armor.CanApplyStencil(Spawn(model)), Is.True, model);

            var user = Spawn("MobHuman");
            var can = Spawn("AtomCatsPaintLowrider");
            var paint = em.GetComponent<PaintComponent>(can);
            Assert.That(em.System<OpenableSystem>().TryOpen(can, user: user));
            var solutions = em.System<SharedSolutionContainerSystem>();
            Assert.That(solutions.TryGetSolution(can, paint.Solution, out _, out var supply));
            var initial = supply!.Volume;
            void Spray(EntityUid target) => em.System<PaintSystem>().Paint((can, paint), target, user);

            var slots = em.System<ItemSlotsSystem>();
            var suit = Spawn("N14ClothingOuterPowerArmorT60");
            var advancedHelmet = slots.GetItemOrNull(suit, "plate-helmet")!.Value;
            Assert.That(slots.TryEject(suit, "plate-helmet", null, out _));
            foreach (var target in new[] { suit, advancedHelmet, Spawn("ReclamationT51PlateHelmet"),
                         Spawn("MisfitsClothingOuterPowerArmorBoS509HonorGuard"), Spawn("MisfitsEnclavePowerArmor"),
                         Spawn("AtomCatsPowerArmorFrame"), Spawn("ReclamationPowerArmorFrame") })
            {
                Assert.That(armor.CanApplyStencil(target), Is.False);
                Spray(target);
                Assert.That(supply.Volume, Is.EqualTo(initial));
            }

            var raider = Spawn("N14ClothingOuterPowerArmorRaider");
            var helmet = slots.GetItemOrNull(raider, "plate-helmet")!.Value;
            Assert.That(slots.TryEject(raider, "plate-helmet", null, out _));
            Assert.That(slots.TryInsert(suit, "plate-helmet", helmet, null));
            Spray(suit);
            Assert.That(supply.Volume, Is.EqualTo(initial - paint.ConsumptionUnit));
            var frame = em.GetComponent<ModularPowerArmorComponent>(suit);
            Assert.That(frame.Visuals[ArmorPlateSlot.Helmet], Does.EndWith("/lowrider.rsi"));
            Assert.That(frame.Visuals.Where(v => v.Key != ArmorPlateSlot.Helmet).All(v => v.Value.EndsWith("T60.rsi")));
            Assert.That(em.GetComponent<PowerArmorPlateComponent>(advancedHelmet).PaintFinish, Is.Null);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BottomlessCanRepaintsIndividualPiecesAndWholeSuitsThroughItsPicker()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var map = await pair.CreateTestMap();
        var server = pair.Server.ResolveDependency<IEntityManager>();
        var client = pair.Client.ResolveDependency<IEntityManager>();
        var session = pair.Server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);
        var tickRate = pair.Server.ResolveDependency<IGameTiming>().TickRate;
        var key = RadialSelectorUiKey.Key;
        var finishes = new[] { "AtomCatsPaintCherry", "AtomCatsPaintCobalt", "AtomCatsPaintMedic", "AtomCatsPaintLowrider" };
        var paths = new[] { "/red.rsi", "/blue.rsi", "/medic.rsi", "/lowrider.rsi" };
        EntityUid painter = default, can = default, suit = default, helmet = default, wornHelmet = default, bare = default;
        NetEntity netCan = default, netPainter = default;
        EntityUid Spawn(string id) => server.SpawnEntity(id, map.GridCoords);
        PowerArmorPlateComponent Plate(EntityUid uid) => server.GetComponent<PowerArmorPlateComponent>(uid);
        ModularPowerArmorComponent Frame(EntityUid uid) => server.GetComponent<ModularPowerArmorComponent>(uid);

        await pair.Server.WaitAssertion(() =>
        {
            painter = Spawn("MobHuman");
            netPainter = server.GetNetEntity(painter);
            pair.Server.PlayerMan.SetAttachedEntity(session, painter);
            suit = Spawn("N14ClothingOuterPowerArmorT45");
            bare = Spawn("ReclamationPowerArmorFrame");
            can = Spawn("AtomCatsStencilCan");
            netCan = server.GetNetEntity(can);
            var mechanic = Spawn("MobHuman");
            var visor = Spawn("AtomCatsModMechanicVisor");
            server.System<NpcFactionSystem>().AddFaction(mechanic, "AtomCats");
            var hands = server.System<SharedHandsSystem>();
            Assert.That(hands.TryPickup(mechanic, visor));
            server.EventBus.RaiseLocalEvent(suit, new InteractUsingEvent(mechanic, visor, suit, map.GridCoords));
            helmet = server.System<ItemSlotsSystem>().GetItemOrNull(suit, "plate-helmet")!.Value;
            server.System<DamageableSystem>().TryChangeDamage(helmet, new DamageSpecifier { DamageDict = { ["Blunt"] = 10 } }, ignoreResistances: true);
            var inventory = server.System<InventorySystem>();
            Assert.That(inventory.TryEquip(painter, suit, "outerClothing", force: true));
            Assert.That(inventory.TryGetSlotEntity(painter, "head", out var head));
            wornHelmet = head!.Value;
            Assert.That(hands.TryPickup(painter, can));
        });
        await pair.RunTicksSync(15);

        async Task OpenPicker(EntityUid target)
        {
            await pair.Server.WaitAssertion(() =>
            {
                Assert.That(server.System<SharedInteractionSystem>().InteractUsing(painter, can, target, map.GridCoords));
                Assert.That(server.System<SharedUserInterfaceSystem>().IsUiOpen(can, key, painter));
            });
            await pair.RunTicksSync(12);
            await pair.Client.WaitAssertion(() =>
            {
                var ui = client.System<SharedUserInterfaceSystem>();
                Assert.That(ui.TryGetUiState<RadialSelectorState>(client.GetEntity(netCan), key, out var state));
                Assert.That(state!.Entries.Select(e => e.Prototype), Is.EqualTo(finishes));
                Assert.That(state.Entries.All(e => e.Icon != null));
            });
        }

        async Task Choose(string stencil)
        {
            await pair.Client.WaitPost(() => client.System<SharedUserInterfaceSystem>().ClientSendUiMessage(
                client.GetEntity(netCan), key, new RadialSelectorSelectedMessage(stencil)));
            await pair.RunTicksSync(tickRate * 3);
        }

        async Task Paint(EntityUid target, int finish)
        {
            await OpenPicker(target);
            await Choose(finishes[finish]);
        }

        await Paint(painter, 0);
        await pair.Server.WaitAssertion(() => Assert.That(Frame(suit).Visuals.Values.All(v => v.EndsWith(paths[0]))));
        await Paint(wornHelmet, 2);
        await pair.Client.WaitAssertion(() =>
        {
            var user = client.GetEntity(netPainter);
            Assert.That(client.System<InventorySystem>().TryGetSlotEntity(user, "head", out var head));
            var visuals = new GetEquipmentVisualsEvent(user, "head");
            client.EventBus.RaiseLocalEvent(head!.Value, visuals);
            Assert.That(visuals.Layers.Any(layer => layer.Item2.State == "helmet" && layer.Item2.RsiPath!.EndsWith(paths[2])));
            Assert.That(visuals.Layers.Any(layer => layer.Item2.State == "mechanicvisor-worn"));
        });
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(Plate(helmet).PaintFinish, Does.EndWith(paths[2]));
            Assert.That(Frame(suit).Visuals[ArmorPlateSlot.Gloves], Does.EndWith(paths[0]));
            Assert.That(Frame(suit).ModVisuals[ArmorPlateSlot.Helmet], Is.EqualTo(ArmorModKind.MechanicVisor));
            Assert.That(Frame(suit).Rating, Is.EqualTo(45));
        });

        // A removed helmet must not redirect the pending selection to its replacement.
        await OpenPicker(wornHelmet);
        await pair.Server.WaitAssertion(() =>
        {
            var slots = server.System<ItemSlotsSystem>();
            Assert.That(slots.TryEject(suit, "plate-helmet", null, out _));
            Assert.That(slots.TryInsert(suit, "plate-helmet", Spawn("ReclamationPlateHelmet"), null));
        });
        await Choose(finishes[1]);
        await pair.Server.WaitAssertion(() => Assert.That(Frame(suit).PaintedHelmet, Is.False));

        await OpenPicker(helmet);
        await Choose("AtomCatsCut");
        await pair.Server.WaitAssertion(() => Assert.That(Plate(helmet).PaintFinish, Does.EndWith(paths[2])));
        await OpenPicker(helmet);
        await pair.Client.WaitPost(() => client.System<SharedUserInterfaceSystem>().ClientSendUiMessage(
            client.GetEntity(netCan), key, new RadialSelectorSelectedMessage(finishes[0])));
        await pair.RunTicksSync(8);
        await pair.Server.WaitAssertion(() => Assert.That(server.System<SharedHandsSystem>().TryDrop(painter, can)));
        await pair.RunTicksSync(tickRate * 3);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(Plate(helmet).PaintFinish, Does.EndWith(paths[2]));
            Assert.That(server.System<SharedHandsSystem>().TryPickup(painter, can));
        });
        for (var i = 0; i < finishes.Length; i++)
        {
            await Paint(helmet, i);
            await pair.Server.WaitAssertion(() => Assert.That(Plate(helmet).PaintFinish, Does.EndWith(paths[i])));
        }
        await pair.Server.WaitAssertion(() =>
        {
            var interaction = server.System<SharedInteractionSystem>();
            var ui = server.System<SharedUserInterfaceSystem>();
            var t51 = Spawn("N14ClothingOuterPowerArmorT51");
            foreach (var blocked in new[] { bare, t51, Spawn("ReclamationHonorGuardPlateHelmet") })
            {
                Assert.That(interaction.InteractUsing(painter, can, blocked, map.GridCoords));
                Assert.That(ui.IsUiOpen(can, key, painter), Is.False);
            }
            Assert.That(server.System<ItemSlotsSystem>().TryInsert(bare, "plate-gloves", Spawn("ReclamationPlateGloves"), null));
        });
        await OpenPicker(bare);
        await pair.Client.WaitPost(() => client.System<SharedUserInterfaceSystem>().ClientSendUiMessage(
            client.GetEntity(netCan), key, new RadialSelectorSelectedMessage(finishes[1])));
        await pair.RunTicksSync(8);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(server.GetComponent<DoAfterComponent>(painter).DoAfters.Values.Any(d => d.Args.Event is SprayArmorStencilEvent));
            var slots = server.System<ItemSlotsSystem>();
            Assert.That(slots.TryEject(bare, "plate-gloves", null, out _));
            Assert.That(slots.TryInsert(bare, "plate-gloves", Spawn("ReclamationT51PlateGloves"), null));
        });
        await pair.RunTicksSync(tickRate * 3);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(Frame(bare).Visuals[ArmorPlateSlot.Gloves], Does.EndWith("T51.rsi"));
            Assert.That(Frame(bare).FrameColor, Is.EqualTo(Robust.Shared.Maths.Color.White));
            Assert.That(Plate(helmet).FittedMod, Is.EqualTo(ArmorModKind.MechanicVisor));
            Assert.That(server.GetComponent<DamageableComponent>(helmet).TotalDamage.Int(), Is.EqualTo(10));
            Assert.That(server.System<SharedHandsSystem>().GetActiveItem(painter), Is.EqualTo(can));
            pair.Server.PlayerMan.SetAttachedEntity(session, null);
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
