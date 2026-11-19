using Content.Client.Alerts;
using Content.IntegrationTests.Pair;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._Misfits.PowerArmor;
using Content.Shared._Misfits.Special;
using Content.Shared._Misfits.Special.Components;
using Content.Shared._ReclamationProject.PowerArmor;
using Content.Shared.Armor;
using Content.Shared.Alert;
using Content.Shared.Atmos;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Clothing.Components;
using System.Linq;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using SpriteComponent = Robust.Client.GameObjects.SpriteComponent;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture]
public sealed class ModularPowerArmorTest
{
    [Test]
    public async Task StrengthAndOptionalTraining()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var user = em.SpawnEntity("MobHuman", map.GridCoords);
            var frame = em.SpawnEntity("ReclamationPowerArmorFrame", map.GridCoords);
            var special = em.System<SharedSpecialSystem>();
            var inventory = em.System<InventorySystem>();
            var stats = em.EnsureComponent<SpecialComponent>(user);
            var movement = em.System<MovementSpeedModifierSystem>();
            var speed = em.GetComponent<MovementSpeedModifierComponent>(user);
            Assert.That(special.TrySetBase(user, SpecialStat.Strength, 2, stats));
            Assert.That(inventory.TryEquip(user, frame, "outerClothing", silent: true), Is.False);
            em.EnsureComponent<PowerArmorProficiencyComponent>(user);
            Assert.That(inventory.TryEquip(user, frame, "outerClothing", silent: true), Is.False);
            em.RemoveComponent<PowerArmorProficiencyComponent>(user);
            Assert.That(special.TrySetBase(user, SpecialStat.Strength, 3, stats));
            movement.RefreshMovementSpeedModifiers(user);
            var normalWalk = speed.WalkSpeedModifier;
            var normalSprint = speed.SprintSpeedModifier;
            Assert.That(inventory.TryEquip(user, frame, "outerClothing", silent: true));
            Assert.That(speed.WalkSpeedModifier, Is.EqualTo(normalWalk * 0.70f).Within(0.005f));
            var book = em.SpawnEntity("TrainingManualPowerArmorOperation", map.GridCoords);
            em.EventBus.RaiseLocalEvent(book, new UseInHandEvent(user));
            Assert.That(em.HasComponent<PowerArmorProficiencyComponent>(user));
            Assert.That(speed.WalkSpeedModifier, Is.EqualTo(normalWalk).Within(0.005f));
            var rig = em.GetComponent<ToggleableClothingComponent>(frame).ClothingUids.Single(p => p.Value == "back").Key;
            foreach (var strength in new[] { 3, 7, 8, 9, 10 })
            {
                special.TrySetBase(user, SpecialStat.Strength, strength, stats);
                movement.RefreshMovementSpeedModifiers(user);
                Assert.That(speed.WalkSpeedModifier, Is.EqualTo(normalWalk).Within(0.005f));
                Assert.That(speed.SprintSpeedModifier, Is.EqualTo(normalSprint).Within(0.005f));
            }
            Assert.That(inventory.TryEquip(user, rig, "back", force: true));
            Assert.That(speed.WalkSpeedModifier, Is.EqualTo(normalWalk).Within(0.005f));
            em.EventBus.RaiseLocalEvent(rig, new FusionCorePowerToggleEvent { Performer = user });
            Assert.That(speed.WalkSpeedModifier, Is.EqualTo(normalWalk * 0.05f).Within(0.005f));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task WornPiecesAndHudReplicate()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var map = await pair.CreateTestMap();
        var server = pair.Server.ResolveDependency<IEntityManager>();
        var client = pair.Client.ResolveDependency<IEntityManager>();
        var protos = pair.Client.ResolveDependency<IPrototypeManager>();
        NetEntity netWearer = default;
        EntityUid suit = default;
        await pair.Server.WaitAssertion(() =>
        {
            var wearer = server.SpawnEntity("MobHuman", map.GridCoords);
            netWearer = server.GetNetEntity(wearer);
            suit = server.SpawnEntity("N14ClothingOuterPowerArmorT45", map.GridCoords);
            Assert.That(server.System<InventorySystem>().TryEquip(wearer, suit, "outerClothing", force: true));
        });

        void CheckHud(string ones, bool hasGloves)
        {
            var wearer = client.GetEntity(netWearer);
            var view = client.SpawnEntity("ReclamationPowerArmorRatingView", MapCoordinates.Nullspace);
            var sprite = client.GetComponent<SpriteComponent>(view);
            var ev = new UpdateAlertSpriteEvent((view, sprite), protos.Index<AlertPrototype>("ModularPowerArmorRating"));
            client.EventBus.RaiseLocalEvent(wearer, ref ev);
            Assert.That(sprite.LayerMapTryGet("pa-tens", out var tensIndex), Is.True);
            Assert.That(sprite[tensIndex].RsiState.Name, Is.EqualTo("4"));
            Assert.That(sprite.LayerMapTryGet("pa-ones", out var onesIndex), Is.True);
            Assert.That(sprite[onesIndex].RsiState.Name, Is.EqualTo(ones));
            var worn = client.GetComponent<SpriteComponent>(wearer);
            Assert.That(worn.LayerMapTryGet("pa-frame", out _), Is.True);
            Assert.That(worn.LayerMapTryGet("pa-Gloves", out _), Is.EqualTo(hasGloves));
            client.DeleteEntity(view);
        }

        await pair.RunTicksSync(20);
        await pair.Client.WaitAssertion(() => CheckHud("5", true));
        await pair.Server.WaitAssertion(() => Assert.That(server.System<ItemSlotsSystem>().TryEject(
            suit, PowerArmorPlates.Container(ArmorPlateSlot.Gloves), null, out _)));
        await pair.RunTicksSync(20);
        await pair.Client.WaitAssertion(() => CheckHud("2", false));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MixBreakRepairAndReattach()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var em = server.ResolveDependency<IEntityManager>();
        EntityUid frame = default, wearer = default, helmet = default;
        await server.WaitAssertion(() =>
        {
            var slots = em.System<ItemSlotsSystem>();
            var inv = em.System<InventorySystem>();
            var damage = em.System<DamageableSystem>();
            frame = em.SpawnEntity("ReclamationPowerArmorFrame", map.GridCoords);
            wearer = em.SpawnEntity("MobHuman", map.GridCoords);
            Assert.That(inv.TryEquip(wearer, frame, "outerClothing", silent: true), Is.True);
            var chassis = em.GetComponent<ModularPowerArmorComponent>(frame);
            Assert.That(chassis.Rating, Is.EqualTo(20));
            Assert.That(chassis.Sealed, Is.False);
            Assert.That(em.GetComponent<ArmorComponent>(frame).Modifiers.Coefficients["Piercing"], Is.EqualTo(.8f).Within(.001f));
            Assert.That(inv.TryGetSlotEntity(wearer, "gloves", out var gloves), Is.True);
            Assert.That(em.GetComponent<MeleeWeaponComponent>(gloves!.Value).Damage.DamageDict["Blunt"].Int(), Is.EqualTo(10));
            Assert.That(em.HasComponent<MeleeThrowOnHitComponent>(gloves.Value), Is.True);

            var donor = em.SpawnEntity("N14ClothingOuterPowerArmorT45", map.GridCoords);
            Assert.That(em.GetComponent<ModularPowerArmorComponent>(donor).Rating, Is.EqualTo(45));
            foreach (var slot in PowerArmorPlates.Slots)
            {
                var key = PowerArmorPlates.Container(slot);
                Assert.That(slots.TryEject(donor, key, null, out var part), Is.True, key);
                Assert.That(slots.TryInsert(frame, key, part!.Value, null), Is.True, key);
                if (slot == ArmorPlateSlot.Helmet) helmet = part.Value;
            }
            Assert.That(chassis.Rating, Is.EqualTo(45));
            Assert.That(chassis.Sealed, Is.True);
            Assert.That(chassis.Absorption, Is.EqualTo(1f - em.GetComponent<PowerArmorIntegrityComponent>(donor).BleedthroughRatio).Within(.001f));
            var fire = new GetFireProtectionEvent();
            inv.RelayEvent((wearer, em.GetComponent<InventoryComponent>(wearer)), ref fire);
            Assert.That(fire.Multiplier, Is.Zero);
            var flames = em.System<FlammableSystem>();
            var flammable = em.GetComponent<FlammableComponent>(wearer);
            flames.AdjustFireStacks(wearer, 2, flammable);
            flames.Ignite(wearer, frame, flammable, ignoreFireProtection: true);
            Assert.That(flammable.OnFire, Is.False);
            var hit = new DamageSpecifier();
            hit.DamageDict["Blunt"] = 10;
            damage.TryChangeDamage(wearer, hit, targetPart: Content.Shared._Shitmed.Targeting.TargetBodyPart.Head);
            var plateHp = em.GetComponent<DamageableComponent>(helmet);
            Assert.That(plateHp.TotalDamage.Float(), Is.GreaterThan(0).And.LessThanOrEqualTo(10));

            hit.DamageDict["Blunt"] = 1000;
            damage.TryChangeDamage(helmet, hit, ignoreResistances: true);
            var mount = PowerArmorPlates.Container(ArmorPlateSlot.Helmet);
            Assert.That(slots.GetItemOrNull(frame, mount), Is.Null);
            Assert.That(chassis.Rating, Is.EqualTo(40));
            Assert.That(chassis.Sealed, Is.False);
            Assert.That(slots.TryInsert(frame, mount, helmet, null), Is.False);
            var open = new GetFireProtectionEvent();
            inv.RelayEvent((wearer, em.GetComponent<InventoryComponent>(wearer)), ref open);
            Assert.That(open.Multiplier, Is.GreaterThan(0));
            flames.Ignite(wearer, frame, flammable, ignoreFireProtection: true);
            Assert.That(flammable.OnFire, Is.True);
            flames.Extinguish(wearer, flammable);
            Assert.That(em.GetComponent<TransformComponent>(helmet).ParentUid, Is.EqualTo(map.Grid.Owner));
            var repairer = em.SpawnEntity("MobHuman", map.GridCoords);
            em.EnsureComponent<GodmodeComponent>(repairer);
            em.EnsureComponent<GodmodeComponent>(wearer);
            var welder = em.SpawnEntity("Welder", map.GridCoords);
            Assert.That(em.System<SharedHandsSystem>().TryPickup(repairer, welder), Is.True);
            Assert.That(em.System<ItemToggleSystem>().TryActivate((welder, null), repairer), Is.True);
            var repair = new InteractUsingEvent(repairer, welder, helmet, map.GridCoords);
            em.EventBus.RaiseLocalEvent(helmet, repair);
            Assert.That(repair.Handled, Is.True);
        });
        var tickRate = server.ResolveDependency<IGameTiming>().TickRate;
        await pair.RunTicksSync(9 * tickRate);
        await server.WaitAssertion(() => Assert.That(em.GetComponent<DamageableComponent>(helmet).TotalDamage.Float(), Is.Positive));
        await pair.RunTicksSync(tickRate + 2);
        await server.WaitAssertion(() =>
        {
            var slots = em.System<ItemSlotsSystem>();
            var mount = PowerArmorPlates.Container(ArmorPlateSlot.Helmet);
            var chassis = em.GetComponent<ModularPowerArmorComponent>(frame);
            Assert.That(em.GetComponent<DamageableComponent>(helmet).TotalDamage.Float(), Is.Zero);
            Assert.That(em.System<SharedHandsSystem>().TryPickup(wearer, helmet), Is.True);
            var install = new InteractUsingEvent(wearer, helmet, wearer, map.GridCoords);
            em.EventBus.RaiseLocalEvent(wearer, install);
            Assert.That(install.Handled, Is.True);
            Assert.That(slots.GetItemOrNull(frame, mount), Is.EqualTo(helmet));
            Assert.That(chassis.Sealed, Is.True);
            Assert.That(slots.TryEject(frame, mount, null, out _), Is.True);

            var t51 = em.SpawnEntity("N14ClothingOuterPowerArmorT51", map.GridCoords);
            Assert.That(slots.TryEject(t51, mount, null, out var upgraded), Is.True);
            Assert.That(slots.TryInsert(frame, mount, upgraded!.Value, null), Is.True);
            Assert.That(chassis.Rating, Is.EqualTo(47));
            Assert.That(chassis.Sealed, Is.True);
        });
        await pair.CleanReturnAsync();
    }
}
