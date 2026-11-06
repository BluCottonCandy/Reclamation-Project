using Content.Shared.DoAfter;
using Content.Shared.Medical;
using Content.Shared.Movement.Systems;
using System;
using System.Linq;
using Content.Server._ReclamationProject.Agility;
using Content.Shared._Misfits.Special;
using Content.Shared._Misfits.Special.Components;
using Content.Shared._Misfits.SpecialStats;
using Content.Shared._ReclamationProject.Agility;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.CombatMode;
using Content.Shared.Damage;
using Content.Shared.Weapons.Melee.Components;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Hands.Components;
using Content.Shared.Inventory;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;

using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;


namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture]
public sealed class AgilityFastHandsTests
{
    [Test]
    public async Task CarriedAmmoAndIndependentMeleeKeepTheirNormalRules()
    {
        await using var pair = await PoolManager.GetServerClient();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var map = em.System<SharedMapSystem>().CreateMap();
            void Check(bool ok, string message) => Assert.That(ok, Is.True, message);
            try
            {
                var coords = new EntityCoordinates(map, System.Numerics.Vector2.Zero);
                var user = em.SpawnEntity("MobHuman", coords);
                var special = em.EnsureComponent<SpecialComponent>(user);
                var melee = em.System<SharedMeleeWeaponSystem>();
                var agility = em.System<SpecialAgilitySystem>();
                var hands = em.System<SharedHandsSystem>();
                var inventory = em.System<InventorySystem>();
                var storage = em.System<SharedStorageSystem>();
                var slots = em.System<ItemSlotsSystem>();
                var guns = em.System<SharedGunSystem>();
                var fast = em.System<AgilityFastHandsSystem>();
                var handComp = em.GetComponent<HandsComponent>(user);
                var knife = em.SpawnEntity("CombatKnife", coords);
                var other = em.SpawnEntity("CombatKnife", coords);
                Check(hands.TryPickup(user, knife, handComp.SortedHands[0]), "active melee pickup");
                Check(hands.TryPickup(user, other, handComp.SortedHands[1]), "offhand melee pickup");
                hands.TrySetActiveHand(user, handComp.SortedHands[0]);
                var weapon = em.GetComponent<MeleeWeaponComponent>(knife);
                special.BaseAgility = 5;
                var baseDelay = melee.GetAttackRate(knife, user);
                for (var stat = 6; stat <= 10; stat++)
                {
                    special.BaseAgility = stat;
                    Check(Math.Abs(melee.GetAttackRate(knife, user) / baseDelay - 1f / (1 + (stat - 5) * .04f)) < .0001f,
                        "melee frequency scaling at " + stat);
                    Check(Math.Abs(agility.GetStaminaCostMultiplier(user) - (1 - (stat - 5) * .04f)) < .0001f,
                        "stamina scaling at " + stat);
                }
                Check(melee.TryGetOffhandMeleeWeapon(user, knife, out var offhand, out _) && offhand == other, "dual melee at 10");
                em.EnsureComponent<MeleeRequiresWieldComponent>(other);
                Check(!melee.TryGetOffhandMeleeWeapon(user, knife, out _, out _), "two-handed weapon excluded");
                em.RemoveComponent<MeleeRequiresWieldComponent>(other);
                var victim = em.SpawnEntity("MobHuman", coords);
                em.System<SharedCombatModeSystem>().SetInCombatMode(user, true);
                var victimHealth = em.GetComponent<DamageableComponent>(victim);
                // The fixture just picked these up in this tick; finish the normal pickup delay.
                weapon.NextAttack = TimeSpan.Zero;
                Check(melee.AttemptLightAttack(user, knife, weapon, victim), "active weapon normal attack succeeds");
                var firstDamage = victimHealth.TotalDamage;
                var otherWeapon = em.GetComponent<MeleeWeaponComponent>(other);
                otherWeapon.NextAttack = TimeSpan.Zero;
                Check(melee.AttemptLightAttack(user, other, otherWeapon, victim) && victimHealth.TotalDamage > firstDamage,
                    "offhand normal attack independently deals damage");
                Check(!melee.AttemptLightAttack(user, other, otherWeapon, victim), "offhand cooldown prevents extra attacks");
                var pairDamage = victimHealth.TotalDamage;
                void PairedDamage(string phase)
                {
                    var target = em.SpawnEntity("MobHuman", coords);
                    weapon.NextAttack = otherWeapon.NextAttack = TimeSpan.Zero;
                    weapon.Attacking = otherWeapon.Attacking = false;
                    var input = new LightAttackEvent(em.GetNetEntity(target), em.GetNetEntity(knife), em.GetNetCoordinates(coords));
                    Check(melee.AttemptPairedAttack(user, knife, weapon, input), "paired input accepted " + phase);
                    Check(em.GetComponent<DamageableComponent>(target).TotalDamage == pairDamage,
                        "both weapons deal full normal damage " + phase);
                    Check(!melee.AttemptPairedAttack(user, knife, weapon, input), "paired cooldown respected " + phase);
                }
                PairedDamage("before Fast Hands has ever been activated");
                var passiveAbility = em.EnsureComponent<AgilityFastHandsComponent>(user);
                var currentTime = IoCManager.Resolve<IGameTiming>().CurTime;
                passiveAbility.Reloading = true;
                passiveAbility.NextUseAt = currentTime + TimeSpan.FromMinutes(2);
                PairedDamage("during Fast Hands");
                passiveAbility.Reloading = false;
                PairedDamage("after Fast Hands, while its action is on cooldown");
                passiveAbility.NextUseAt = TimeSpan.Zero;
                var wideTarget = em.SpawnEntity("MobHuman", new EntityCoordinates(map, new System.Numerics.Vector2(.8f, 0)));
                weapon.NextAttack = otherWeapon.NextAttack = TimeSpan.Zero;
                weapon.Attacking = otherWeapon.Attacking = false;
                var stamina = em.GetComponent<Content.Shared.Damage.Components.StaminaComponent>(user);
                var staminaBefore = stamina.StaminaDamage;
                var wideInput = new HeavyAttackEvent(em.GetNetEntity(knife), new() { em.GetNetEntity(wideTarget) },
                    em.GetNetCoordinates(em.GetComponent<TransformComponent>(wideTarget).Coordinates));
                Check(melee.AttemptPairedAttack(user, knife, weapon, wideInput), "paired wide swing accepted without Fast Hands");
                Check(em.GetComponent<DamageableComponent>(wideTarget).TotalDamage > melee.GetDamage(knife, user).GetTotal(),
                    "both wide-swing weapons deal damage");
                Check(Math.Abs(stamina.StaminaDamage - staminaBefore -
                        (weapon.HeavyStaminaCost + otherWeapon.HeavyStaminaCost) * .8f) < .001f,
                    "wide swing charges each weapon's reduced stamina cost");
                var normalWideCost = stamina.StaminaDamage - staminaBefore;
                for (var heldSwing = 1; heldSwing <= 4; heldSwing++)
                {
                    // The button remains held, but each weapon's attack cooldown has elapsed.
                    weapon.NextAttack = otherWeapon.NextAttack = TimeSpan.Zero;
                    weapon.Attacking = otherWeapon.Attacking = true;
                    var target = em.SpawnEntity("MobHuman", new EntityCoordinates(map, new System.Numerics.Vector2(.8f, 0)));
                    var heldInput = new HeavyAttackEvent(em.GetNetEntity(knife), new() { em.GetNetEntity(target) }, wideInput.Coordinates);
                    var before = stamina.StaminaDamage;
                    Check(melee.AttemptPairedAttack(user, knife, weapon, heldInput), "held wide swing accepted " + heldSwing);
                    Check(em.GetComponent<DamageableComponent>(target).TotalDamage > melee.GetDamage(knife, user).GetTotal(),
                        "held wide swing damages with both weapons " + heldSwing);
                    Check(stamina.StaminaDamage > before, "held wide swing pays stamina " + heldSwing);
                }
                var recoveredStamina = stamina.StaminaDamage;
                stamina.StaminaDamage = stamina.CritThreshold - weapon.HeavyStaminaCost * .8f;
                weapon.NextAttack = otherWeapon.NextAttack = TimeSpan.Zero;
                var damageBeforeExhaustedSwing = em.GetComponent<DamageableComponent>(wideTarget).TotalDamage;
                Check(!melee.CanPayHeavyStamina(user, weapon) && !melee.AttemptPairedAttack(user, knife, weapon, wideInput),
                    "exhausted held swings are rejected before attacking");
                var deniedPrimary = weapon.NextAttack;
                var deniedOffhand = otherWeapon.NextAttack;
                Check(deniedPrimary > currentTime && deniedOffhand > currentTime &&
                        em.GetComponent<DamageableComponent>(wideTarget).TotalDamage == damageBeforeExhaustedSwing,
                    "exhausted swings replicate normal cooldowns without phantom damage");
                for (var retry = 0; retry < 100; retry++)
                    Check(!melee.AttemptPairedAttack(user, knife, weapon, wideInput), "exhausted retry does not queue a strike");
                Check(weapon.NextAttack == deniedPrimary && otherWeapon.NextAttack == deniedOffhand,
                    "retries during cooldown do not accumulate or extend attacks");
                stamina.StaminaDamage = recoveredStamina;
                Check(!melee.AttemptPairedAttack(user, knife, weapon, wideInput),
                    "stamina recovery does not bypass the rejected swing cooldown");
                weapon.NextAttack = otherWeapon.NextAttack = TimeSpan.Zero;
                var recoveredTarget = em.SpawnEntity("MobHuman", new EntityCoordinates(map, new System.Numerics.Vector2(.8f, 0)));
                var recoveredInput = new HeavyAttackEvent(em.GetNetEntity(knife), new() { em.GetNetEntity(recoveredTarget) }, wideInput.Coordinates);
                var staminaBeforeRecovery = stamina.StaminaDamage;
                Check(melee.AttemptPairedAttack(user, knife, weapon, recoveredInput), "held wide swing resumes when stamina returns");
                var recoveredDamage = em.GetComponent<DamageableComponent>(recoveredTarget).TotalDamage;
                // Body-part damage can vary. Paid stamina counts strikes deterministically.
                Check(recoveredDamage > 0 && Math.Abs(stamina.StaminaDamage - staminaBeforeRecovery - normalWideCost) < .001f &&
                        weapon.NextAttack > currentTime && otherWeapon.NextAttack > currentTime,
                    "recovery produces exactly one paid strike per hand, not a backlog");
                special.BaseAgility = 9;
                Check(!melee.TryGetOffhandMeleeWeapon(user, knife, out _, out _), "no dual melee below 10");
                special.BaseAgility = 10;
                var ready = new SpecialChangedEvent(user);
                em.EventBus.RaiseLocalEvent(user, ref ready, true);
                var ability = em.GetComponent<AgilityFastHandsComponent>(user);
                Check(ability.Action != null, "Fast Hands action granted");
                hands.TryDrop(user, knife);
                hands.TryDrop(user, other);
                var gun = em.SpawnEntity("N14WeaponPistol9mm", coords);
                Check(hands.TryPickup(user, gun, handComp.SortedHands[0]), "held gun pickup");
                var backpack = em.SpawnEntity("ClothingBackpack", coords);
                Check(inventory.TryEquip(user, backpack, "back", silent: true, force: true), "backpack equipped");
                var loaded = guns.GetMagazineEntity(gun) ?? throw new Exception("No loaded magazine");
                void SetCount(EntityUid item, int count)
                {
                    var ammo = em.GetComponent<BallisticAmmoProviderComponent>(item);
                    ammo.UnspawnedCount = count;
                    ammo.SpawnedCountPredict = 0;
                }
                var spare = em.SpawnEntity("N14MagazinePistol9mm", coords);
                var pocket = em.SpawnEntity("N14MagazinePistol9mm", coords);
                SetCount(loaded, 0); SetCount(spare, 3); SetCount(pocket, 0);
                Check(storage.Insert(backpack, spare, out _, user: user), "spare magazine in backpack");
                Check(inventory.TryEquip(user, pocket, "pocket1", silent: true, force: true), "magazine in pocket");
                var box = em.SpawnEntity("MagazineBox9mm", coords);
                SetCount(box, 5);
                Check(storage.Insert(backpack, box, out _, user: user), "ammo box in backpack");
                var wrong = em.SpawnEntity("MagazineBox10mm", coords);
                Check(storage.Insert(backpack, wrong, out _, user: user), "incompatible ammo carried");
                int Count(EntityUid uid) => em.GetComponent<BallisticAmmoProviderComponent>(uid).AmmoCount;
                var wrongBefore = Count(wrong);
                var storedGun = em.SpawnEntity("N14WeaponPistol9mm", coords);
                var storedMagazine = guns.GetMagazineEntity(storedGun) ?? throw new Exception("Missing stored gun magazine");
                SetCount(storedMagazine, 0);
                Check(storage.Insert(backpack, storedGun, out _, user: user), "another gun carried in backpack");
                var activate = new AgilityFastHandsActionEvent { Performer = user };
                em.EventBus.RaiseLocalEvent(user, ref activate);
                var now = IoCManager.Resolve<IGameTiming>().CurTime;
                Check(activate.Handled && ability.NextUseAt == now + TimeSpan.FromMinutes(2) && !ability.Reloading,
                    "bulk refill finishes instantly with a two-minute cooldown");
                Check(guns.GetMagazineEntity(gun) == spare && Count(spare) == 3,
                    "one swap happens before refill and the magazine now in the gun remains unfilled");
                Check(Count(loaded) + Count(pocket) == 5 && Count(box) == 0,
                    "ejected magazine and carried spares refill using available ammunition");
                Check(Count(storedMagazine) == 0 && Count(wrong) == wrongBefore,
                    "magazines inside stored guns and incompatible ammo remain untouched");
                Check(Count(spare) + Count(box) + Count(loaded) + Count(pocket) == 8,
                    "bulk refill conserves ammunition without draining the loaded magazine");
                Check(fast.GetCarriedItems(user).Contains(loaded), "ejected magazine remains carried after stowing");
                var repeat = new AgilityFastHandsActionEvent { Performer = user };
                em.EventBus.RaiseLocalEvent(user, ref repeat);
                Check(!repeat.Handled && guns.GetMagazineEntity(gun) == spare,
                    "cooldown prevents repeated swaps");
                fast.Update(0);
                Check(guns.GetMagazineEntity(gun) == spare && Count(spare) == 3,
                    "bulk refill does not keep swapping or refilling a magazine in the gun");
                // Sufficient ammo fills every carried spare, but never a loaded magazine.
                SetCount(box, 50);
                ability.NextUseAt = TimeSpan.Zero;
                var secondBulk = new AgilityFastHandsActionEvent { Performer = user };
                em.EventBus.RaiseLocalEvent(user, ref secondBulk);
                var nowLoaded = guns.GetMagazineEntity(gun) ?? throw new Exception("No loaded magazine after second test");
                Check(secondBulk.Handled && fast.GetCarriedItems(user).Where(item =>
                        em.HasComponent<BallisticAmmoProviderComponent>(item) &&
                        (item == loaded || item == spare || item == pocket) && item != nowLoaded)
                    .All(item => Count(item) == em.GetComponent<BallisticAmmoProviderComponent>(item).Capacity),
                    "all carried spare magazines refill in one activation when ammo is available");
                Check(Count(storedMagazine) == 0, "loaded magazines in other guns stay excluded on every activation");

                FastHandsReloadDoAfterEvent FinishRound()
                {
                    var id = ability.DoAfter ?? throw new Exception("Missing loading progress bar");
                    var bar = em.GetComponent<DoAfterComponent>(user).DoAfters[id.Index];
                    var source = bar.Args.Used ?? throw new Exception("No ammo source");
                    Check(bar.Args.Delay == guns.GetFastHandsReloadDelay(source) && bar.Args.Delay > TimeSpan.Zero,
                        "internal loading progress uses half normal ammo-loading delay");
                    var completed = new FastHandsReloadDoAfterEvent { DoAfter = bar };
                    em.EventBus.RaiseLocalEvent(user, ref completed);
                    return completed;
                }
                // Internal magazines use the same timed one-round insertion, without a swap.
                hands.TryDrop(user, gun);
                var rifle = em.SpawnEntity("N14WeaponSniperHunting", coords);
                Check(hands.TryPickup(user, rifle, handComp.SortedHands[0]), "internal-magazine rifle held");
                SetCount(rifle, 0);
                var rifleAmmo = em.SpawnEntity("MagazineBox308", coords);
                SetCount(rifleAmmo, 3);
                Check(storage.Insert(backpack, rifleAmmo, out _, user: user), "rifle ammunition carried");
                ability.NextUseAt = TimeSpan.Zero;
                var rifleReload = new AgilityFastHandsActionEvent { Performer = user };
                em.EventBus.RaiseLocalEvent(user, ref rifleReload);
                Check(rifleReload.Handled && ability.ReloadTarget == rifle && Count(rifle) == 0,
                    "internal gun reload begins without instant ammunition");
                for (var round = 0; round < 3; round++)
                    FinishRound();
                Check(!ability.Reloading && Count(rifle) == 3 && Count(rifleAmmo) == 0 && guns.GetMagazineEntity(rifle) == null,
                    "internal gun loads real rounds at double speed without magazine swapping");
                ability.NextUseAt = TimeSpan.Zero;
                SetCount(rifleAmmo, 20);
                var boundedReload = new AgilityFastHandsActionEvent { Performer = user };
                em.EventBus.RaiseLocalEvent(user, ref boundedReload);
                var budget = ability.RoundsRemaining;
                Check(boundedReload.Handled && budget == guns.GetFastHandsAmmo(rifle).Capacity - Count(rifle),
                    "internal reload is bounded by one initial magazine deficit");
                for (var round = 0; round < budget; round++)
                {
                    FinishRound();
                    var fired = guns.DoTakeAmmo(1, rifle, user);
                    foreach (var cartridge in fired)
                        if (cartridge.Item1 is { } firedRound)
                            em.QueueDeleteEntity(firedRound);
                }
                Check(!ability.Reloading && Count(rifleAmmo) == 20 - budget,
                    "firing while reloading cannot extend one use into unlimited loading");
                hands.TryDrop(user, rifle);
                var revolver = em.SpawnEntity("WeaponRevolverInspector", coords);
                Check(hands.TryPickup(user, revolver, handComp.SortedHands[0]), "revolver held");
                var cylinder = em.GetComponent<RevolverAmmoProviderComponent>(revolver);
                cylinder.Chambers = Enumerable.Repeat<bool?>(false, cylinder.Capacity).ToArray();
                cylinder.AmmoSlots = Enumerable.Repeat<EntityUid?>(null, cylinder.Capacity).ToList();
                var revolverAmmo = em.SpawnEntity("MagazineBoxMagnum", coords);
                SetCount(revolverAmmo, 3);
                Check(hands.TryPickup(user, revolverAmmo, handComp.SortedHands[1]), "revolver spare ammo carried");
                ability.NextUseAt = TimeSpan.Zero;
                var cylinderReload = new AgilityFastHandsActionEvent { Performer = user };
                em.EventBus.RaiseLocalEvent(user, ref cylinderReload);
                Check(cylinderReload.Handled && ability.ReloadTarget == revolver,
                    "spent revolver cylinder is a valid internal reload target");
                for (var round = 0; round < 3; round++)
                    FinishRound();
                Check(!ability.Reloading && guns.GetFastHandsAmmo(revolver).Count == 3 && Count(revolverAmmo) == 0,
                    "revolver clears spent cases and reloads real ammunition one round at a time");
                special.BaseAgility = 9; em.EventBus.RaiseLocalEvent(user, ref ready, true);
                special.BaseAgility = 10; em.EventBus.RaiseLocalEvent(user, ref ready, true);
                Check(ability.NextUseAt == now + TimeSpan.FromMinutes(2), "stat changes do not reset cooldown");
            }
            finally
            {
                em.DeleteEntity(map);
            }
        });
        await pair.CleanReturnAsync();
    }
    [Test]
    public async Task TimedActionCurveRemainsUnchanged()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        var timing = pair.Server.ResolveDependency<IGameTiming>();
        await pair.Server.WaitAssertion(() =>
        {
            var user = em.SpawnEntity("MobHuman", map.GridCoords);
            em.EnsureComponent<SpecialComponent>(user);
            try
            {
                var special = em.System<SharedSpecialSystem>();
                var speeds = em.System<SpecialDoAfterSystem>();
                var delta = new[] { -5f, -3.5f, -2.25f, -1f, 0f, 1f, 2.25f, 3.75f, 5.5f, 7.5f };
                for (var level = 1; level <= 10; level++)
                {
                    special.TrySetBase(user, SpecialStat.Agility, level);
                    Assert.That(speeds.GetSpeed(user), Is.EqualTo(1 + delta[level - 1] * 0.01f).Within(0.0001));
                    var movement = new RefreshMovementSpeedModifiersEvent();
                    em.EventBus.RaiseLocalEvent(user, movement);
                    Assert.That(movement.WalkSpeedModifier, Is.EqualTo(1f).Within(0.0001));
                    Assert.That(movement.SprintSpeedModifier, Is.EqualTo(1f).Within(0.0001));
                }
                var input = new DoAfterArgs(em, user, 10f, new HealingDoAfterEvent(), user) { NeedHand = false };
                Assert.That(em.System<SharedDoAfterSystem>().TryStartDoAfter(input, out var id), Is.True);
                var actual = em.GetComponent<DoAfterComponent>(user).DoAfters[id!.Value.Index];
                Assert.That(actual.Args.Delay.TotalSeconds, Is.EqualTo(10 / 1.075).Within(0.0001));
                Assert.That(input.Delay, Is.EqualTo(TimeSpan.FromSeconds(10)));
            }
            finally
            {
                em.DeleteEntity(user);
            }
        });
        await pair.CleanReturnAsync();
    }
}
