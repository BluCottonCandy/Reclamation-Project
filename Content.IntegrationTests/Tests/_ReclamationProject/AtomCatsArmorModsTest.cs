using System.Linq;
using System.Numerics;
using Content.Client.Overlays;
using Content.IntegrationTests.Pair;
using Content.Server._ReclamationProject.PowerArmor;
using Content.Server.Gravity;
using Content.Shared._ReclamationProject.PowerArmor;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Examine;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Movement.Components;
using Content.Shared.Actions;
using Content.Shared.Clothing;
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
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Tools.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using Robust.Client.Graphics;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture]
public sealed class AtomCatsArmorModsTest
{
    [Test]
    public async Task DiagnosticVisorTracksAnotherWearersPlates()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var map = await pair.CreateTestMap();
        var server = pair.Server.ResolveDependency<IEntityManager>();
        var client = pair.Client.ResolveDependency<IEntityManager>();
        var overlays = pair.Client.ResolveDependency<IOverlayManager>();
        var session = pair.Server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);
        EntityUid viewerSuit = default, target = default, targetSuit = default, chest = default;
        NetEntity netTarget = default, netUnarmored = default;
        await pair.Server.WaitAssertion(() =>
        {
            EntityUid Spawn(string prototype) => server.SpawnEntity(prototype, map.GridCoords);
            var mechanic = Spawn("MobHuman");
            target = Spawn("MobHuman");
            netTarget = server.GetNetEntity(target);
            netUnarmored = server.GetNetEntity(Spawn("MobHuman"));
            viewerSuit = Spawn("AtomCatsT45Blue");
            targetSuit = Spawn("N14ClothingOuterPowerArmorT45");
            var visor = Spawn("AtomCatsModMechanicVisor");
            server.System<NpcFactionSystem>().AddFaction(mechanic, "AtomCats");
            Assert.That(server.System<SharedHandsSystem>().TryPickup(mechanic, visor));
            server.EventBus.RaiseLocalEvent(viewerSuit, new InteractUsingEvent(mechanic, visor, viewerSuit, map.GridCoords));
            var inventory = server.System<InventorySystem>();
            Assert.That(inventory.TryEquip(mechanic, viewerSuit, "outerClothing", force: true));
            Assert.That(inventory.TryEquip(target, targetSuit, "outerClothing", force: true));
            chest = server.System<ItemSlotsSystem>().GetItemOrNull(targetSuit, "plate-chest")!.Value;
            pair.Server.PlayerMan.SetAttachedEntity(session, mechanic);
        });
        async Task Check(float? expected)
        {
            await pair.RunTicksSync(15);
            await pair.Client.WaitAssertion(() =>
            {
                var overlay = overlays.GetOverlay<EntityHealthBarOverlay>();
                Assert.That(overlay.DamageContainers, Does.Not.Contain("Biological"));
                Assert.That(overlay.GetArmorCondition(client.GetEntity(netUnarmored)), Is.Null);
                var reading = overlay.GetArmorCondition(client.GetEntity(netTarget));
                if (expected.HasValue)
                    Assert.That(reading, Is.EqualTo(expected.Value).Within(.0001f));
                else
                    Assert.That(reading, Is.Null);
            });
        }
        void Hit(EntityUid uid, string type, int amount) =>
            server.System<DamageableSystem>().TryChangeDamage(uid, new DamageSpecifier { DamageDict = { [type] = amount } },
                ignoreResistances: true, doPartDamage: false);
        await Check(1f);
        await pair.Server.WaitPost(() => Hit(target, "Poison", 10));
        await Check(1f);
        await pair.Server.WaitPost(() => Hit(chest, "Blunt", 120));
        await Check(.76f);
        await pair.Server.WaitPost(() => Hit(chest, "Blunt", -120));
        await Check(1f);
        await pair.Server.WaitPost(() => Hit(chest, "Blunt", 240));
        await Check(.52f);
        await pair.Server.WaitAssertion(() =>
            Assert.That(server.System<InventorySystem>().TryUnequip(target, "outerClothing", force: true)));
        await Check(null);
        await pair.Server.WaitAssertion(() =>
            Assert.That(server.System<ItemSlotsSystem>().TryEject(viewerSuit, "plate-helmet", null, out _)));
        await pair.RunTicksSync(15);
        await pair.Client.WaitAssertion(() => Assert.That(overlays.HasOverlay<EntityHealthBarOverlay>(), Is.False));
        await pair.Server.WaitPost(() => pair.Server.PlayerMan.SetAttachedEntity(session, null));
        await pair.CleanReturnAsync();
    }

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
        NetEntity netWearer = default;
        await pair.Server.WaitAssertion(() =>
        {
            var shotgun = FitVisor(ArmorModKind.Shotgun);
            server.EventBus.RaiseLocalEvent(shotgun, new ActivateArmorModEvent { Performer = customer });
            netWearer = server.GetNetEntity(customer);
        });
        await pair.RunTicksSync(20);
        await pair.Client.WaitAssertion(() =>
        {
            var user = client.GetEntity(netWearer);
            Assert.That(client.System<SharedHandsSystem>().GetActiveItem(user), Is.Null);
            Assert.That(client.System<SharedGunSystem>().TryGetGun(user, out var gun, out _));
            Assert.That(client.GetComponent<MetaDataComponent>(gun).EntityPrototype!.ID, Is.EqualTo("AtomCatsWristShotgun"));
            Assert.That(client.System<InventorySystem>().TryGetSlotEntity(user, "outerClothing", out var outer));
            var visuals = new GetEquipmentVisualsEvent(user, "outerClothing");
            client.EventBus.RaiseLocalEvent(outer!.Value, visuals);
            Assert.That(visuals.Layers.Any(layer => layer.Item2.State == "shotgun-worn"));
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
            Assert.That(hands.GetActiveItem(outsider), Is.Null);
            var guns = em.System<SharedGunSystem>();
            Assert.That(guns.TryGetGun(outsider, out var activeGun, out var shotgun));
            Assert.That(activeGun, Is.EqualTo(data.ToolEntity));
            var magazine = em.GetComponent<BallisticAmmoProviderComponent>(data.ToolEntity!.Value);
            Assert.That(magazine.Capacity, Is.EqualTo(5));
            guns.AttemptShoot(outsider, activeGun, shotgun!, map.GridCoords.Offset(new System.Numerics.Vector2(3, 0)));
            Assert.That(magazine.AmmoCount, Is.EqualTo(4));
            var shell = Spawn("N14ShellShotgun12");
            Assert.That(hands.TryPickup(outsider, shell));
            Assert.That(guns.TryGetGun(outsider, out _, out _), Is.False);
            Assert.That(em.System<SharedInteractionSystem>().InteractUsing(outsider, shell, rusted, map.GridCoords));
            Assert.That(magazine.AmmoCount, Is.EqualTo(5));
            em.System<DamageableSystem>().TryChangeDamage(plate, new DamageSpecifier { DamageDict = { ["Blunt"] = 1000 } }, ignoreResistances:true);
            Assert.That(slots.GetItemOrNull(rusted, "plate-gloves"), Is.EqualTo(plate));
            Assert.That(data.Wearer, Is.EqualTo(outsider));
            Assert.That(hands.GetActiveItem(outsider), Is.Null);
            Assert.That(guns.TryGetGun(outsider, out var stillActive, out _));
            Assert.That(stillActive, Is.EqualTo(data.ToolEntity));
            Assert.That(mods.Fitted(rusted).Count(), Is.EqualTo(2));
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
            Assert.That(em.GetComponent<PowerArmorModComponent>(claw).ActionEntity, Is.Null);
            Assert.That(em.System<SharedMeleeWeaponSystem>().TryGetWeapon(wearer, out _, out var melee));
            Assert.That(melee!.Damage.DamageDict["Slash"].Int(), Is.EqualTo(24));
            Assert.That(melee.Damage.DamageDict["Piercing"].Int(), Is.EqualTo(6));
            var knuckles = Fit("Knuckles", maker);
            Assert.That(em.GetComponent<PowerArmorModComponent>(knuckles).ActionEntity, Is.Null);
            Assert.That(em.System<SharedMeleeWeaponSystem>().TryGetWeapon(wearer, out _, out var reinforced));
            Assert.That(reinforced!.Damage.DamageDict["Structural"].Int(), Is.EqualTo(50));
            Assert.That(reinforced.Damage.DamageDict["Blunt"].Int(), Is.EqualTo(30));
            Assert.That(em.EntityExists(claw), Is.False);
            var wrist = Fit("Welder", maker);
            em.EventBus.RaiseLocalEvent(wrist, new ActivateArmorModEvent { Performer = wearer });
            Assert.That(hands.GetActiveItem(wearer), Is.Null);
            Assert.That(em.GetComponent<PowerArmorModWearerComponent>(wearer).ActiveWristTool, Is.Not.Null);
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
                    Assert.That(hands.GetActiveItem(wearer), Is.Null);
                    var tool = hud.ActiveWristTool;
                    Assert.That(tool, Is.EqualTo(mod.ToolEntity).And.Not.Null);
                    if (kind == ArmorModKind.Welder)
                        Assert.That(em.HasComponent<WelderComponent>(tool!.Value));
                    else
                    {
                        var canister = em.System<ItemSlotsSystem>().GetItemOrNull(tool!.Value, "gun_magazine");
                        Assert.That(canister, Is.Not.Null);
                        var solutions = em.System<SharedSolutionContainerSystem>();
                        Assert.That(solutions.TryGetSolution(canister!.Value, "fuel", out _, out var fuel));
                        Assert.That(fuel!.MaxVolume.Int(), Is.EqualTo(10));
                        Assert.That(em.System<SharedGunSystem>().TryGetGun(wearer, out var active, out var gun));
                        em.System<SharedGunSystem>().AttemptShoot(wearer, active, gun!, map.GridCoords.Offset(new System.Numerics.Vector2(2, 0)));
                        Assert.That(fuel.Volume.Int(), Is.EqualTo(9));
                        var spare = Spawn("N14FlamerFuel");
                        em.System<SharedInteractionSystem>().InteractHand(wearer, spare);
                        Assert.That(fuel.Volume.Int(), Is.EqualTo(10));
                        Assert.That(hands.GetActiveItem(wearer), Is.Null);
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

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    public async Task JumpServosLandAtRangeOrBeforeWall(bool wall, bool diagonal)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        var direction = diagonal ? Vector2.Normalize(Vector2.One) : Vector2.UnitX;
        var start = new Vector2(0.5f, 0.5f);
        EntityUid wearer = default;
        await pair.Server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            for (var x = 0; x <= 7; x++)
            for (var y = 0; y <= 5; y++)
                maps.SetTile(map.Grid, new Vector2i(x, y), map.Tile.Tile);
            em.System<GravitySystem>().EnableGravity(map.Grid.Owner);
            if (wall)
                em.SpawnEntity("WallReinforced", new EntityCoordinates(map.Grid, 3.5f, 0.5f));
            var position = new EntityCoordinates(map.Grid, start);
            wearer = em.SpawnEntity("MobHuman", position);
            var frame = em.SpawnEntity("AtomCatsT45Red", position);
            var module = em.SpawnEntity("AtomCatsModJump", position);
            em.System<NpcFactionSystem>().AddFaction(wearer, "AtomCats");
            Assert.That(em.System<InventorySystem>().TryEquip(wearer, frame, "outerClothing", force: true));
            Assert.That(em.System<SharedHandsSystem>().TryPickup(wearer, module));
            em.EventBus.RaiseLocalEvent(frame, new InteractUsingEvent(wearer, module, frame, position));
            em.System<SharedTransformSystem>().SetWorldRotation(wearer, direction.ToWorldAngle());
            em.System<SharedPhysicsSystem>().SetLinearVelocity(wearer, new Vector2(1, -2));
            var actionId = em.GetComponent<PowerArmorModComponent>(module).ActionEntity!.Value;
            var action = em.GetComponent<InstantActionComponent>(actionId);
            em.System<SharedActionsSystem>().PerformAction(wearer, null, actionId, action, action.Event, pair.Server.Timing.CurTime);
            Assert.That(em.HasComponent<ThrownItemComponent>(wearer));
            Assert.That(action.Cooldown!.Value.End - action.Cooldown.Value.Start, Is.EqualTo(TimeSpan.FromSeconds(30)));
        });
        await pair.Server.WaitRunTicks(pair.SecondsToTicks(1));
        await pair.Server.WaitAssertion(() =>
        {
            var travelled = em.System<SharedTransformSystem>().GetWorldPosition(wearer) - start;
            Assert.That(em.HasComponent<ThrownItemComponent>(wearer), Is.False);
            var expected = wall ? 2.1f : 5f;
            Assert.That(Vector2.Distance(travelled, direction * expected), Is.LessThan(0.15f),
                $"Expected {direction * expected}, travelled {travelled}");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MountedIndustrialWelderRepairsWithAnEmptyHand()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        var rate = (int)pair.Server.ResolveDependency<IGameTiming>().TickRate;
        EntityUid user = default, module = default, target = default, tool = default;
        await pair.Server.WaitAssertion(() =>
        {
            EntityUid Spawn(string id) => em.SpawnEntity(id, map.GridCoords);
            var frame = Spawn("N14ClothingOuterPowerArmorT45");
            user = Spawn("MobHuman");
            em.System<NpcFactionSystem>().AddFaction(user, "AtomCats");
            Assert.That(em.System<InventorySystem>().TryEquip(user, frame, "outerClothing", force: true));
            module = Spawn("AtomCatsModWelder");
            Assert.That(em.System<SharedHandsSystem>().TryPickup(user, module));
            em.EventBus.RaiseLocalEvent(frame, new InteractUsingEvent(user, module, frame, map.GridCoords));
            em.EventBus.RaiseLocalEvent(module, new ActivateArmorModEvent { Performer = user });
            tool = em.GetComponent<PowerArmorModWearerComponent>(user).ActiveWristTool!.Value;
            target = Spawn("ReclamationPlateGloves");
            em.System<DamageableSystem>().TryChangeDamage(target, new DamageSpecifier { DamageDict = { ["Blunt"] = 10 } }, ignoreResistances: true);
            em.System<SharedInteractionSystem>().InteractHand(user, target);
            Assert.That(em.System<SharedHandsSystem>().GetActiveItem(user), Is.Null);
        });
        await pair.Server.WaitRunTicks(rate * 11);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<DamageableComponent>(target).TotalDamage.Int(), Is.Zero);
            Assert.That(em.System<SharedToolSystem>().GetWelderFuelAndCapacity(tool).fuel.Int(), Is.EqualTo(250));
            em.EventBus.RaiseLocalEvent(module, new ActivateArmorModEvent { Performer = user });
            Assert.That(em.GetComponent<PowerArmorModWearerComponent>(user).ActiveWristTool, Is.Null);
            em.System<SharedInteractionSystem>().InteractHand(user, target);
            Assert.That(em.System<SharedHandsSystem>().GetActiveItem(user), Is.EqualTo(target));
        });
        await pair.CleanReturnAsync();
    }
}
