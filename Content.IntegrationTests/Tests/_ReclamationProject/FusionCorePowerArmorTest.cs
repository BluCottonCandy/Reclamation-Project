using System.Linq;
using Content.Server._ReclamationProject.PowerArmor;
using Content.Server.Emp;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared._Misfits.PowerArmor;
using Content.Shared._Misfits.Special.Components;
using Content.Shared._ReclamationProject.Endurance;
using Content.Shared._ReclamationProject.PowerArmor;
using Content.Shared.Clothing.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Emp;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Traits.Assorted.Components;
using Content.IntegrationTests.Pair;
using Content.Shared.Verbs;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture]
public sealed class FusionCorePowerArmorTest
{
    private const float PoweredSpeed = 0.70f;          // powered, Strength 1-7
    private const float StrongPoweredSpeed = 0.75f;    // powered, Strength 8-9
    private const float MightyPoweredSpeed = 0.80f;    // powered, Strength 10
    private const float DeadSpeed = 0.10f;

    private static (EntityUid Wearer, EntityUid Systems) SpawnWearer(IEntityManager entities, TestMapData map)
    {
        var inventory = entities.System<InventorySystem>();
        var wearer = entities.SpawnEntity("MobHuman", map.GridCoords);
        var suit = entities.SpawnEntity("N14ClothingOuterPowerArmorT51", map.GridCoords);
        var systems = entities.SpawnEntity("N14ClothingPowerSystemsBack", map.GridCoords);
        Assert.That(inventory.TryEquip(wearer, suit, "outerClothing", silent: true, force: true), Is.True);
        Assert.That(inventory.TryEquip(wearer, systems, "back", silent: true, force: true), Is.True);
        return (wearer, systems);
    }

    private static void KillCore(IEntityManager entities, EntityUid systems)
    {
        var core = entities.System<ItemSlotsSystem>().GetItemOrNull(systems, "fusion_core")!.Value;
        entities.System<BatterySystem>().SetCharge(core, 0);
        entities.System<FusionCorePoweredSystem>().UpdatePowered((systems, entities.GetComponent<FusionCorePoweredComponent>(systems)));
    }

    [Test]
    public async Task PowerArmourRunsOnFusionCores()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var slots = entities.System<ItemSlotsSystem>();

        EntityUid wearer = default;
        EntityUid systems = default;
        EntityUid strong = default;

        await server.WaitAssertion(() =>
        {
            (wearer, systems) = SpawnWearer(entities, map);

            // Spawns with a full core, and a full core lasts 30 minutes.
            var core = slots.GetItemOrNull(systems, "fusion_core");
            Assert.That(core, Is.Not.Null, "power systems spawned without a fusion core");
            var battery = entities.GetComponent<BatteryComponent>(core!.Value);
            var drain = entities.GetComponent<FusionCorePoweredComponent>(systems).DrainPerSecond;
            Assert.That(battery.MaxCharge / drain, Is.EqualTo(1800f).Within(1f), "a full core doesn't last 30 minutes");

            // The Laser Gatling still takes the same core.
            var gatling = entities.SpawnEntity("N14WeaponLaserGatling", map.GridCoords);
            Assert.That(slots.GetItemOrNull(gatling, "gun_magazine") is { } gatCore
                && entities.GetComponent<MetaDataComponent>(gatCore).EntityPrototype?.ID == "N14FusionCore",
                "the Laser Gatling doesn't use the fusion core");

            // Compare a stronger wearer in both powered and unpowered armor.
            EntityUid strongSystems;
            (strong, strongSystems) = SpawnWearer(entities, map);
            entities.EnsureComponent<SpecialComponent>(strong).BaseStrength = 8;

            // Powered speed: 70%, or 75% at Strength 8.
            var movement = entities.System<MovementSpeedModifierSystem>();
            movement.RefreshMovementSpeedModifiers(wearer);
            movement.RefreshMovementSpeedModifiers(strong);
            Assert.That(entities.GetComponent<MovementSpeedModifierComponent>(wearer).WalkSpeedModifier,
                Is.EqualTo(PoweredSpeed).Within(0.005f), "powered suit isn't 70% speed");
            Assert.That(entities.GetComponent<MovementSpeedModifierComponent>(strong).WalkSpeedModifier,
                Is.EqualTo(StrongPoweredSpeed).Within(0.005f), "Strength 8 powered suit isn't 75% speed");

            KillCore(entities, strongSystems);
            entities.System<MovementSpeedModifierSystem>().RefreshMovementSpeedModifiers(strong);

            KillCore(entities, systems);
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<FusionCorePoweredComponent>(systems).Powered, Is.False);
            Assert.That(entities.GetComponent<MovementSpeedModifierComponent>(wearer).WalkSpeedModifier,
                Is.EqualTo(DeadSpeed).Within(0.005f), "a dead core must leave 10% movement");
            Assert.That(entities.GetComponent<MovementSpeedModifierComponent>(strong).WalkSpeedModifier,
                Is.EqualTo(DeadSpeed).Within(0.005f), "Strength 8 must still crawl with a dead core");

        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// #Reclamation Project - Strength 10 ignores normal clothing slowdown, but never power armour's.
    /// </summary>
    [Test]
    public async Task StrengthTenDoesNotCancelPowerArmourSlowdown()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var movement = entities.System<MovementSpeedModifierSystem>();

        EntityUid wearer = default;
        EntityUid systems = default;

        await server.WaitAssertion(() =>
        {
            (wearer, systems) = SpawnWearer(entities, map);
            entities.EnsureComponent<SpecialComponent>(wearer).BaseStrength = 10;
            movement.RefreshMovementSpeedModifiers(wearer);
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<MovementSpeedModifierComponent>(wearer).WalkSpeedModifier,
                Is.EqualTo(MightyPoweredSpeed).Within(0.005f), "Strength 10 powered suit isn't 80% speed");

            // Switching off the servos applies the same crawl at any Strength.
            entities.EventBus.RaiseLocalEvent(systems, new FusionCorePowerToggleEvent { Performer = wearer });
            movement.RefreshMovementSpeedModifiers(wearer);
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<FusionCorePoweredComponent>(systems).Powered, Is.False);
            Assert.That(entities.GetComponent<MovementSpeedModifierComponent>(wearer).WalkSpeedModifier,
                Is.EqualTo(DeadSpeed).Within(0.005f), "Strength 10 still cancels the switched-off slowdown");
        });

        // Carrying an unworn suit: 50% normally, 65% at Strength 10.
        EntityUid weak = default;
        EntityUid strong = default;
        await server.WaitAssertion(() =>
        {
            var hands = entities.System<SharedHandsSystem>();
            weak = entities.SpawnEntity("MobHuman", map.GridCoords);
            strong = entities.SpawnEntity("MobHuman", map.GridCoords);
            entities.EnsureComponent<SpecialComponent>(strong).BaseStrength = 10;
            Assert.That(hands.TryPickupAnyHand(weak, entities.SpawnEntity("N14ClothingOuterPowerArmorT51", map.GridCoords)), Is.True);
            Assert.That(hands.TryPickupAnyHand(strong, entities.SpawnEntity("N14ClothingOuterPowerArmorT51", map.GridCoords)), Is.True);
            movement.RefreshMovementSpeedModifiers(weak);
            movement.RefreshMovementSpeedModifiers(strong);
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<MovementSpeedModifierComponent>(weak).WalkSpeedModifier,
                Is.EqualTo(0.5f).Within(0.005f), "carrying a suit isn't 50% speed");
            Assert.That(entities.GetComponent<MovementSpeedModifierComponent>(strong).WalkSpeedModifier,
                Is.EqualTo(0.65f).Within(0.005f), "Strength 10 carrying a suit isn't 65% speed");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PowerSwitchAndOutsideInstall()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var loc = server.ResolveDependency<ILocalizationManager>();
        var tickRate = (int) server.ResolveDependency<IGameTiming>().TickRate;
        var slots = entities.System<ItemSlotsSystem>();
        var hands = entities.System<SharedHandsSystem>();
        var fusion = entities.System<FusionCorePoweredSystem>();

        EntityUid wearer = default;
        EntityUid systems = default;
        EntityUid helper = default;
        EntityUid fresh = default;
        EntityUid oldCore = default;
        float chargeWhenOff = 0;

        await server.WaitAssertion(() =>
        {
            (wearer, systems) = SpawnWearer(entities, map);

            // Switch off from inside: crawl, and the core stops draining.
            entities.EventBus.RaiseLocalEvent(systems, new FusionCorePowerToggleEvent { Performer = wearer });
            var comp = entities.GetComponent<FusionCorePoweredComponent>(systems);
            Assert.That(comp.SwitchedOn, Is.False);
            Assert.That(comp.Powered, Is.False, "switched-off suit still powered");
            chargeWhenOff = entities.GetComponent<BatteryComponent>(slots.GetItemOrNull(systems, "fusion_core")!.Value).CurrentCharge;
        });

        await server.WaitRunTicks(3 * tickRate);

        await server.WaitAssertion(() =>
        {
            var core = slots.GetItemOrNull(systems, "fusion_core")!.Value;
            Assert.That(entities.GetComponent<BatteryComponent>(core).CurrentCharge, Is.EqualTo(chargeWhenOff).Within(0.01f),
                "core drained while switched off");
            Assert.That(entities.GetComponent<MovementSpeedModifierComponent>(wearer).WalkSpeedModifier,
                Is.EqualTo(DeadSpeed).Within(0.005f), "switched-off suit isn't a crawl");

            // Back on: normal power armour speed.
            entities.EventBus.RaiseLocalEvent(systems, new FusionCorePowerToggleEvent { Performer = wearer });
            Assert.That(entities.GetComponent<FusionCorePoweredComponent>(systems).Powered, Is.True);

            // The wearer can service the core, but cannot install one held by another player.
            var selfVerbs = entities.System<SharedVerbSystem>().GetLocalVerbs(wearer, wearer, typeof(AlternativeVerb));
            Assert.That(selfVerbs.Any(v => v.Text == loc.GetString("fusion-core-verb-remove")), Is.True);

            // Kill the core; a helper fits a fresh one from outside (3 seconds).
            KillCore(entities, systems);
            oldCore = core;
            helper = entities.SpawnEntity("MobHuman", map.GridCoords);
            fresh = entities.SpawnEntity("N14FusionCore", map.GridCoords);
            Assert.That(hands.TryPickupAnyHand(helper, fresh), Is.True);
            Assert.That(fusion.TryStartInstallInWearer(wearer, wearer, fresh), Is.False, "wearer installed a core from the helper's hand");
            Assert.That(fusion.TryStartInstallInWearer(helper, wearer, fresh), Is.True);
        });

        await server.WaitRunTicks((int) (1.5f * tickRate));
        await server.WaitAssertion(() =>
            Assert.That(slots.GetItemOrNull(systems, "fusion_core"), Is.EqualTo(oldCore), "core swapped before 3 seconds"));

        await server.WaitRunTicks(2 * tickRate);
        await server.WaitAssertion(() =>
        {
            Assert.That(slots.GetItemOrNull(systems, "fusion_core"), Is.EqualTo(fresh), "helper's core wasn't fitted");
            Assert.That(hands.IsHolding(helper, oldCore), Is.True, "old core didn't go to the helper");
            Assert.That(entities.GetComponent<FusionCorePoweredComponent>(systems).Powered, Is.True);
            Assert.That(entities.GetComponent<MovementSpeedModifierComponent>(wearer).WalkSpeedModifier,
                Is.EqualTo(PoweredSpeed).Within(0.01f), "powered PA speed changed");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task OthersNeedThreeSecondsToPullAWornCore()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var loc = server.ResolveDependency<ILocalizationManager>();
        var tickRate = (int) server.ResolveDependency<IGameTiming>().TickRate;
        var slots = entities.System<ItemSlotsSystem>();

        EntityUid wearer = default;
        EntityUid systems = default;
        EntityUid thief = default;

        await server.WaitAssertion(() =>
        {
            (wearer, systems) = SpawnWearer(entities, map);
            thief = entities.SpawnEntity("MobHuman", map.GridCoords);

            // No instant yoinking through the normal item-slot route.
            Assert.That(slots.TryEject(systems, "fusion_core", thief, out _), Is.False, "core came out instantly");

            // Right-clicking the wearer offers it, and it starts the 3-second pull.
            var verbs = entities.System<SharedVerbSystem>().GetLocalVerbs(wearer, thief, typeof(AlternativeVerb));
            Assert.That(verbs.Any(v => v.Text == loc.GetString("fusion-core-verb-remove")), "no Remove fusion core option on the wearer");
            Assert.That(entities.System<FusionCorePoweredSystem>().TryStartRemoveFromWearer(thief, wearer), Is.True);
        });

        // Halfway: still in.
        await server.WaitRunTicks((int) (1.5f * tickRate));
        await server.WaitAssertion(() =>
            Assert.That(slots.GetItemOrNull(systems, "fusion_core"), Is.Not.Null, "core came out before 3 seconds"));

        await server.WaitRunTicks((int) (2f * tickRate));
        await server.WaitAssertion(() =>
        {
            Assert.That(slots.GetItemOrNull(systems, "fusion_core"), Is.Null, "core wasn't pulled out after 3 seconds");
            var held = entities.System<SharedHandsSystem>().GetActiveItem(thief);
            Assert.That(held != null && entities.GetComponent<MetaDataComponent>(held.Value).EntityPrototype?.ID == "N14FusionCore",
                "the puller isn't holding the core");
            Assert.That(entities.GetComponent<FusionCorePoweredComponent>(systems).Powered, Is.False);
        });

        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task AtomCatsWearerPredictsTheServersCoreSpeed(bool deployRig)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var map = await pair.CreateTestMap();
        var server = pair.Server.EntMan;
        var client = pair.Client.EntMan;
        var session = pair.Server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);
        var tickRate = (int) pair.Server.ResolveDependency<IGameTiming>().TickRate;
        EntityUid wearer = default;
        NetEntity netWearer = default;
        await pair.Server.WaitAssertion(() =>
        {
            wearer = server.SpawnEntity("MobHuman", map.GridCoords);
            netWearer = server.GetNetEntity(wearer);
            server.EnsureComponent<PowerArmorProficiencyComponent>(wearer);
            var frame = server.SpawnEntity("AtomCatsT45Blue", map.GridCoords);
            var inventory = server.System<InventorySystem>();
            Assert.That(inventory.TryEquip(wearer, frame, "outerClothing", force: true));
            if (deployRig)
            {
                var rig = server.System<FusionCorePoweredSystem>().GetSuitPowerSystems(frame)!.Value;
                Assert.That(inventory.TryEquip(wearer, rig, "back", force: true));
            }
            pair.Server.PlayerMan.SetAttachedEntity(session, wearer);
        });

        async Task CheckClientSpeed(float expected)
        {
            await pair.Client.WaitAssertion(() =>
            {
                var uid = client.GetEntity(netWearer);
                var speed = client.GetComponent<MovementSpeedModifierComponent>(uid);
                Assert.That(speed.WalkSpeedModifier, Is.EqualTo(expected).Within(0.001f));
                client.System<MovementSpeedModifierSystem>().RefreshMovementSpeedModifiers(uid);
                Assert.That(speed.WalkSpeedModifier, Is.EqualTo(expected).Within(0.001f));
                Assert.That(speed.SprintSpeedModifier, Is.EqualTo(expected).Within(0.001f));
            });
        }

        await pair.RunTicksSync(20);
        await CheckClientSpeed(1f);
        await pair.Server.WaitAssertion(() =>
            Assert.That(server.System<FusionCorePoweredSystem>().TryStartRemoveFromWearer(wearer, wearer)));
        await pair.RunTicksSync(3 * tickRate + 8);
        await CheckClientSpeed(DeadSpeed);
        await pair.Server.WaitPost(() => pair.Server.PlayerMan.SetAttachedEntity(session, null));
        await pair.CleanReturnAsync();
    }

    [TestCase(3)]
    [TestCase(10)]
    public async Task AtomCatsFrameWithoutPowerSystemsIsUnpowered(int strength)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            var wearer = em.SpawnEntity("MobHuman", map.GridCoords);
            em.EnsureComponent<SpecialComponent>(wearer).BaseStrength = strength;
            em.EnsureComponent<PowerArmorProficiencyComponent>(wearer);
            em.EnsureComponent<SpeedModifierImmunityComponent>(wearer);
            var frame = em.SpawnEntity("AtomCatsPowerArmorFrame", map.GridCoords);
            var rig = em.System<FusionCorePoweredSystem>().GetSuitPowerSystems(frame)!.Value;
            em.DeleteEntity(rig);
            Assert.That(em.System<InventorySystem>().TryEquip(wearer, frame, "outerClothing", force: true));
            var speed = em.GetComponent<MovementSpeedModifierComponent>(wearer);
            Assert.That(speed.WalkSpeedModifier, Is.EqualTo(DeadSpeed).Within(0.001f));
            Assert.That(speed.SprintSpeedModifier, Is.EqualTo(DeadSpeed).Within(0.001f));
        });
        await pair.CleanReturnAsync();
    }

    [TestCase("ReclamationPowerArmorFrame", false)]
    [TestCase("ReclamationPowerArmorFrame", true)]
    [TestCase("AtomCatsT45Red", false)]
    [TestCase("AtomCatsT45Red", true)]
    public async Task WearerCanRemoveInstallAndSwapOwnCore(string frameId, bool deployRig)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var em = server.ResolveDependency<IEntityManager>();
        var loc = server.ResolveDependency<ILocalizationManager>();
        var tickRate = (int) server.ResolveDependency<IGameTiming>().TickRate;
        var inventory = em.System<InventorySystem>();
        var hands = em.System<SharedHandsSystem>();
        var slots = em.System<ItemSlotsSystem>();
        EntityUid wearer = default, frame = default, rig = default, original = default, replacement = default;

        await server.WaitAssertion(() =>
        {
            wearer = em.SpawnEntity("MobHuman", map.GridCoords);
            em.EnsureComponent<SpecialComponent>(wearer).BaseStrength = 3;
            frame = em.SpawnEntity(frameId, map.GridCoords);
            Assert.That(inventory.TryEquip(wearer, frame, "outerClothing", force: true));
            rig = em.GetComponent<ToggleableClothingComponent>(frame).ClothingUids.Single(p => p.Value == "back").Key;
            if (deployRig)
                Assert.That(inventory.TryEquip(wearer, rig, "back", force: true));
            var speed = em.GetComponent<MovementSpeedModifierComponent>(wearer);
            Assert.That(speed.WalkSpeedModifier, Is.EqualTo(PoweredSpeed).Within(0.001f));
            Assert.That(speed.SprintSpeedModifier, Is.EqualTo(PoweredSpeed).Within(0.001f));
            em.EnsureComponent<PowerArmorProficiencyComponent>(wearer);
            Assert.That(speed.WalkSpeedModifier, Is.EqualTo(1f).Within(0.001f));
            Assert.That(speed.SprintSpeedModifier, Is.EqualTo(1f).Within(0.001f));
            original = slots.GetItemOrNull(rig, "fusion_core")!.Value;
            var target = deployRig ? wearer : frame;
            var verbs = em.System<SharedVerbSystem>().GetLocalVerbs(target, wearer, typeof(AlternativeVerb));
            verbs.Single(v => v.Text == loc.GetString("fusion-core-verb-remove")).Act!();
        });
        await server.WaitRunTicks(tickRate * 2);
        await server.WaitAssertion(() => Assert.That(slots.GetItemOrNull(rig, "fusion_core"), Is.EqualTo(original)));
        await server.WaitRunTicks(tickRate + 2);
        await server.WaitAssertion(() =>
        {
            Assert.That(slots.GetItemOrNull(rig, "fusion_core"), Is.Null);
            Assert.That(hands.IsHolding(wearer, original));
            Assert.That(em.GetComponent<FusionCorePoweredComponent>(rig).Powered, Is.False);
            var speed = em.GetComponent<MovementSpeedModifierComponent>(wearer);
            var movement = em.System<MovementSpeedModifierSystem>();
            void CheckCrawl()
            {
                Assert.That(speed.WalkSpeedModifier, Is.EqualTo(DeadSpeed).Within(0.001f));
                Assert.That(speed.SprintSpeedModifier, Is.EqualTo(DeadSpeed).Within(0.001f));
            }
            CheckCrawl();
            em.RemoveComponent<PowerArmorProficiencyComponent>(wearer);
            CheckCrawl();
            em.EnsureComponent<PowerArmorProficiencyComponent>(wearer);
            em.EnsureComponent<SpeedModifierImmunityComponent>(wearer);
            movement.RefreshMovementSpeedModifiers(wearer);
            CheckCrawl();
            em.RemoveComponent<SpeedModifierImmunityComponent>(wearer);
            var rage = em.EnsureComponent<EnduranceEnragedComponent>(wearer);
            rage.Active = true;
            rage.EndsAt = server.ResolveDependency<IGameTiming>().CurTime + TimeSpan.FromMinutes(1);
            movement.RefreshMovementSpeedModifiers(wearer);
            CheckCrawl();
            var boosted = new RefreshMovementSpeedModifiersEvent();
            em.EventBus.RaiseLocalEvent(wearer, boosted);
            boosted.ModifySpeed(2f);
            Assert.That(boosted.WalkSpeedModifier, Is.EqualTo(DeadSpeed).Within(0.001f));
            em.RemoveComponent<EnduranceEnragedComponent>(wearer);
            var target = deployRig ? frame : wearer;
            var use = new InteractUsingEvent(wearer, original, target, map.GridCoords);
            em.EventBus.RaiseLocalEvent(target, use);
            Assert.That(use.Handled);
        });
        await server.WaitRunTicks(tickRate * 3 + 2);
        await server.WaitAssertion(() =>
        {
            Assert.That(slots.GetItemOrNull(rig, "fusion_core"), Is.EqualTo(original));
            Assert.That(em.GetComponent<FusionCorePoweredComponent>(rig).Powered);
            var speed = em.GetComponent<MovementSpeedModifierComponent>(wearer);
            Assert.That(speed.WalkSpeedModifier, Is.EqualTo(1f).Within(0.001f));
            Assert.That(speed.SprintSpeedModifier, Is.EqualTo(1f).Within(0.001f));
            replacement = em.SpawnEntity("N14FusionCore", map.GridCoords);
            Assert.That(hands.TryPickupAnyHand(wearer, replacement));
            var verbs = em.System<SharedVerbSystem>().GetLocalVerbs(frame, wearer, typeof(AlternativeVerb));
            verbs.Single(v => v.Text == loc.GetString("fusion-core-verb-install")).Act!();
        });
        await server.WaitRunTicks(tickRate * 3 + 2);
        await server.WaitAssertion(() =>
        {
            Assert.That(slots.GetItemOrNull(rig, "fusion_core"), Is.EqualTo(replacement));
            Assert.That(hands.IsHolding(wearer, original));
            Assert.That(inventory.TryGetSlotEntity(wearer, "outerClothing", out var worn) && worn == frame);
            em.System<BatterySystem>().SetCharge(replacement, 0.1f);
        });
        await server.WaitRunTicks(tickRate + 2);
        await server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<BatteryComponent>(replacement).CurrentCharge, Is.Zero);
            var speed = em.GetComponent<MovementSpeedModifierComponent>(wearer);
            Assert.That(speed.WalkSpeedModifier, Is.EqualTo(DeadSpeed).Within(0.001f));
            Assert.That(speed.SprintSpeedModifier, Is.EqualTo(DeadSpeed).Within(0.001f));
        });
        await pair.CleanReturnAsync();
    }

    [TestCase("ReclamationPowerArmorFrame", false)]
    [TestCase("N14ClothingOuterPowerArmorT45", true)]
    public async Task EmpSpareCoreDrainsWhileInstalledCoreAndWearerAreProtected(string frameId, bool deployRig)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var wearer = em.SpawnEntity("MobHuman", map.GridCoords);
            var frame = em.SpawnEntity(frameId, map.GridCoords);
            var rig = em.GetComponent<ToggleableClothingComponent>(frame).ClothingUids.Single(p => p.Value == "back").Key;
            var inventory = em.System<InventorySystem>();
            Assert.That(inventory.TryEquip(wearer, frame, "outerClothing", force: true));
            if (deployRig)
                Assert.That(inventory.TryEquip(wearer, rig, "back", force: true));
            var slots = em.System<ItemSlotsSystem>();
            var core = slots.GetItemOrNull(rig, "fusion_core")!.Value;
            var plates = PowerArmorPlates.Slots.Select(slot => slots.GetItemOrNull(frame, PowerArmorPlates.Container(slot)))
                .OfType<EntityUid>().ToArray();
            var installed = em.GetComponent<BatteryComponent>(core);
            var charge = installed.CurrentCharge;
            var spare = em.SpawnEntity("N14FusionCore", map.GridCoords);
            var body = em.GetComponent<DamageableComponent>(wearer);
            var injury = body.TotalDamage;
            var emp = em.System<EmpSystem>();
            emp.EmpPulse(map.MapCoords, 5, 10000, 10);
            // Check a directed pulse too, even when the rig or core is nested in equipment.
            emp.TryEmpEffects(core, 10000, 10);
            Assert.That(em.GetComponent<BatteryComponent>(spare).CurrentCharge, Is.Zero);
            Assert.That(em.HasComponent<EmpDisabledComponent>(spare));
            Assert.That(body.TotalDamage, Is.EqualTo(injury));
            foreach (var plate in plates)
                Assert.That(em.GetComponent<DamageableComponent>(plate).TotalDamage.Float(), Is.Zero);
            Assert.That(installed.CurrentCharge, Is.EqualTo(charge));
            foreach (var item in new[] { frame, rig, core })
                Assert.That(em.HasComponent<EmpDisabledComponent>(item), Is.False);
            em.System<FusionCorePoweredSystem>().UpdatePowered((rig, em.GetComponent<FusionCorePoweredComponent>(rig)));
            Assert.That(em.GetComponent<FusionCorePoweredComponent>(rig).Powered);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PendingRemovalCannotTakeAnotherCoreOrFollowAnUnequippedFrame()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var em = server.ResolveDependency<IEntityManager>();
        var tickRate = (int) server.ResolveDependency<IGameTiming>().TickRate;
        var inventory = em.System<InventorySystem>();
        var hands = em.System<SharedHandsSystem>();
        var slots = em.System<ItemSlotsSystem>();
        var fusion = em.System<FusionCorePoweredSystem>();
        EntityUid wearer = default, rig = default, helper = default, replacement = default;
        await server.WaitAssertion(() =>
        {
            wearer = em.SpawnEntity("MobHuman", map.GridCoords);
            helper = em.SpawnEntity("MobHuman", map.GridCoords);
            var frame = em.SpawnEntity("ReclamationPowerArmorFrame", map.GridCoords);
            Assert.That(inventory.TryEquip(wearer, frame, "outerClothing", force: true));
            rig = em.GetComponent<ToggleableClothingComponent>(frame).ClothingUids.Single(p => p.Value == "back").Key;
            replacement = em.SpawnEntity("N14FusionCore", map.GridCoords);
            Assert.That(hands.TryPickupAnyHand(wearer, replacement));
            Assert.That(fusion.TryStartInstallInWearer(wearer, wearer, replacement));
        });
        await server.WaitRunTicks(tickRate);
        await server.WaitAssertion(() => Assert.That(fusion.TryStartRemoveFromWearer(helper, wearer)));
        await server.WaitRunTicks(tickRate * 3 + 2);
        await server.WaitAssertion(() =>
        {
            Assert.That(slots.GetItemOrNull(rig, "fusion_core"), Is.EqualTo(replacement));
            Assert.That(hands.IsHolding(helper, replacement), Is.False);
            Assert.That(fusion.TryStartRemoveFromWearer(wearer, wearer));
            Assert.That(inventory.TryUnequip(wearer, "outerClothing", force: true));
        });
        await server.WaitRunTicks(tickRate * 3 + 2);
        await server.WaitAssertion(() => Assert.That(slots.GetItemOrNull(rig, "fusion_core"), Is.EqualTo(replacement)));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CoreComesOutOfSuitOnTheFloor()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var loc = server.ResolveDependency<ILocalizationManager>();

        await server.WaitAssertion(() =>
        {
            var fusion = entities.System<FusionCorePoweredSystem>();
            var hands = entities.System<SharedHandsSystem>();
            var user = entities.SpawnEntity("MobHuman", map.GridCoords);
            var suit = entities.SpawnEntity("N14ClothingOuterPowerArmorT51", map.GridCoords);

            var verbs = entities.System<SharedVerbSystem>().GetLocalVerbs(suit, user, typeof(AlternativeVerb));
            Assert.That(verbs.Any(v => v.Text == loc.GetString("fusion-core-verb-remove")), "no Remove fusion core option on the floor");

            Assert.That(fusion.TryRemoveCoreFromSuit(suit, user), Is.True, "couldn't take the core out of a suit on the floor");
            var core = hands.GetActiveItem(user);
            Assert.That(core != null && entities.GetComponent<MetaDataComponent>(core.Value).EntityPrototype?.ID == "N14FusionCore",
                "the core didn't end up in the user's hand");

            Assert.That(fusion.TryInsertCoreIntoSuit(suit, core!.Value, user), Is.True, "couldn't put a core into the suit on the floor");
            Assert.That(hands.GetActiveItem(user), Is.Null);

            // Once worn, the instant floor route is closed (others must do the 3-second pull).
            var wearer = entities.SpawnEntity("MobHuman", map.GridCoords);
            Assert.That(entities.System<InventorySystem>().TryEquip(wearer, suit, "outerClothing", silent: true, force: true), Is.True);
            Assert.That(fusion.TryRemoveCoreFromSuit(suit, user), Is.False, "instant removal from a worn suit");
        });

        await pair.CleanReturnAsync();
    }
}
