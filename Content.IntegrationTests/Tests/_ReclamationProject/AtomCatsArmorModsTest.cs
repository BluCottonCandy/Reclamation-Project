using System.Linq;
using Content.Client.Overlays;
using Content.IntegrationTests.Pair;
using Content.Server._ReclamationProject.PowerArmor;
using Content.Shared._ReclamationProject.PowerArmor;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Examine;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Movement.Components;
using Content.Shared.Actions;
using Content.Shared.Throwing;
using Content.Shared.Tools.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Item.ItemToggle;
using Content.Shared.NPC.Systems;
using Content.Shared.Stacks;
using Content.Shared.Verbs;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using Robust.Client.Graphics;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture]
public sealed class AtomCatsArmorModsTest
{
    [Test]
    public async Task ReplacingTheVisorUpdatesTheWearersHud()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var map = await pair.CreateTestMap();
        var server = pair.Server.ResolveDependency<IEntityManager>();
        var client = pair.Client.ResolveDependency<IEntityManager>();
        var light = pair.Client.ResolveDependency<ILightManager>();
        var session = pair.Server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);
        EntityUid suit = default, cat = default, customer = default;
        EntityUid FitVisor(ArmorModKind kind)
        {
            var module = server.SpawnEntity("AtomCatsMod"+kind, map.GridCoords);
            Assert.That(server.System<SharedHandsSystem>().TryPickup(cat, module));
            server.EventBus.RaiseLocalEvent(suit, new InteractUsingEvent(cat, module, suit, map.GridCoords));
            return module;
        }
        await pair.Server.WaitAssertion(() =>
        {
            cat = server.SpawnEntity("MobHuman", map.GridCoords);
            customer = server.SpawnEntity("MobHuman", map.GridCoords);
            server.System<NpcFactionSystem>().AddFaction(cat, "AtomCats");
            suit = server.SpawnEntity("N14ClothingOuterPowerArmorT45", map.GridCoords);
            Assert.That(server.System<InventorySystem>().TryEquip(customer, suit, "outerClothing", force: true));
            pair.Server.PlayerMan.SetAttachedEntity(session, customer);
            FitVisor(ArmorModKind.MedicalHud);
        });
        await pair.RunTicksSync(20);
        await pair.Client.WaitAssertion(() =>
            Assert.That(client.System<ShowHealthIconsSystem>().DamageContainers, Does.Contain("Biological")));
        await pair.Server.WaitAssertion(() =>
        {
            var night = FitVisor(ArmorModKind.NightVision);
            Assert.That(server.System<PowerArmorModSystem>().Fitted(suit).Single(), Is.EqualTo(night));
            server.EventBus.RaiseLocalEvent(night, new ActivateArmorModEvent { Performer = customer });
        });
        await pair.RunTicksSync(20);
        await pair.Client.WaitAssertion(() =>
        {
            Assert.That(light.DrawLighting, Is.False);
            Assert.That(client.System<ShowHealthIconsSystem>().DamageContainers, Does.Not.Contain("Biological"));
        });
        await pair.Server.WaitAssertion(() => Assert.That(server.System<ItemSlotsSystem>().TryEject(suit, "plate-helmet", null, out _)));
        await pair.RunTicksSync(12);
        await pair.Client.WaitAssertion(() =>
        {
            Assert.That(light.DrawLighting, Is.True);
            Assert.That(client.System<ShowHealthIconsSystem>().DamageContainers, Does.Not.Contain("Biological"));
        });
        await pair.Server.WaitPost(() => pair.Server.PlayerMan.SetAttachedEntity(session, null));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ConstructInOrderAndTransferBetweenChassis()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var em = server.ResolveDependency<IEntityManager>();
        var mods = em.System<PowerArmorModSystem>();
        var slots = em.System<ItemSlotsSystem>();
        var hands = em.System<SharedHandsSystem>();
        var rate = (int)server.ResolveDependency<IGameTiming>().TickRate;
        EntityUid cat = default, outsider = default, plate = default, used = default, oldMod = default;
        EntityUid Spawn(string proto) => em.SpawnEntity(proto, map.GridCoords);
        Entity<PowerArmorPlateComponent> Plate() => (plate, em.GetComponent<PowerArmorPlateComponent>(plate));
        var inputs = new[] { "Wrench", "SheetSteel", "Welder", "N14Scrap", "Screwdriver", "N14IngotGold5", "Wirecutter" };
        int[] seconds = [2, 3, 4, 4, 2, 3, 2];
        int[] amounts = [0, 10, 0, 20, 0, 2, 0];
        await server.WaitAssertion(() =>
        {
            cat = Spawn("MobHuman");
            em.System<NpcFactionSystem>().AddFaction(cat, "AtomCats");
            outsider = Spawn("MobHuman");
            plate = Spawn("ReclamationPlateGloves");
            oldMod = Spawn("AtomCatsModWelder");
            Assert.That(hands.TryPickup(cat, oldMod));
            em.EventBus.RaiseLocalEvent(plate, new InteractUsingEvent(cat, oldMod, plate, map.GridCoords));
            var wrench = Spawn("Wrench");
            Assert.That(hands.TryPickup(outsider, wrench));
            Assert.That(mods.StartStep(Plate(), ArmorModKind.Shotgun, outsider, wrench), Is.False);
            var wrong = Spawn("SheetSteel");
            Assert.That(hands.TryPickup(cat, wrong));
            Assert.That(mods.StartStep(Plate(), ArmorModKind.Shotgun, cat, wrong), Is.False);
            hands.TryDrop(cat, wrong);
            em.DeleteEntity(wrong);
        });
        for (var i = 0; i < inputs.Length; i++)
        {
            var stage = i;
            var before = 0;
            await server.WaitAssertion(() =>
            {
                if (stage == 5)
                {
                    var stationGold = Spawn("IngotGold");
                    Assert.That(hands.TryPickup(cat, stationGold));
                    var originalCount = em.GetComponent<StackComponent>(stationGold).Count;
                    Assert.That(mods.StartStep(Plate(), ArmorModKind.Shotgun, cat, stationGold), Is.False);
                    Assert.That(em.GetComponent<StackComponent>(stationGold).Count, Is.EqualTo(originalCount));
                    Assert.That(hands.TryDrop(cat, stationGold));
                    em.DeleteEntity(stationGold);
                }
                used = Spawn(inputs[stage]);
                Assert.That(hands.TryPickup(cat, used));
                if (stage == 0)
                {
                    var verbs = em.System<SharedVerbSystem>().GetLocalVerbs(plate, cat, typeof(AlternativeVerb));
                    Assert.That(verbs.Any(v => v.Text.Contains("wrist shotgun")), "missing wrench build menu");
                }
                if (stage == 2)
                {
                    Assert.That(mods.StartStep(Plate(), ArmorModKind.Shotgun, cat, used), Is.False, "unlit welder");
                    em.System<ItemToggleSystem>().TryActivate(used, cat);
                }
                if (amounts[stage] > 0) before = em.GetComponent<StackComponent>(used).Count;
                Assert.That(mods.StartStep(Plate(), ArmorModKind.Shotgun, cat, used), $"stage {stage}");
            });
            await server.WaitRunTicks((seconds[i] - 1) * rate);
            await server.WaitAssertion(() => Assert.That(Plate().Comp.BuildStep, Is.EqualTo(stage), "step finished early"));
            await server.WaitRunTicks(rate + 3);
            await server.WaitAssertion(() =>
            {
                Assert.That(Plate().Comp.BuildStep, Is.EqualTo((stage + 1) % 7), $"step {stage} did not finish");
                Assert.That(em.EntityExists(oldMod), Is.EqualTo(stage < 6));
                if (amounts[stage] > 0) Assert.That(em.GetComponent<StackComponent>(used).Count, Is.EqualTo(before - amounts[stage]));
                hands.TryDrop(cat, used);
                em.DeleteEntity(used);
            });
        }
        await server.WaitAssertion(() =>
        {
            var standard = Spawn("ReclamationPowerArmorFrame");
            var rusted = Spawn("AtomCatsPowerArmorFrame");
            var extra = Spawn("ReclamationPlateHelmet");
            var visor = Spawn("AtomCatsModMedicalHud");
            Assert.That(hands.TryPickup(cat, visor));
            em.EventBus.RaiseLocalEvent(extra, new InteractUsingEvent(cat, visor, extra, map.GridCoords));
            Assert.That(slots.TryInsert(standard, "plate-gloves", plate, null));
            Assert.That(slots.TryInsert(standard, "plate-helmet", extra, null), Is.False);
            Assert.That(slots.TryEject(standard, "plate-gloves", null, out _));
            Assert.That(slots.TryInsert(rusted, "plate-helmet", extra, null));
            Assert.That(slots.TryInsert(rusted, "plate-gloves", plate, null));
            Assert.That(mods.Fitted(rusted).Count(), Is.EqualTo(2));
            Assert.That(em.System<InventorySystem>().TryEquip(outsider, rusted, "outerClothing", force:true));
            var module = mods.Fitted(rusted).Single(uid => em.GetComponent<PowerArmorModComponent>(uid).Kind == ArmorModKind.Shotgun);
            var data = em.GetComponent<PowerArmorModComponent>(module);
            Assert.That(data.Wearer, Is.EqualTo(outsider));
            Assert.That(em.GetComponent<PowerArmorModWearerComponent>(outsider).MedicalHud);
            hands.TryDrop(outsider);
            var extend = new ActivateArmorModEvent { Performer = outsider };
            em.EventBus.RaiseLocalEvent(module, extend);
            Assert.That(extend.Handled);
            Assert.That(hands.GetActiveItem(outsider), Is.EqualTo(data.ToolEntity));
            Assert.That(em.GetComponent<BallisticAmmoProviderComponent>(data.ToolEntity!.Value).Capacity, Is.EqualTo(5));
            em.System<DamageableSystem>().TryChangeDamage(plate, new DamageSpecifier { DamageDict = { ["Blunt"] = 1000 } }, ignoreResistances:true);
            Assert.That(slots.GetItemOrNull(rusted, "plate-gloves"), Is.Null);
            Assert.That(data.Wearer, Is.Null);
            Assert.That(hands.GetActiveItem(outsider), Is.Null);
            Assert.That(em.GetComponent<TransformComponent>(data.ToolEntity.Value).ParentUid, Is.EqualTo(module));
        });
        await pair.CleanReturnAsync();
    }

    [TestCase("AtomCatsT45Red")]
    [TestCase("AtomCatsT45Blue")]
    public async Task ReplacingGauntletModOnFullRustbucketKeepsOtherPlate(string proto)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            EntityUid Spawn(string p) => em.SpawnEntity(p, map.GridCoords);
            var frame = Spawn(proto);
            var shell = em.GetComponent<ModularPowerArmorComponent>(frame);
            Assert.That(shell.ModCapacity, Is.EqualTo(2));
            Assert.That(shell.FrameSprite, Does.Contain("AtomCats/frame.rsi"));
            Assert.That(shell.Rating, Is.EqualTo(40));
            var wearer = Spawn("MobHuman");
            var maker = Spawn("MobHuman");
            em.System<NpcFactionSystem>().AddFaction(maker, "AtomCats");
            var hands = em.System<SharedHandsSystem>();
            EntityUid Fit(string mod, EntityUid user)
            {
                var item = Spawn("AtomCatsMod"+mod);
                Assert.That(hands.TryPickup(user, item));
                em.EventBus.RaiseLocalEvent(frame, new InteractUsingEvent(user, item, frame, map.GridCoords));
                return item;
            }
            var failed = Fit("Knuckles", wearer);
            Assert.That(hands.IsHolding(wearer, failed));
            hands.TryDrop(wearer);
            var claw = Fit("Deathclaw", maker);
            var helmet = Fit("MedicalHud", maker);
            Assert.That(em.System<InventorySystem>().TryEquip(wearer, frame, "outerClothing", force:true));
            void Select(EntityUid item, string type, int amount)
            {
                em.EventBus.RaiseLocalEvent(item, new ActivateArmorModEvent { Performer = wearer });
                Assert.That(em.System<SharedMeleeWeaponSystem>().TryGetWeapon(wearer, out _, out var melee));
                Assert.That(melee!.Damage.DamageDict[type].Int(), Is.EqualTo(amount));
            }
            Select(claw, "Slash", 24);
            var knuckles = Fit("Knuckles", maker);
            Select(knuckles, "Structural", 50);
            Assert.That(em.EntityExists(claw), Is.False);
            var wrist = Fit("Welder", maker);
            em.EventBus.RaiseLocalEvent(wrist, new ActivateArmorModEvent { Performer = wearer });
            var tool = hands.GetActiveItem(wearer);
            Assert.That(tool, Is.Not.Null);
            var replacement = Fit("Flamer", maker);
            Assert.That(hands.GetActiveItem(wearer), Is.Null);
            Assert.That(em.EntityExists(wrist), Is.False);
            Assert.That(em.System<PowerArmorModSystem>().Fitted(frame), Is.EquivalentTo(new[] { helmet, replacement }));
            var third = Fit("Jump", maker);
            Assert.That(hands.IsHolding(maker, third));
            Assert.That(em.System<PowerArmorModSystem>().Fitted(frame).Count(), Is.EqualTo(2));
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(ArmorModKind.NightVision)]
    [TestCase(ArmorModKind.MedicalHud)]
    [TestCase(ArmorModKind.MechanicVisor)]
    [TestCase(ArmorModKind.Overdrive)]
    [TestCase(ArmorModKind.Jump)]
    [TestCase(ArmorModKind.Welder)]
    [TestCase(ArmorModKind.Flamer)]
    public async Task EffectsFollowThePlate(ArmorModKind kind)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            EntityUid Spawn(string p) => em.SpawnEntity(p, map.GridCoords);
            var frame = Spawn("N14ClothingOuterPowerArmorT45");
            var wearer = Spawn("MobHuman");
            var cat = Spawn("MobHuman");
            em.System<NpcFactionSystem>().AddFaction(cat, "AtomCats");
            var item = Spawn("AtomCatsMod"+kind);
            Assert.That(em.System<InventorySystem>().TryEquip(wearer, frame, "outerClothing", force:true));
            var speed = em.GetComponent<MovementSpeedModifierComponent>(wearer);
            var baseline = speed.WalkSpeedModifier;
            var hands = em.System<SharedHandsSystem>();
            Assert.That(hands.TryPickup(cat, item));
            em.EventBus.RaiseLocalEvent(frame, new InteractUsingEvent(cat, item, frame, map.GridCoords));
            var mod = em.GetComponent<PowerArmorModComponent>(item);
            var hud = em.GetComponent<PowerArmorModWearerComponent>(wearer);
            Assert.That(mod.Wearer, Is.EqualTo(wearer));
            var timing = pair.Server.ResolveDependency<IGameTiming>();
            void Activate()
            {
                var action = em.GetComponent<InstantActionComponent>(mod.ActionEntity!.Value);
                em.System<SharedActionsSystem>().PerformAction(wearer, null, mod.ActionEntity.Value, action, action.Event, timing.CurTime);
            }
            switch (kind)
            {
                case ArmorModKind.NightVision:
                    Activate();
                    Assert.That(hud.NightVision);
                    break;
                case ArmorModKind.MedicalHud:
                    Assert.That(hud.MedicalHud);
                    break;
                case ArmorModKind.MechanicVisor:
                    var scan = new ExaminedEvent(new FormattedMessage(), frame, wearer, true, false);
                    em.EventBus.RaiseLocalEvent(frame, scan);
                    Assert.That(scan.GetTotalMessage().ToString(), Does.Contain("Fusion core").And.Contain("HP"));
                    break;
                case ArmorModKind.Overdrive:
                    Assert.That(speed.WalkSpeedModifier, Is.EqualTo(baseline * 1.1f).Within(0.001f));
                    break;
                case ArmorModKind.Jump:
                    Activate();
                    Assert.That(em.HasComponent<ThrownItemComponent>(wearer));
                    var jump = em.GetComponent<InstantActionComponent>(mod.ActionEntity!.Value);
                    Assert.That(jump.Cooldown!.Value.End - jump.Cooldown.Value.Start, Is.EqualTo(TimeSpan.FromSeconds(30)));
                    break;
                case ArmorModKind.Flamer:
                case ArmorModKind.Welder:
                    Activate();
                    var tool = hands.GetActiveItem(wearer);
                    Assert.That(tool, Is.EqualTo(mod.ToolEntity).And.Not.Null);
                    if (kind == ArmorModKind.Welder)
                        Assert.That(em.HasComponent<WelderComponent>(tool!.Value));
                    else
                    {
                        Assert.That(em.System<SharedSolutionContainerSystem>().TryGetSolution(tool!.Value, "chamber", out _, out var fuel));
                        Assert.That(fuel!.MaxVolume.Int(), Is.EqualTo(50));
                        Assert.That(fuel.Volume.Int(), Is.EqualTo(50));
                    }
                    break;
            }
            Assert.That(em.System<ItemSlotsSystem>().TryEject(frame, PowerArmorPlates.Container(mod.Slot), null, out _));
            Assert.That(mod.Wearer, Is.Null);
            Assert.That(hud.MedicalHud || hud.NightVision, Is.False);
            Assert.That(hands.GetActiveItem(wearer), Is.Null);
            if (kind == ArmorModKind.Overdrive)
                Assert.That(speed.WalkSpeedModifier, Is.EqualTo(baseline).Within(0.001f));
        });
        await pair.CleanReturnAsync();
    }
}
