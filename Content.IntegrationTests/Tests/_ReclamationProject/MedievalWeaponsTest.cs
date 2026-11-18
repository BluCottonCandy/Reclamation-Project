// #Reclamation Project - medieval weapons
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._ReclamationProject.Medieval;
using Content.Server.Lathe;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared._ReclamationProject.Medieval;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Explosion.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Item;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Lathe;
using Content.Shared.Research.Prototypes;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture]
public sealed class MedievalWeaponsTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: TestMedievalPlainBolt
  components:
  - type: Projectile
    damage:
      types:
        Piercing: 25
";

    [Test]
    public async Task ForgeRecipesNeedTheBlacksmithTrait()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        var proto = pair.Server.ResolveDependency<IPrototypeManager>();
        EntityUid forgeEnt = default, userEnt = default;
        await pair.Server.WaitAssertion(() =>
        {
            var forge = em.SpawnEntity("N14WorkbenchForge", map.GridCoords);
            var user = em.SpawnEntity("MobHuman", map.GridCoords);
            var lathe = em.System<LatheSystem>();
            var smith = em.System<BlacksmithLatheSystem>();
            var recipes = lathe.GetAvailableRecipes(forge, em.GetComponent<LatheComponent>(forge));
            Assert.That(recipes, Does.Contain(new ProtoId<LatheRecipePrototype>("RPSmithLongsword")), "forge lists weapon recipes");
            Assert.That(recipes, Does.Contain(new ProtoId<LatheRecipePrototype>("RPSmithMedievalKnightHarness")), "forge lists armour recipes");
            Assert.That(recipes, Does.Contain(new ProtoId<LatheRecipePrototype>("N14IngotIron")), "normal forge recipes still there");

            var sword = proto.Index<LatheRecipePrototype>("RPSmithLongsword");
            var iron = proto.Index<LatheRecipePrototype>("N14IngotIron");
            Assert.That(smith.CanQueue(forge, user, sword), Is.False, "no trait = can't forge weapons");
            Assert.That(smith.CanQueue(forge, user, iron), Is.True, "no trait = can still smelt");
            em.EnsureComponent<BlacksmithComponent>(user);
            Assert.That(smith.CanQueue(forge, user, sword), Is.True, "trait = can forge");
            forgeEnt = forge;
            userEnt = user;
        });
        // the forge's "needs no power" setting only kicks in on the next tick
        await pair.RunTicksSync(5);
        await pair.Server.WaitAssertion(() =>
        {
            var forge = forgeEnt;
            var user = userEnt;
            // armour needs leather and thread, musket balls need gunpowder: the forge must take them
            var mats = em.System<Content.Server.Materials.MaterialStorageSystem>();
            foreach (var mat in new[] { "N14MaterialLeather1", "N14MaterialThread1", "N14Gunpowder1" })
            {
                var stack = em.SpawnEntity(mat, map.GridCoords);
                Assert.That(mats.TryInsertMaterialEntity(user, stack, forge), Is.True, $"forge accepts {mat}");
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SlingPouchThrowsEverythingAtOnce()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid user = default, sling = default, rock = default, rock2 = default, grenade = default, bullet = default, victim = default;
        await pair.Server.WaitAssertion(() =>
        {
            var storage = em.System<Content.Shared.Storage.EntitySystems.SharedStorageSystem>();
            sling = em.SpawnEntity("RPMedievalSling", map.GridCoords);
            var big = em.SpawnEntity("RPMedievalGreatsword", map.GridCoords);
            Assert.That(storage.CanInsert(sling, big, out _), Is.False, "a greatsword doesn't fit the pouch");
            user = em.SpawnEntity("MobHuman", map.GridCoords);
            Assert.That(em.System<SharedHandsSystem>().TryPickup(user, sling), Is.True);
            victim = em.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(2, 0)));
            rock = em.SpawnEntity("N14MaterialBrick1", map.GridCoords);
            rock2 = em.SpawnEntity("N14MaterialBrick1", map.GridCoords);
            grenade = em.SpawnEntity("ExGrenade", map.GridCoords);
            bullet = em.SpawnEntity("RPMedievalMusketBall", map.GridCoords);
            foreach (var ent in new[] { rock, rock2, grenade, bullet })
                Assert.That(storage.Insert(sling, ent, out _, playSound: false, stackAutomatically: false), Is.True, "pouch holds several things, bullets too");
            Assert.That(em.GetComponent<ItemComponent>(sling).HeldPrefix, Is.EqualTo("loaded"));
        });
        await pair.RunTicksSync(90);   // picking a gun up sets its next shot ~1.25 s ahead
        await pair.Server.WaitAssertion(() =>
        {
            var bulletsBefore = em.Count<Content.Shared.Projectiles.ProjectileComponent>();
            em.System<GunSystem>().AttemptShoot(user, sling, em.GetComponent<GunComponent>(sling), map.GridCoords.Offset(new Vector2(4, 0)));
            var pouch = em.System<Robust.Shared.Containers.SharedContainerSystem>().GetContainer(sling, "storagebase");
            Assert.That(pouch.ContainedEntities.Count, Is.EqualTo(0), "one shot empties the pouch");
            foreach (var ent in new[] { rock, rock2, grenade, bullet })
                Assert.That(em.HasComponent<SlingProjectileComponent>(ent), Is.True, "every slung item hits harder");
            Assert.That(em.HasComponent<ActiveTimerTriggerComponent>(grenade), Is.True, "slung grenade is primed");
            Assert.That(em.EntityExists(bullet) && !em.GetComponent<CartridgeAmmoComponent>(bullet).Spent, Is.True, "the shepherd's sling throws a bullet, it doesn't fire it");
            Assert.That(em.Count<Content.Shared.Projectiles.ProjectileComponent>(), Is.EqualTo(bulletsBefore), "no gunshot came out");
            Assert.That(em.GetComponent<ItemComponent>(sling).HeldPrefix, Is.Null, "empty sling looks empty");

            // headless test worlds don't collide thrown items with mobs, so trigger the hit like ThrownItemSystem does
            em.System<ThrownItemSystem>().ThrowCollideInteraction(em.GetComponent<ThrownItemComponent>(rock), rock, victim);
            Assert.That(em.GetComponent<DamageableComponent>(victim).TotalDamage.Float(), Is.GreaterThanOrEqualTo(12f),
                "the sling's bonus damage landed on the target");
            em.DeleteEntity(grenade);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MusketBallHitsHard()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid target = default;
        await pair.Server.WaitAssertion(() =>
        {
            var user = em.SpawnEntity("MobHuman", map.GridCoords);
            var gun = em.SpawnEntity("RPMedievalTanegashima", map.GridCoords);
            Assert.That(em.System<SharedHandsSystem>().TryPickup(user, gun), Is.True);
            target = em.SpawnEntity("TargetHuman", map.GridCoords.Offset(new Vector2(2, 0)));
            var ball = em.SpawnEntity("RPMedievalMusketBall", map.GridCoords);
            var shots = new List<(EntityUid?, IShootable)> { (ball, em.GetComponent<CartridgeAmmoComponent>(ball)) };
            em.System<GunSystem>().Shoot(gun, em.GetComponent<GunComponent>(gun), shots, map.GridCoords,
                map.GridCoords.Offset(new Vector2(2, 0)), out _, user);
        });
        await pair.RunTicksSync(60);
        await pair.Server.WaitAssertion(() =>
        {
            var total = em.GetComponent<DamageableComponent>(target).TotalDamage;
            Assert.That(total.Float(), Is.GreaterThanOrEqualTo(85f), "a musket ball should do about 90");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SwordOfStateLightsAndBlazesWhenWielded()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var user = em.SpawnEntity("MobHuman", map.GridCoords);
            var sword = em.SpawnEntity("RPMedievalSwordOfState", map.GridCoords);
            Assert.That(em.System<SharedHandsSystem>().TryPickup(user, sword), Is.True);
            var item = em.GetComponent<ItemComponent>(sword);
            var appearance = em.System<SharedAppearanceSystem>();
            var toggle = em.System<ItemToggleSystem>();
            var wield = em.System<WieldableSystem>();

            Assert.That(toggle.Toggle(sword, user, predicted: false), Is.True);
            Assert.That(appearance.TryGetData<string>(sword, MedievalLookVisuals.State, out var lit) ? lit : null, Is.EqualTo("lit"));
            Assert.That(item.HeldPrefix, Is.EqualTo("lit"));

            Assert.That(wield.TryWield(sword, em.GetComponent<WieldableComponent>(sword), user), Is.True);
            Assert.That(appearance.TryGetData<string>(sword, MedievalLookVisuals.State, out var blaze) ? blaze : null, Is.EqualTo("blaze"));
            Assert.That(item.HeldPrefix, Is.EqualTo("blaze-wielded"));

            wield.TryUnwield(sword, em.GetComponent<WieldableComponent>(sword), user);
            Assert.That(item.HeldPrefix, Is.EqualTo("lit"));
            toggle.Toggle(sword, user, predicted: false);
            Assert.That(item.HeldPrefix, Is.Null);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CrossbowShowsItsLoadedSprite()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var bow = em.SpawnEntity("RPMedievalCrossbow", map.GridCoords);
            var arrow = em.SpawnEntity("ArrowRegular", map.GridCoords);
            var appearance = em.System<SharedAppearanceSystem>();
            var item = em.GetComponent<ItemComponent>(bow);
            var containers = em.System<Robust.Shared.Containers.SharedContainerSystem>();
            var slot = containers.GetContainer(bow, "projectiles");

            Assert.That(appearance.TryGetData<string>(bow, MedievalLookVisuals.State, out var empty) ? empty : null, Is.EqualTo("icon"));
            Assert.That(containers.Insert(arrow, slot), Is.False, "can't load an arrow before cranking");
            var crossbow = em.GetComponent<CrossbowComponent>(bow);
            em.System<CrossbowSystem>().SetSpanned(bow, crossbow, true);
            Assert.That(containers.Insert(arrow, slot), Is.True, "cranked crossbow takes an arrow");
            Assert.That(appearance.TryGetData<string>(bow, MedievalLookVisuals.State, out var full) ? full : null, Is.EqualTo("loaded"));
            Assert.That(item.HeldPrefix, Is.EqualTo("loaded"), "held sprite switches to the loaded crossbow");
            Assert.That(containers.Remove(arrow, slot), Is.True);
            Assert.That(item.HeldPrefix, Is.Null);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FlippedVoulgeHeavySwingTrips()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var user = em.SpawnEntity("MobHuman", map.GridCoords);
            var victim = em.SpawnEntity("MobHuman", map.GridCoords);
            var voulge = em.SpawnEntity("RPMedievalSilverVoulge", map.GridCoords);
            var dmg = new DamageSpecifier();

            // a heavy swing while NOT flipped does nothing special
            em.EventBus.RaiseLocalEvent(voulge, new Content.Shared.Weapons.Melee.Events.MeleeHitEvent(new List<EntityUid> { victim }, user, voulge, dmg, Vector2.UnitX));
            Assert.That(em.HasComponent<Content.Shared.Stunnable.KnockedDownComponent>(victim), Is.False);

            Assert.That(em.System<ItemToggleSystem>().Toggle(voulge, user, predicted: false), Is.True);
            // a light attack (no direction) while flipped does nothing special either
            em.EventBus.RaiseLocalEvent(voulge, new Content.Shared.Weapons.Melee.Events.MeleeHitEvent(new List<EntityUid> { victim }, user, voulge, dmg, null));
            Assert.That(em.HasComponent<Content.Shared.Stunnable.KnockedDownComponent>(victim), Is.False);
            // the heavy swing while flipped trips them
            em.EventBus.RaiseLocalEvent(voulge, new Content.Shared.Weapons.Melee.Events.MeleeHitEvent(new List<EntityUid> { victim }, user, voulge, dmg, Vector2.UnitX));
            Assert.That(em.HasComponent<Content.Shared.Stunnable.KnockedDownComponent>(victim), Is.True, "flipped heavy swing knocks down");
            Assert.That(em.HasComponent<Content.Shared.Stunnable.KnockedDownComponent>(user), Is.False, "never the wielder");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CrossbowHitsHarderThanABow()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var bolt = em.SpawnEntity("TestMedievalPlainBolt", map.GridCoords);
            var target = em.SpawnEntity("MobHuman", map.GridCoords);
            var plain = em.GetComponent<Content.Shared.Projectiles.ProjectileComponent>(bolt).Damage.GetTotal();
            em.EnsureComponent<CrossbowBoltComponent>(bolt).BonusDamage = em.GetComponent<CrossbowComponent>(em.SpawnEntity("RPMedievalCrossbow", map.GridCoords)).BonusDamage;
            var ev = new Content.Shared.Projectiles.ProjectileHitEvent(em.GetComponent<Content.Shared.Projectiles.ProjectileComponent>(bolt).Damage, target);
            em.EventBus.RaiseLocalEvent(bolt, ref ev);
            var arrow = em.SpawnEntity("ArrowRegular", map.GridCoords);
            Assert.That(ev.Damage.GetTotal().Float(), Is.EqualTo(plain.Float() + 25f), "crossbow adds 25 to the arrow");
            var sol = em.System<Content.Shared.Chemistry.EntitySystems.SharedSolutionContainerSystem>();
            Assert.That(sol.TryGetSolution(arrow, "ammo", out _, out var ammo) && ammo.MaxVolume == 10, Is.True, "arrows hold 10u");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task UsingTheCrossbowCranksItThenItTakesAnArrow()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid user = default, bow = default;
        await pair.Server.WaitAssertion(() =>
        {
            user = em.SpawnEntity("MobHuman", map.GridCoords);
            bow = em.SpawnEntity("RPMedievalCrossbow", map.GridCoords);
            Assert.That(em.System<SharedHandsSystem>().TryPickup(user, bow), Is.True);
            Assert.That(em.System<Content.Shared.Interaction.SharedInteractionSystem>().UseInHandInteraction(user, bow), Is.True);
            Assert.That(em.GetComponent<WieldableComponent>(bow).Wielded, Is.False, "first use cranks, it does not wield");
        });
        await pair.RunTicksSync(120);   // 4 s > 2.5 s crank
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<CrossbowComponent>(bow).Spanned, Is.True, "cranked after the do-after");
            var arrow = em.SpawnEntity("ArrowRegular", map.GridCoords);
            var containers = em.System<Robust.Shared.Containers.SharedContainerSystem>();
            Assert.That(containers.Insert(arrow, containers.GetContainer(bow, "projectiles")), Is.True, "takes an arrow once cranked");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task HeldMedievalWeaponIsDrawnAtHalfScaleOnTheClient()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var map = await pair.CreateTestMap();
        var server = pair.Server.ResolveDependency<IEntityManager>();
        var client = pair.Client.ResolveDependency<IEntityManager>();
        var session = pair.Server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);
        NetEntity netUser = default;
        await pair.Server.WaitPost(() =>
        {
            var user = server.SpawnEntity("MobHuman", map.GridCoords);
            var bow = server.SpawnEntity("RPMedievalCrossbow", map.GridCoords);
            Assert.That(server.System<SharedHandsSystem>().TryPickup(user, bow), Is.True);
            netUser = server.GetNetEntity(user);
            pair.Server.PlayerMan.SetAttachedEntity(session, user);
        });
        await pair.RunTicksSync(30);
        await pair.Client.WaitAssertion(() =>
        {
            var uid = client.GetEntity(netUser);
            var sprite = client.GetComponent<Robust.Client.GameObjects.SpriteComponent>(uid);
            var found = false;
            foreach (var key in new[] { "inhand-left", "inhand-right" })
            {
                if (!sprite.LayerMapTryGet(key, out var index))
                    continue;
                found = true;
                Assert.That(sprite[index].Scale.X, Is.EqualTo(0.5f).Within(0.001f), "held layer is drawn at half scale");
            }
            Assert.That(found, Is.True, "the crossbow has a held layer on the mob");
        });
        await pair.Server.WaitPost(() => pair.Server.PlayerMan.SetAttachedEntity(session, null));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CrossbowPlayerFlowCrankLoadFire()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid user = default, bow = default, arrow = default, musketGun = default;
        await pair.Server.WaitAssertion(() =>
        {
            user = em.SpawnEntity("MobHuman", map.GridCoords);
            bow = em.SpawnEntity("RPMedievalCrossbow", map.GridCoords);
            arrow = em.SpawnEntity("ArrowRegular", map.GridCoords);
            var hands = em.System<SharedHandsSystem>();
            var handComp = em.GetComponent<Content.Shared.Hands.Components.HandsComponent>(user);
            Assert.That(hands.TryPickup(user, bow, handComp.SortedHands[0]), Is.True);
            Assert.That(hands.TryPickup(user, arrow, handComp.SortedHands[1]), Is.True);
            hands.TrySetActiveHand(user, handComp.SortedHands[0]);
            Assert.That(em.System<Content.Shared.Interaction.SharedInteractionSystem>().UseInHandInteraction(user, bow), Is.True);
        });
        await pair.RunTicksSync(120);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<CrossbowComponent>(bow).Spanned, Is.True, "cranked");
            var hands = em.System<SharedHandsSystem>();
            var handComp = em.GetComponent<Content.Shared.Hands.Components.HandsComponent>(user);
            hands.TrySetActiveHand(user, handComp.SortedHands[1]);
            em.System<Content.Shared.Interaction.SharedInteractionSystem>().InteractUsing(user, arrow, bow, em.GetComponent<TransformComponent>(bow).Coordinates);
        });
        await pair.RunTicksSync(60);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.System<ItemSlotsSystem>().GetItemOrNull(bow, "projectiles"), Is.EqualTo(arrow), "clicking the cranked crossbow with an arrow loads it");
            Assert.That(em.GetComponent<ItemComponent>(bow).HeldPrefix, Is.EqualTo("loaded"));

            // musket balls go in by clicking too (they are cartridges)
            var gun = em.SpawnEntity("RPMedievalArquebus", map.GridCoords);
            musketGun = gun;
            var ball = em.SpawnEntity("RPMedievalMusketBall", map.GridCoords);
            var hands = em.System<SharedHandsSystem>();
            hands.TryDrop(user, bow);
            Assert.That(hands.TryPickupAnyHand(user, ball), Is.True);
            em.System<Content.Shared.Interaction.SharedInteractionSystem>().InteractUsing(user, ball, gun, em.GetComponent<TransformComponent>(gun).Coordinates);
        });
        await pair.RunTicksSync(30);
        await pair.Server.WaitAssertion(() =>
        {
            var musket = em.GetComponent<BallisticAmmoProviderComponent>(musketGun);
            Assert.That(musket.AmmoCount, Is.EqualTo(1), "a musket ball loads by clicking the arquebus with it");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CrossbowCranksOnTheGround()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid bow = default;
        await pair.Server.WaitAssertion(() =>
        {
            var user = em.SpawnEntity("MobHuman", map.GridCoords);
            bow = em.SpawnEntity("RPMedievalCrossbow", map.GridCoords);   // lying on the floor, not held
            var ev = new Content.Shared.Interaction.ActivateInWorldEvent(user, bow, true);
            em.EventBus.RaiseLocalEvent(bow, ev);
            Assert.That(ev.Handled, Is.True, "E on the crossbow on the ground starts cranking");
        });
        await pair.RunTicksSync(120);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<CrossbowComponent>(bow).Spanned, Is.True, "cranked where it lies");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BalearicSlingFiresBulletsInOneVolley()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid user = default, fusti = default, rock = default;
        await pair.Server.WaitAssertion(() =>
        {
            var storage = em.System<Content.Shared.Storage.EntitySystems.SharedStorageSystem>();
            user = em.SpawnEntity("MobHuman", map.GridCoords);
            fusti = em.SpawnEntity("RPMedievalBalearicSling", map.GridCoords);
            Assert.That(em.System<SharedHandsSystem>().TryPickup(user, fusti), Is.True);
            rock = em.SpawnEntity("N14MaterialBrick1", map.GridCoords);
            var ball1 = em.SpawnEntity("RPMedievalMusketBall", map.GridCoords);
            var ball2 = em.SpawnEntity("RPMedievalMusketBall", map.GridCoords);
            foreach (var ent in new[] { rock, ball1, ball2 })
                Assert.That(storage.Insert(fusti, ent, out _, playSound: false, stackAutomatically: false), Is.True, "the Balearic sling takes bullets and junk");
        });
        await pair.RunTicksSync(90);
        await pair.Server.WaitAssertion(() =>
        {
            var before = em.Count<Content.Shared.Projectiles.ProjectileComponent>();
            em.System<GunSystem>().AttemptShoot(user, fusti, em.GetComponent<GunComponent>(fusti), map.GridCoords.Offset(new Vector2(5, 0)));
            var pouch = em.System<Robust.Shared.Containers.SharedContainerSystem>().GetContainer(fusti, "storagebase");
            Assert.That(pouch.ContainedEntities.Count, Is.EqualTo(0), "one swing empties the pouch");
            Assert.That(em.Count<Content.Shared.Projectiles.ProjectileComponent>() - before, Is.GreaterThanOrEqualTo(2), "both musket balls fired as real shots");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BalearicSlingFiresShellsAndAmmoBoxes()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid user = default, sling = default;
        await pair.Server.WaitAssertion(() =>
        {
            var storage = em.System<Content.Shared.Storage.EntitySystems.SharedStorageSystem>();
            user = em.SpawnEntity("MobHuman", map.GridCoords);
            sling = em.SpawnEntity("RPMedievalBalearicSling", map.GridCoords);
            Assert.That(em.System<SharedHandsSystem>().TryPickup(user, sling), Is.True);
            foreach (var id in new[] { "N14ShellShotgun12", "N14ShellShotgun12", "N14ShellShotgun12", "N14ShellShotgun12", "N14ShellShotgun12", "N14ShellShotgun12", "N14ShellShotgun12", "N14ShellShotgun12", "PackAmmoBox12Gauge" })
                Assert.That(storage.Insert(sling, em.SpawnEntity(id, map.GridCoords), out _, playSound: false, stackAutomatically: false), Is.True, id);
        });
        await pair.RunTicksSync(90);
        await pair.Server.WaitAssertion(() =>
        {
            var before = em.Count<Content.Shared.Projectiles.ProjectileComponent>();
            em.System<GunSystem>().AttemptShoot(user, sling, em.GetComponent<GunComponent>(sling), map.GridCoords.Offset(new Vector2(5, 0)));
            var fired = em.Count<Content.Shared.Projectiles.ProjectileComponent>() - before;
            TestContext.Out.WriteLine($"projectiles fired: {fired}");
            Assert.That(fired, Is.GreaterThanOrEqualTo(30), "loose shells and the shells in the box all go off as gunshots");
            Assert.That(em.EntityQuery<Content.Shared.Weapons.Ranged.Components.CartridgeAmmoComponent>().Count(c => !c.Spent), Is.EqualTo(0), "no live shells left anywhere");
            var appearance = em.System<SharedAppearanceSystem>();
            var q = em.EntityQueryEnumerator<Content.Shared.Weapons.Ranged.Components.CartridgeAmmoComponent>();
            while (q.MoveNext(out var shell, out _))
            {
                Assert.That(appearance.TryGetData<bool>(shell, Content.Shared.Weapons.Ranged.Systems.AmmoVisuals.Spent, out var spentLook) && spentLook, Is.True, "every casing on the floor looks spent");
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BalearicSlingTakesBigThings()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var storage = em.System<Content.Shared.Storage.EntitySystems.SharedStorageSystem>();
            var shepherd = em.SpawnEntity("RPMedievalSling", map.GridCoords);
            var balearic = em.SpawnEntity("RPMedievalBalearicSling", map.GridCoords);
            var big = em.SpawnEntity("RPMedievalGreatsword", map.GridCoords);
            Assert.That(storage.CanInsert(shepherd, big, out _), Is.False, "the shepherd's sling is too small for a greatsword");
            Assert.That(storage.CanInsert(balearic, big, out _), Is.True, "the Balearic sling takes Large things");
            Assert.That(em.GetComponent<SlingComponent>(balearic).FireCartridges, Is.True, "only the Balearic sling fires bullets");
            Assert.That(em.GetComponent<SlingComponent>(shepherd).FireCartridges, Is.False, "the shepherd's sling throws them");
            var fustibalus = em.SpawnEntity("RPMedievalFustibalus", map.GridCoords);
            Assert.That(em.GetComponent<SlingComponent>(fustibalus).FireCartridges, Is.False, "the fustibalus throws them");
        });
        await pair.CleanReturnAsync();
    }
}
