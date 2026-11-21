#nullable enable annotations

using Content.Client.Alerts;
using Content.Client.UserInterface.Systems.DamageOverlays.Overlays;
using Content.IntegrationTests.Pair;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Traits;
using Content.Shared._Misfits.PowerArmor;
using Content.Shared._Misfits.Special;
using Content.Shared._Misfits.Special.Components;
using Content.Shared._ReclamationProject.PowerArmor;
using Content.Shared.Armor;
using Content.Shared.Alert;
using Content.Shared.Atmos;
using Content.Shared.Body.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Explosion;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory;
using Content.Shared.Item;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Projectiles;
using Content.Shared.Standing;
using Content.Shared.Traits;
using Content.Shared.Clothing.Components;
using System.Linq;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Components;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Client.Graphics;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using SpriteComponent = Robust.Client.GameObjects.SpriteComponent;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture]
public sealed class ModularPowerArmorTest
{
    [Test]
    public async Task WendoverArmorMonumentCanInitializeAlongsideAWearableSuit()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            var em = server.EntMan;
            var maps = server.System<SharedMapSystem>();
            var map = maps.CreateMap(out var mapId, runMapInit: false);
            var position = new EntityCoordinates(map, 0, 0);
            const string model = "N14ClothingOuterPowerArmorT51China";

            // Wendover's monument strips these components when the map is deserialized.
            var monument = em.CreateEntityUninitialized(model, position);
            em.RemoveComponent<ClothingComponent>(monument);
            em.RemoveComponent<ArmorComponent>(monument);
            em.RemoveComponent<ItemComponent>(monument);
            em.InitializeAndStartEntity(monument, mapId);
            var wearable = em.SpawnEntity(model, position);

            maps.InitializeMap(mapId);
            Assert.That(em.HasComponent<ModularPowerArmorComponent>(monument), Is.False);
            Assert.That(em.HasComponent<ArmorComponent>(monument), Is.False);
            var suit = em.GetComponent<ModularPowerArmorComponent>(wearable);
            Assert.That(suit.Sealed, Is.True);
            Assert.That(suit.Rating, Is.EqualTo(55));
            maps.DeleteMap(mapId);
        });
        await pair.CleanReturnAsync();
    }

    [TestCase("ReclamationPowerArmorFrame", false)]
    [TestCase("N14ClothingOuterPowerArmorT45", true)]
    [TestCase("AtomCatsT45Red", false)]
    [TestCase(null, false)]
    public async Task DamageCircleTracksWearer(string? suitId, bool reconnect)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        if (reconnect)
        {
            var network = pair.Client.ResolveDependency<IClientNetManager>();
            await pair.Client.WaitPost(() => network.ClientDisconnect("Testing injury feedback after rejoining"));
            await pair.ReallyBeIdle(10);
            await pair.Client.WaitPost(() => network.ClientConnect(null!, 0, null!));
            await pair.ReallyBeIdle(10);
        }
        var map = await pair.CreateTestMap();
        var server = pair.Server.ResolveDependency<IEntityManager>();
        var client = pair.Client.ResolveDependency<IEntityManager>();
        var overlays = pair.Client.ResolveDependency<IOverlayManager>();
        var session = pair.Server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);
        EntityUid wearer = default;
        NetEntity netWearer = default;

        void Hit(EntityUid uid, string type, float amount) =>
            server.System<DamageableSystem>().TryChangeDamage(uid,
                new DamageSpecifier { DamageDict = { [type] = amount } }, doPartDamage: false);

        float CheckInjuryCircle()
        {
            var uid = client.GetEntity(netWearer);
            var wounds = client.GetComponent<DamageableComponent>(uid).Damage;
            var critical = client.System<MobThresholdSystem>().GetThresholdForState(uid, MobState.Critical);
            var expected = (wounds.DamageDict["Blunt"] + wounds.DamageDict["Heat"]).Float() / critical.Float();
            var level = overlays.GetOverlay<DamageOverlay>().InjuryLevel;
            Assert.That(level, Is.EqualTo(expected).Within(.0001f));
            return level;
        }

        await pair.Server.WaitAssertion(() =>
        {
            wearer = server.SpawnEntity("MobHuman", map.GridCoords);
            netWearer = server.GetNetEntity(wearer);
            pair.Server.PlayerMan.SetAttachedEntity(session, wearer);
            if (suitId != null)
            {
                var suit = server.SpawnEntity(suitId, map.GridCoords);
                Assert.That(server.System<InventorySystem>().TryEquip(wearer, suit, "outerClothing"));
                if (server.System<ItemSlotsSystem>().GetItemOrNull(suit, PowerArmorPlates.Container(ArmorPlateSlot.Chest)) is {} plate)
                    Hit(plate, "Blunt", 30);
            }
        });
        await pair.RunTicksSync(20);
        await pair.Client.WaitAssertion(() => Assert.That(overlays.GetOverlay<DamageOverlay>().InjuryLevel, Is.Zero));

        var previousLevel = 0f;
        foreach (var (type, amount) in new[] { ("Blunt", 3f), ("Heat", 30f), ("Blunt", 45f) })
        {
            await pair.Server.WaitAssertion(() =>
            {
                var body = server.GetComponent<DamageableComponent>(wearer);
                var before = body.TotalDamage;
                Hit(wearer, type, amount);
                Assert.That(body.TotalDamage, Is.GreaterThan(before));
            });
            await pair.RunTicksSync(12);
            await pair.Client.WaitAssertion(() =>
            {
                var level = CheckInjuryCircle();
                Assert.That(level, Is.GreaterThan(previousLevel));
                previousLevel = level;
            });
        }

        await pair.Server.WaitAssertion(() =>
        {
            if (suitId != null)
                Assert.That(server.System<InventorySystem>().TryUnequip(wearer, "outerClothing", force: true));
            Hit(wearer, "Asphyxiation", 8f);
        });
        await pair.RunTicksSync(5);
        await pair.Client.WaitAssertion(() =>
        {
            Assert.That(CheckInjuryCircle(), Is.Positive);
            Assert.That(overlays.GetOverlay<DamageOverlay>().OxygenLevel, Is.Positive);
        });
        await pair.Server.WaitAssertion(() =>
            server.System<DamageableSystem>().SetAllDamage(wearer, server.GetComponent<DamageableComponent>(wearer), 0));
        await pair.RunTicksSync(12);
        await pair.Client.WaitAssertion(() =>
        {
            var overlay = overlays.GetOverlay<DamageOverlay>();
            Assert.That(overlay.InjuryLevel, Is.Zero);
            Assert.That(overlay.OxygenLevel, Is.Zero);
        });
        await pair.Server.WaitPost(() => pair.Server.PlayerMan.SetAttachedEntity(session, null));
        await pair.CleanReturnAsync();
    }

    [TestCase("Left", "Right", true)]
    [TestCase("Right", "Left", false)]
    public async Task FrameSupportsMissingLegs(string firstLeg, string secondLeg, bool trained)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
        EntityUid wearer = default;
        await pair.Server.WaitAssertion(() =>
        {
            wearer = em.SpawnTraumaPatient(map.GridCoords);
            if (trained)
                em.EnsureComponent<PowerArmorProficiencyComponent>(wearer);
            var frame = em.SpawnEntity("ReclamationPowerArmorFrame", map.GridCoords);
            var inventory = em.System<InventorySystem>();
            var hands = em.System<SharedHandsSystem>();
            var standing = em.System<StandingStateSystem>();
            var speed = em.GetComponent<MovementSpeedModifierComponent>(wearer);
            var legs = em.GetComponent<BodyComponent>(wearer).LegEntities;
            void Equip() => Assert.That(inventory.TryEquip(wearer, frame, "outerClothing", silent: true));
            void Remove() => Assert.That(inventory.TryUnequip(wearer, "outerClothing", silent: true, force: true));
            void Amputate(string side) => em.System<TraitSystem>().AddTrait(wearer,
                prototypes.Index<TraitPrototype>("ReclamationAmputated" + side + "Leg"));
            Equip();
            var walk = speed.CurrentWalkSpeed;
            var sprint = speed.CurrentSprintSpeed;
            var acceleration = speed.Acceleration;
            Assert.That(walk, Is.Positive);
            void CheckFrameWalking()
            {
                Assert.That(standing.IsDown(wearer), Is.False);
                Assert.That(speed.CurrentWalkSpeed, Is.EqualTo(walk).Within(.001f));
                Assert.That(speed.CurrentSprintSpeed, Is.EqualTo(sprint).Within(.001f));
                Assert.That(speed.Acceleration, Is.EqualTo(acceleration).Within(.001f));
            }
            Remove();
            Amputate(firstLeg);
            Assert.That(legs, Has.Count.EqualTo(1));
            Assert.That(standing.IsDown(wearer));
            var cane = em.SpawnEntity("Cane", map.GridCoords);
            Assert.That(hands.TryPickupAnyHand(wearer, cane));
            var caneWalk = speed.CurrentWalkSpeed;
            Assert.That(standing.IsDown(wearer), Is.False);
            Equip();
            CheckFrameWalking();
            Remove();
            Assert.That(standing.IsDown(wearer), Is.False);
            Assert.That(speed.CurrentWalkSpeed, Is.EqualTo(caneWalk).Within(.001f));
            Equip();
            Assert.That(hands.TryDrop(wearer, cane));
            CheckFrameWalking();
            Amputate(secondLeg);
            Assert.That(legs, Is.Empty);
            CheckFrameWalking();
            Remove();
            Assert.That(standing.IsDown(wearer));
            Assert.That(speed.BaseWalkSpeed, Is.Zero);
            Assert.That(standing.Stand(wearer), Is.False);
            Assert.That(hands.TryPickupAnyHand(wearer, cane));
            Assert.That(standing.Stand(wearer), Is.False);
            Equip();
            CheckFrameWalking();
            Assert.That(legs, Is.Empty);
            Assert.That(standing.Down(wearer, dropHeldItems: false));
            Assert.That(em.System<SharedLayingDownSystem>().TryStandUp(wearer));
        });
        await pair.RunTicksSync(pair.Server.ResolveDependency<IGameTiming>().TickRate * 3);
        await pair.Server.WaitAssertion(() => Assert.That(em.System<StandingStateSystem>().IsDown(wearer), Is.False));
        await pair.CleanReturnAsync();
    }

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
            Assert.That(speed.WalkSpeedModifier, Is.EqualTo(normalWalk * 0.10f).Within(0.005f));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task WornPiecesAndHudReplicate()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var map = await pair.CreateTestMap();
        var server = pair.Server.ResolveDependency<IEntityManager>();
        var client = pair.Client.ResolveDependency<IEntityManager>();
        var protos = pair.Client.ResolveDependency<IPrototypeManager>();
        var session = pair.Server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);
        NetEntity netWearer = default;
        EntityUid suit = default;
        EntityUid gloves = default;
        var gloveSlot = PowerArmorPlates.Container(ArmorPlateSlot.Gloves);
        await pair.Server.WaitAssertion(() =>
        {
            var wearer = server.SpawnEntity("MobHuman", map.GridCoords);
            netWearer = server.GetNetEntity(wearer);
            pair.Server.PlayerMan.SetAttachedEntity(session, wearer);
            suit = server.SpawnEntity("N14ClothingOuterPowerArmorT45", map.GridCoords);
            Assert.That(server.System<InventorySystem>().TryEquip(wearer, suit, "outerClothing", force: true));
            gloves = server.System<ItemSlotsSystem>().GetItemOrNull(suit, gloveSlot)!.Value;
        });

        void CheckHud(int rating, int level, bool hasGloves)
        {
            var wearer = client.GetEntity(netWearer);
            var view = client.SpawnEntity("ReclamationPowerArmorRatingView", MapCoordinates.Nullspace);
            var sprite = client.GetComponent<SpriteComponent>(view);
            var alert = protos.Index<AlertPrototype>("ModularPowerArmorRating");
            var state = client.System<AlertsSystem>().GetActiveAlerts(wearer)![alert.AlertKey];
            Assert.That(state.Severity, Is.EqualTo(level));
            Assert.That(((SpriteSpecifier.Rsi) alert.GetIcon(state.Severity)).RsiState, Is.EqualTo("condition" + level));
            var ev = new UpdateAlertSpriteEvent((view, sprite), alert);
            client.EventBus.RaiseLocalEvent(wearer, ref ev);
            Assert.That(sprite.LayerMapTryGet("pa-tens", out var tensIndex), Is.True);
            Assert.That(sprite[tensIndex].RsiState.Name, Is.EqualTo((rating / 10).ToString()));
            Assert.That(sprite.LayerMapTryGet("pa-ones", out var onesIndex), Is.True);
            Assert.That(sprite[onesIndex].RsiState.Name, Is.EqualTo((rating % 10).ToString()));
            var worn = client.GetComponent<SpriteComponent>(wearer);
            Assert.That(worn.LayerMapTryGet("pa-frame", out _), Is.True);
            Assert.That(worn.LayerMapTryGet("pa-Gloves", out _), Is.EqualTo(hasGloves));
            client.DeleteEntity(view);
        }

        await pair.RunTicksSync(20);
        await pair.Client.WaitAssertion(() => CheckHud(45, 10, true));
        var hit = new DamageSpecifier();
        hit.DamageDict["Blunt"] = 30;
        for (var impact = 1; impact <= 2; impact++)
        {
            await pair.Server.WaitPost(() => server.System<DamageableSystem>().TryChangeDamage(gloves, hit, ignoreResistances: true));
            await pair.RunTicksSync(20);
            if (impact == 1)
                await pair.Client.WaitAssertion(() => CheckHud(45, 9, true));
            else
                await pair.Client.WaitAssertion(() => CheckHud(42, 8, true));
        }
        await pair.Server.WaitAssertion(() =>
        {
            server.System<DamageableSystem>().SetAllDamage(gloves, server.GetComponent<DamageableComponent>(gloves), 0);
            Assert.That(server.System<ItemSlotsSystem>().GetItemOrNull(suit, gloveSlot), Is.EqualTo(gloves));
        });
        await pair.RunTicksSync(20);
        await pair.Client.WaitAssertion(() => CheckHud(45, 10, true));
        await pair.Server.WaitAssertion(() =>
        {
            foreach (var slot in PowerArmorPlates.Slots)
                Assert.That(server.System<ItemSlotsSystem>().TryEject(suit, PowerArmorPlates.Container(slot), null, out _));
        });
        await pair.RunTicksSync(20);
        await pair.Client.WaitAssertion(() => CheckHud(20, 0, false));
        await pair.Server.WaitPost(() => pair.Server.PlayerMan.SetAttachedEntity(session, null));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FireCannotHurtASealedWearerOrTheirPlates()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid wearer = default, frame = default, groundFire = default;
        EntityUid[] plates = [];
        void Shoot(string projectileId, EntityUid target)
        {
            var projectile = em.SpawnEntity(projectileId, map.GridCoords);
            var hit = new ProjectileHitEvent(em.GetComponent<ProjectileComponent>(projectile).Damage, target);
            em.EventBus.RaiseLocalEvent(projectile, ref hit);
            em.System<DamageableSystem>().TryChangeDamage(target, hit.Damage, damageSource: projectile);
            em.DeleteEntity(projectile);
        }
        void CheckForBurns()
        {
            Assert.That(em.GetComponent<FlammableComponent>(wearer).OnFire, Is.False);
            Assert.That(em.GetComponent<DamageableComponent>(wearer).Damage.DamageDict["Heat"].Float(), Is.Zero);
            Assert.That(em.GetComponent<DamageableComponent>(frame).TotalDamage.Float(), Is.Zero);
            foreach (var plate in plates)
                Assert.That(em.GetComponent<DamageableComponent>(plate).TotalDamage.Float(), Is.Zero);
        }
        await pair.Server.WaitAssertion(() =>
        {
            var slots = em.System<ItemSlotsSystem>();
            wearer = em.SpawnEntity("MobHuman", map.GridCoords);
            frame = em.SpawnEntity("N14ClothingOuterPowerArmorT45", map.GridCoords);
            var donor = em.SpawnEntity("N14ClothingOuterPowerArmorT51", map.GridCoords);
            var mount = PowerArmorPlates.Container(ArmorPlateSlot.Helmet);
            Assert.That(slots.TryEject(frame, mount, null, out _));
            Assert.That(slots.TryEject(donor, mount, null, out var mixedHelmet));
            Assert.That(slots.TryInsert(frame, mount, mixedHelmet!.Value, null));
            Assert.That(em.System<InventorySystem>().TryEquip(wearer, frame, "outerClothing", force: true));
            plates = PowerArmorPlates.Slots.Select(slot => slots.GetItemOrNull(frame, PowerArmorPlates.Container(slot))!.Value).ToArray();
            foreach (var target in plates.Append(frame).Append(wearer))
                Shoot("BulletFlame", target);
            Shoot("BulletIncinerator", wearer);
            Shoot("ProjectileFireball", wearer);
            var burningAxe = em.SpawnEntity("FireAxeFlaming", map.GridCoords);
            var contact = new DamageSpecifier();
            contact.DamageDict["Heat"] = 12;
            em.System<DamageableSystem>().TryChangeDamage(wearer, contact, damageSource: burningAxe);
            var heat = new DamageSpecifier();
            heat.DamageDict["Heat"] = 40;
            var fireball = new BeforeExplodeEvent(heat, "IncineratorExplosion", []);
            em.EventBus.RaiseLocalEvent(wearer, ref fireball);
            Assert.That(fireball.Damage.AnyPositive(), Is.False);
            em.System<DamageableSystem>().TryChangeDamage(wearer, fireball.Damage, ignoreResistances: true);
            groundFire = em.SpawnEntity("MisfitsTileFireNapalm", map.GridCoords);
            CheckForBurns();
        });
        await pair.RunTicksSync(pair.Server.ResolveDependency<IGameTiming>().TickRate * 2);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.HasComponent<Content.Server._Misfits.Fire.TileFireTouchComponent>(wearer));
            CheckForBurns();
            Assert.That(em.GetComponent<FlammableComponent>(wearer).FireStacks, Is.LessThanOrEqualTo(0f));
            em.DeleteEntity(groundFire);
            Shoot("BulletLaser", wearer);
            Assert.That(plates.Any(plate => em.GetComponent<DamageableComponent>(plate).TotalDamage > 0), Is.True);
            Assert.That(em.System<ItemSlotsSystem>().TryEject(frame, PowerArmorPlates.Container(ArmorPlateSlot.Helmet), null, out _));
            var before = em.GetComponent<DamageableComponent>(wearer).Damage.DamageDict["Heat"];
            Shoot("BulletFlame", wearer);
            Assert.That(em.GetComponent<DamageableComponent>(wearer).Damage.DamageDict["Heat"].Float(), Is.GreaterThan(before.Float()));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MixedPlatesStayMountedThroughBreakageAndTenSecondWeld()
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
            Assert.That(slots.GetItemOrNull(frame, mount), Is.EqualTo(helmet));
            Assert.That(chassis.Visuals.ContainsKey(ArmorPlateSlot.Helmet));
            Assert.That(chassis.BrokenSlots, Does.Contain(ArmorPlateSlot.Helmet));
            Assert.That(inv.TryGetSlotEntity(wearer, "head", out var wornHelmet));
            Assert.That(em.System<ModularArmorDamageSystem>().IsBrokenHelmet(wornHelmet!.Value));
            Assert.That(em.System<ModularArmorDamageSystem>().ImpactPlate(frame, Content.Shared._Shitmed.Targeting.TargetBodyPart.Head), Is.Null);
            Assert.That(chassis.Rating, Is.EqualTo(40));
            Assert.That(chassis.Sealed, Is.False);
            var open = new GetFireProtectionEvent();
            inv.RelayEvent((wearer, em.GetComponent<InventoryComponent>(wearer)), ref open);
            Assert.That(open.Multiplier, Is.GreaterThan(0));
            flames.AdjustFireStacks(wearer, 2, flammable);
            flames.Ignite(wearer, frame, flammable, ignoreFireProtection: true);
            Assert.That(flammable.OnFire, Is.True);
            flames.Extinguish(wearer, flammable);
            Assert.That(em.GetComponent<TransformComponent>(helmet).ParentUid, Is.EqualTo(frame));
            var repairer = em.SpawnEntity("MobHuman", map.GridCoords);
            em.EnsureComponent<GodmodeComponent>(repairer);
            em.EnsureComponent<GodmodeComponent>(wearer);
            var welder = em.SpawnEntity("Welder", map.GridCoords);
            Assert.That(em.System<SharedHandsSystem>().TryPickup(repairer, welder), Is.True);
            Assert.That(em.System<ItemToggleSystem>().TryActivate((welder, null), repairer), Is.True);
            Assert.That(em.System<SharedInteractionSystem>().InteractUsing(repairer, welder, wearer, map.GridCoords));
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
            Assert.That(slots.GetItemOrNull(frame, mount), Is.EqualTo(helmet));
            Assert.That(chassis.Rating, Is.EqualTo(45));
            Assert.That(chassis.BrokenSlots, Is.Empty);
            Assert.That(em.System<ModularArmorDamageSystem>().ImpactPlate(frame, Content.Shared._Shitmed.Targeting.TargetBodyPart.Head), Is.EqualTo(helmet));
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
