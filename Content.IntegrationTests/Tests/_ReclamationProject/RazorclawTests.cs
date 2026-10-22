using System;
using System.Linq;
using System.Numerics;
using Content.Server._ReclamationProject.Mobs;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Weapons.Reflect;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Melee;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture]
public sealed class RazorclawTests
{
    [TestCase(false, 100)]
    [TestCase(true, 120)]
    public async Task ChargeSweepsMultipleEnemiesWithoutHittingBehindOrDoubleHitting(bool enraged, int expected)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var boss = em.SpawnEntity("ReclamationMobRazorclaw", map.GridCoords);
            var front = em.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(2.5f, 0)));
            var second = em.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(2.5f, .6f)));
            var behind = em.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(-2f, 0)));
            var ally = em.SpawnEntity("ReclamationMobBabyDeathclaw", map.GridCoords.Offset(new Vector2(2, -.5f)));
            var reflection = em.GetComponent<ReflectComponent>(boss);
            Assert.That(reflection.Reflects, Is.EqualTo(ReflectType.None));
            Assert.That(reflection.ReflectProb, Is.Zero);
            Assert.That(reflection.ReflectProbByType.Values.All(probability => probability == 0), Is.True);
            var charge = em.GetComponent<MirelurkQueenChargeComponent>(boss);
            var damage = em.System<DamageableSystem>();
            var regeneration = em.GetComponent<PassiveDamageComponent>(boss);
            var interval = regeneration.Interval;
            damage.TryChangeDamage(boss, new DamageSpecifier { DamageDict = new() { ["Slash"] = 100 } });
            Assert.That(em.GetComponent<DamageableComponent>(boss).TotalDamage.Float(), Is.EqualTo(50), "Half damage passes the hide.");
            if (enraged)
            {
                damage.TryChangeDamage(boss, new DamageSpecifier { DamageDict = new() { ["Slash"] = 1900 } });
                Assert.That(charge.Enraged, Is.True);
                Assert.That(regeneration.Interval, Is.EqualTo(interval / 1.4f).Within(.0001f));
                Assert.That(em.GetComponent<MeleeWeaponComponent>(boss).Damage.GetTotal().Float(), Is.EqualTo(60));
                damage.TryChangeDamage(boss, new DamageSpecifier { DamageDict = new() { ["Slash"] = -100 } });
                damage.TryChangeDamage(boss, new DamageSpecifier { DamageDict = new() { ["Slash"] = 100 } });
                Assert.That(charge.ImpactDamage!.GetTotal().Float(), Is.EqualTo(120), "Enrage happens only once.");
            }
            charge.Dashing = true;
            charge.DashDirection = Vector2.UnitX;
            var thrown = em.EnsureComponent<ThrownItemComponent>(boss);
            em.EventBus.RaiseLocalEvent(boss, new ThrowDoHitEvent(boss, front, thrown, null), true);
            em.EventBus.RaiseLocalEvent(boss, new ThrowDoHitEvent(boss, second, thrown, null), true);
            foreach (var victim in new[] { front, second })
            {
                Assert.That(em.GetComponent<DamageableComponent>(victim).Damage.DamageDict["Slash"].Float(), Is.EqualTo(expected));
                Assert.That(em.GetComponent<DamageableComponent>(victim).TotalDamage.Float(), Is.EqualTo(expected));
                Assert.That(em.GetComponent<StaminaComponent>(victim).StaminaDamage, Is.Zero);
            }
            Assert.That(em.GetComponent<DamageableComponent>(behind).TotalDamage.Float(), Is.Zero);
            Assert.That(em.GetComponent<DamageableComponent>(ally).TotalDamage.Float(), Is.Zero);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ButcheringDeadRazorclawProducesFourBabiesHideAndSkull()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid corpse = default;
        await pair.Server.WaitAssertion(() =>
        {
            var user = em.SpawnEntity("MobHuman", map.GridCoords);
            var knife = em.SpawnEntity("KitchenKnife", map.GridCoords);
            Assert.That(em.System<SharedHandsSystem>().TryPickup(user, knife), Is.True);
            corpse = em.SpawnEntity("ReclamationMobRazorclaw", map.GridCoords);
            em.System<DamageableSystem>().TryChangeDamage(corpse,
                new DamageSpecifier { DamageDict = new() { ["Slash"] = 2100 } }, ignoreResistances: true, doPartDamage: false, canSever: false);
            Assert.That(em.EntityQuery<MetaDataComponent>().Count(m => m.EntityPrototype?.ID == "ReclamationMobBabyDeathclaw"), Is.Zero,
                "Babies appear on butchering, not merely on death.");
            var ev = new AfterInteractEvent(user, knife, corpse, map.GridCoords, true);
            em.EventBus.RaiseLocalEvent(knife, ev);
            Assert.That(ev.Handled, Is.True);
        });
        await pair.RunTicksSync(650);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.EntityExists(corpse), Is.False);
            var babies = em.EntityQuery<MetaDataComponent>().Where(m => m.EntityPrototype?.ID == "ReclamationMobBabyDeathclaw").ToArray();
            Assert.That(babies, Has.Length.EqualTo(4));
            foreach (var baby in babies)
                Assert.That(em.GetComponent<MeleeWeaponComponent>(baby.Owner).Damage.GetTotal().Float(), Is.EqualTo(75));
            foreach (var id in new[] { "ReclamationRazorclawHide", "ReclamationRazorclawSkull" })
                Assert.That(em.EntityQuery<MetaDataComponent>().Count(m => m.EntityPrototype?.ID == id), Is.EqualTo(1));
        });
        await pair.CleanReturnAsync();
    }
}
