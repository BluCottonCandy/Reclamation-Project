using System;
using System.Linq;
using System.Numerics;
using JunkhoundPulseOverlay = Content.Client._ReclamationProject.PowerArmor.JunkhoundPulseOverlay;
using Content.IntegrationTests.Pair;
using Content.Server._ReclamationProject.PowerArmor;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Weapons.Melee;
using Content.Shared._ReclamationProject.PowerArmor;
using Content.Shared.Actions;
using Content.Shared.CombatMode;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Repulsor;
using Content.Shared.NPC.Systems;
using Content.Shared.Standing;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Components;
using Robust.Client.Graphics;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture]
public sealed class AtomCatsGarageUtilityTest
{
    internal static EntityUid Fit(IEntityManager em, EntityCoordinates coords, EntityUid frame, string kind)
    {
        var mechanic = em.SpawnEntity("MobHuman", coords);
        em.System<NpcFactionSystem>().AddFaction(mechanic, "AtomCats");
        var kit = em.SpawnEntity("AtomCatsMod" + kind, coords);
        Assert.That(em.System<SharedHandsSystem>().TryPickup(mechanic, kit));
        em.EventBus.RaiseLocalEvent(frame, new InteractUsingEvent(mechanic, kit, frame, coords));
        Assert.That(em.System<PowerArmorModSystem>().Fitted(frame), Does.Contain(kit));
        em.DeleteEntity(mechanic);
        return kit;
    }

    private static bool Target(IEntityManager em, EntityUid module, EntityUid wearer, EntityUid target)
    {
        var ev = new TargetGarageModEvent { Performer = wearer, Target = target };
        em.EventBus.RaiseLocalEvent(module, ev);
        return ev.Handled;
    }

    private static bool Activate(IEntityManager em, EntityUid module, EntityUid wearer)
    {
        var ev = new ActivateArmorModEvent { Performer = wearer };
        em.EventBus.RaiseLocalEvent(module, ev);
        return ev.Handled;
    }

    [Test]
    public async Task JumperTransfersOnlyBetweenTheSameInstalledCoresAfterThreeSeconds()
    {
        await using var pair = await PoolManager.GetServerClient();
        var coords = (await pair.CreateTestMap()).GridCoords;
        var em = pair.Server.ResolveDependency<IEntityManager>();
        var rate = pair.Server.ResolveDependency<IGameTiming>().TickRate;
        EntityUid wearer = default, module = default, recipient = default, donor = default, receiver = default;
        EntityUid Core(EntityUid frame)
        {
            var systems = em.System<FusionCorePoweredSystem>();
            var rig = systems.GetSuitPowerSystems(frame)!.Value;
            rig.Comp.SwitchedOn = false;
            systems.UpdatePowered(rig);
            return em.System<ItemSlotsSystem>().GetItemOrNull(rig, rig.Comp.SlotId)!.Value;
        }
        float Charge(EntityUid core) => em.GetComponent<BatteryComponent>(core).CurrentCharge;
        await pair.Server.WaitAssertion(() =>
        {
            var frame = em.SpawnEntity("AtomCatsT45Red", coords);
            recipient = em.SpawnEntity("N14ClothingOuterPowerArmorT45", coords);
            wearer = em.SpawnEntity("MobHuman", coords);
            module = Fit(em, coords, frame, "JumperCables");
            Assert.That(em.System<InventorySystem>().TryEquip(wearer, frame, "outerClothing", force: true));
            donor = Core(frame);
            receiver = Core(recipient);
            var batteries = em.System<BatterySystem>();
            batteries.SetCharge(donor, 2000);
            batteries.SetCharge(receiver, 0);
            Assert.That(Target(em, module, wearer, wearer), Is.False);
            Assert.That(Target(em, module, wearer, recipient));
            Assert.That(Charge(receiver), Is.Zero);
        });
        await pair.RunTicksSync(rate * 4);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(Charge(donor), Is.EqualTo(750));
            Assert.That(Charge(receiver), Is.EqualTo(1250));
            Assert.That(Target(em, module, wearer, recipient));
            Assert.That(em.System<FusionCorePoweredSystem>().TryRemoveCoreFromSuit(recipient, wearer));
        });
        await pair.RunTicksSync(rate * 4);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(Charge(donor), Is.EqualTo(750));
            Assert.That(Charge(receiver), Is.EqualTo(1250));
            Assert.That(em.System<FusionCorePoweredSystem>().TryInsertCoreIntoSuit(recipient, receiver, wearer));
            var patient = em.SpawnEntity("MobHuman", coords);
            Assert.That(em.System<InventorySystem>().TryEquip(patient, recipient, "outerClothing", force: true));
            em.System<BatterySystem>().SetCharge(receiver, 4950);
            Assert.That(Target(em, module, wearer, patient));
        });
        await pair.RunTicksSync(rate * 4);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(Charge(donor), Is.EqualTo(700));
            Assert.That(Charge(receiver), Is.EqualTo(5000));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task HitchLeavesBothHandsFreeAndBootsIndependentlyResistKnockback()
    {
        await using var pair = await PoolManager.GetServerClient();
        var coords = (await pair.CreateTestMap()).GridCoords;
        var em = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            EntityUid Spawn(string id) => em.SpawnEntity(id, coords);
            var wearer = Spawn("MobHuman");
            var patient = Spawn("MobHuman");
            var crate = Spawn("ReclamationSupplyCrateRed");
            var frame = Spawn("AtomCatsT45Blue");
            var tow = Fit(em, coords, frame, "TowRig");
            var mag = Fit(em, coords, frame, "MagLock");
            var hands = em.System<SharedHandsSystem>();
            var inventory = em.System<InventorySystem>();
            Assert.That(inventory.TryEquip(wearer, frame, "outerClothing", force: true));
            var mover = em.GetComponent<InputMoverComponent>(wearer);
            var pull = em.GetComponent<PullerComponent>(wearer);
            var movement = em.GetComponent<MovementSpeedModifierComponent>(wearer);
            var walk = movement.CurrentWalkSpeed;
            var sprint = movement.CurrentSprintSpeed;
            em.System<SharedMoverController>().SetSprinting((wearer, mover), 0, mover.DefaultSprinting);

            void CheckSpeed(float multiplier)
            {
                Assert.That(movement.CurrentWalkSpeed, Is.EqualTo(walk * multiplier).Within(.001f));
                Assert.That(movement.CurrentSprintSpeed, Is.EqualTo(sprint * multiplier).Within(.001f));
                Assert.That(mover.Sprinting);
            }

            Assert.That(Target(em, tow, wearer, patient), Is.False);
            Assert.That(Target(em, tow, wearer, crate));
            Assert.That(pull.Pulling, Is.EqualTo(crate));
            CheckSpeed(1f);
            Assert.That(Activate(em, mag, wearer));
            CheckSpeed(0.95f);
            var capped = new RefreshMovementSpeedModifiersEvent { IgnoreSlowdowns = true };
            capped.ModifySpeed(3f);
            em.EventBus.RaiseLocalEvent(wearer, capped);
            capped.CapSpeed(1.35f);
            Assert.That(capped.SprintSpeedModifier, Is.EqualTo(1.35f * 0.95f).Within(.001f));
            Assert.That(hands.TryPickupAnyHand(wearer, Spawn("Wrench")));
            Assert.That(hands.TryPickupAnyHand(wearer, Spawn("Screwdriver")));
            Assert.That(Target(em, tow, wearer, wearer));
            Assert.That(Target(em, tow, wearer, crate), Is.False, "hitch needs one free hand");
            CheckSpeed(0.95f);
            var gauntlet = Spawn("ReclamationFrameGauntlets");
            var weapon = (gauntlet, em.GetComponent<MeleeThrowOnHitComponent>(gauntlet));
            Assert.That(em.System<MeleeThrowOnHitSystem>().CanThrowOnHit(weapon, wearer), Is.False);
            em.System<ThrowingSystem>().TryThrow(wearer, Vector2.UnitX, user: patient);
            Assert.That(em.HasComponent<ThrownItemComponent>(wearer), Is.False);
            Assert.That(Activate(em, mag, wearer));
            CheckSpeed(1f);
            Assert.That(em.System<MeleeThrowOnHitSystem>().CanThrowOnHit(weapon, wearer));
            Assert.That(hands.TryDrop(wearer));
            Assert.That(em.System<StandingStateSystem>().Down(patient));
            Assert.That(Target(em, tow, wearer, patient));
            CheckSpeed(1f);
            Assert.That(inventory.TryUnequip(wearer, "outerClothing", force: true));
            Assert.That(em.HasComponent<ArmorTowComponent>(wearer), Is.False);
            Assert.That(pull.Pulling, Is.Null);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EngagedAnchorsBlockShovesAndExhaustionParalysis()
    {
        await using var pair = await PoolManager.GetServerClient();
        var coords = (await pair.CreateTestMap()).GridCoords;
        var em = pair.Server.ResolveDependency<IEntityManager>();
        var rate = pair.Server.ResolveDependency<IGameTiming>().TickRate;
        EntityUid wearer = default, attacker = default, boots = default;
        await pair.Server.WaitAssertion(() =>
        {
            wearer = em.SpawnEntity("MobHuman", coords);
            attacker = em.SpawnEntity("MobHuman", coords.Offset(new Vector2(0.65f, 0f)));
            var suit = em.SpawnEntity("AtomCatsT45Blue", coords);
            boots = Fit(em, coords, suit, "MagLock");
            Assert.That(em.System<InventorySystem>().TryEquip(wearer, suit, "outerClothing", force: true));
            Assert.That(Activate(em, boots, wearer));
            var combat = em.GetComponent<CombatModeComponent>(attacker);
            em.System<SharedCombatModeSystem>().SetInCombatMode(attacker, true);
            em.System<SharedCombatModeSystem>().SetCanDisarm(attacker, true, combat);
            var hand = em.GetComponent<MeleeWeaponComponent>(attacker);
            var body = em.GetComponent<PhysicsComponent>(wearer);
            var before = body.LinearVelocity;
            Assert.That(em.System<MeleeWeaponSystem>().AttemptDisarmAttack(attacker, attacker, hand, wearer), Is.False);
            var crowd = new AttemptMobCollideEvent();
            em.EventBus.RaiseLocalEvent(wearer, ref crowd);
            Assert.That(crowd.Cancelled);
            em.System<RepulseSystem>().Repulse((attacker, em.EnsureComponent<RepulseComponent>(attacker)), wearer);
            Assert.That(body.LinearVelocity, Is.EqualTo(before));
            Assert.That(em.HasComponent<StunnedComponent>(wearer), Is.False);
            Assert.That(em.HasComponent<KnockedDownComponent>(wearer), Is.False);
            var stamina = em.GetComponent<StaminaComponent>(wearer);
            Assert.That(stamina.StaminaDamage, Is.Zero);
            em.System<StaminaSystem>().ToggleStaminaDrain(wearer, stamina.CritThreshold * 2, true, true, attacker);
        });
        await pair.RunTicksSync(rate);
        await pair.Server.WaitAssertion(() =>
        {
            var stamina = em.GetComponent<StaminaComponent>(wearer);
            Assert.That(stamina.StaminaDamage, Is.EqualTo(stamina.CritThreshold));
            Assert.That(stamina.Critical);
            Assert.That(em.HasComponent<StunnedComponent>(wearer), Is.False);
            Assert.That(em.HasComponent<KnockedDownComponent>(wearer), Is.False);
            Assert.That(em.System<StandingStateSystem>().IsDown(wearer), Is.False);
            em.System<StaminaSystem>().ToggleStaminaDrain(wearer, 0, false, true, attacker);
            Assert.That(Activate(em, boots, wearer));
            Assert.That(em.HasComponent<ArmorMagLockComponent>(wearer), Is.False);
            var crowd = new AttemptMobCollideEvent();
            em.EventBus.RaiseLocalEvent(wearer, ref crowd);
            Assert.That(crowd.Cancelled, Is.False);
        });
        await pair.RunTicksSync(rate * 12);
        await pair.Server.WaitAssertion(() =>
        {
            var stamina = em.GetComponent<StaminaComponent>(wearer);
            Assert.That(stamina.Critical, Is.False);
            em.System<StaminaSystem>().TakeStaminaDamage(wearer, stamina.CritThreshold, stamina);
            Assert.That(em.HasComponent<StunnedComponent>(wearer));
            Assert.That(em.HasComponent<KnockedDownComponent>(wearer));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task JunkhoundPulsesLooseSalvageForTenSecondsAndCanPulseAgainOnExpiry()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var coords = (await pair.CreateTestMap()).GridCoords;
        var em = pair.Server.ResolveDependency<IEntityManager>();
        var client = pair.Client.ResolveDependency<IEntityManager>();
        var session = pair.Server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);
        var rate = pair.Server.ResolveDependency<IGameTiming>().TickRate;
        EntityUid player = default, module = default, plate = default, metal = default, scrap = default;
        NetEntity netPlayer = default, netPlate = default, netMetal = default;
        await pair.Server.WaitAssertion(() =>
        {
            EntityUid Spawn(string id, float x = 0) => em.SpawnEntity(id, coords.Offset(new Vector2(x, 0)));
            player = Spawn("MobHuman");
            netPlayer = em.GetNetEntity(player);
            pair.Server.PlayerMan.SetAttachedEntity(session, player);
            var frame = Spawn("N14ClothingOuterPowerArmorT45");
            module = Fit(em, coords, frame, "Junkhound");
            Assert.That(em.System<InventorySystem>().TryEquip(player, frame, "outerClothing", force: true));
            plate = Spawn("ReclamationPlateGloves", 1);
            metal = Spawn("SheetSteel1", 5.5f);
            scrap = Spawn("N14Scrap1", 2);
            netPlate = em.GetNetEntity(plate);
            netMetal = em.GetNetEntity(metal);
            Spawn("Wrench");
            Spawn("SheetSteel1", 6.5f);
            var carried = Spawn("N14Scrap1");
            Assert.That(em.System<SharedHandsSystem>().TryPickup(player, carried));
        });
        await pair.RunTicksSync(12);
        await pair.Server.WaitAssertion(() =>
        {
            var action = em.GetComponent<PowerArmorModComponent>(module).ActionEntity!.Value;
            Assert.That(em.GetComponent<InstantActionComponent>(action).UseDelay, Is.EqualTo(TimeSpan.FromSeconds(10)));
            Assert.That(Activate(em, module, player));
            Assert.That(em.GetComponent<JunkhoundPulseComponent>(player).Hits, Is.EquivalentTo(new[] { scrap, metal, plate }));
            Assert.That(Activate(em, module, player), Is.False);
        });
        async Task Check(bool metalLit, bool plateLit)
        {
            await pair.Client.WaitAssertion(() =>
            {
                var overlay = pair.Client.ResolveDependency<IOverlayManager>().GetOverlay<JunkhoundPulseOverlay>();
                var viewer = client.GetEntity(netPlayer);
                var highlighted = overlay.HighlightedItems(viewer).ToArray();
                Assert.That(highlighted.Contains(client.GetEntity(netMetal)), Is.EqualTo(metalLit));
                Assert.That(highlighted.Contains(client.GetEntity(netPlate)), Is.EqualTo(plateLit));
            });
        }
        await pair.RunTicksSync(rate);
        await Check(true, true);
        await pair.Server.WaitAssertion(() => Assert.That(em.System<SharedHandsSystem>().TryPickupAnyHand(player, plate)));
        await pair.RunTicksSync(rate * 8);
        await Check(true, false);
        await pair.Server.WaitAssertion(() => Assert.That(Activate(em, module, player), Is.False));
        await pair.RunTicksSync(rate + 3);
        await Check(false, false);
        await pair.Server.WaitAssertion(() => Assert.That(Activate(em, module, player)));
        await pair.RunTicksSync(12);
        await Check(true, false);
        await pair.Server.WaitAssertion(() => Assert.That(em.System<InventorySystem>().TryUnequip(player, "outerClothing", force: true)));
        await pair.RunTicksSync(12);
        await Check(false, false);
        await pair.Server.WaitPost(() => pair.Server.PlayerMan.SetAttachedEntity(session, null));
        await pair.CleanReturnAsync();
    }
}
