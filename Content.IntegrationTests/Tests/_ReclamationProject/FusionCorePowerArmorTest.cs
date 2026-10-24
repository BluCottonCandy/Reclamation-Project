using System.Linq;
using Content.Server._ReclamationProject.PowerArmor;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared._Misfits.Special.Components;
using Content.Shared._ReclamationProject.PowerArmor;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.IntegrationTests.Pair;
using Content.Shared.Verbs;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture]
public sealed class FusionCorePowerArmorTest
{
    private const float PoweredSpeed = 0.2f * 4.5f;      // unchanged power armour speed
    private const float DeadSpeed = 0.2f * 0.2f;         // 4%
    private const float StrongDeadSpeed = 0.2f * 0.25f;  // 5% at Strength 8+

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

            // A Strength 8 wearer for the 5% check.
            EntityUid strongSystems;
            (strong, strongSystems) = SpawnWearer(entities, map);
            entities.EnsureComponent<SpecialComponent>(strong).BaseStrength = 8;
            KillCore(entities, strongSystems);
            entities.System<MovementSpeedModifierSystem>().RefreshMovementSpeedModifiers(strong);

            KillCore(entities, systems);
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<FusionCorePoweredComponent>(systems).Powered, Is.False);
            Assert.That(entities.GetComponent<MovementSpeedModifierComponent>(wearer).WalkSpeedModifier,
                Is.EqualTo(DeadSpeed).Within(0.005f), "dead core isn't 4% speed");
            Assert.That(entities.GetComponent<MovementSpeedModifierComponent>(strong).WalkSpeedModifier,
                Is.EqualTo(StrongDeadSpeed).Within(0.005f), "Strength 8 dead core isn't 5% speed");

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

            // The wearer can't reach their own core from inside.
            var selfVerbs = entities.System<SharedVerbSystem>().GetLocalVerbs(wearer, wearer, typeof(AlternativeVerb));
            Assert.That(selfVerbs.Any(v => v.Text == loc.GetString("fusion-core-verb-remove")), Is.False, "wearer can pull their own core");
            Assert.That(fusion.TryStartRemoveFromWearer(wearer, wearer), Is.False);

            // Kill the core; a helper fits a fresh one from outside (3 seconds).
            KillCore(entities, systems);
            oldCore = core;
            helper = entities.SpawnEntity("MobHuman", map.GridCoords);
            fresh = entities.SpawnEntity("N14FusionCore", map.GridCoords);
            Assert.That(hands.TryPickupAnyHand(helper, fresh), Is.True);
            Assert.That(fusion.TryStartInstallInWearer(wearer, wearer, fresh), Is.False, "wearer installed into own suit");
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
