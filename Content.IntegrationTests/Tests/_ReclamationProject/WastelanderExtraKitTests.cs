using System.Collections.Generic;
using System.Linq;
using Content.Server.Storage.Components;
using Content.Server.UndecidedLoadout.Components;
using Content.Shared.Armor;
using Content.Shared.Storage;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Wieldable.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture, NonParallelizable]
public sealed class WastelanderExtraKitTests
{
    [Test]
    public async Task ExtraKitsSupplyMatchingAmmoAndExactlyOneOrdinaryTwoHandedWeapon()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var selector = em.SpawnEntity("ReclamationWastelanderKitSelector", map.GridCoords);
            Assert.That(em.GetComponent<UndecidedLoadoutBackpackComponent>(selector).PossibleSets, Has.Count.EqualTo(8));
            var doctor = em.SpawnEntity("ReclamationWastelanderKitBackAlleyDoctor", map.GridCoords);
            var supplies = EntitySpawnCollection.GetSpawns(em.GetComponent<SpawnItemsOnUseComponent>(doctor).Items);
            Assert.That(supplies.Count(id => id == "N14Stimpak"), Is.EqualTo(2));
            Assert.That(supplies, Has.Count.EqualTo(9));
            var revolver = em.SpawnEntity("N14WeaponRevolver9mm", map.GridCoords);
            var loader = em.SpawnEntity("SpeedLoader9", map.GridCoords);
            var ammo = em.SpawnEntity("MagazineBox9mm", map.GridCoords);
            var round = em.GetComponent<RevolverAmmoProviderComponent>(revolver).FillPrototype;
            Assert.That(em.GetComponent<BallisticAmmoProviderComponent>(loader).Proto, Is.EqualTo(round));
            Assert.That(em.GetComponent<BallisticAmmoProviderComponent>(ammo).Proto, Is.EqualTo(round));

            var allowed = new[] { "N14BaseBallBat", "N14SledgeHammer", "N14FireAxe", "N14GolfClub", "N14WastelandSpear" };
            foreach (var id in allowed)
            {
                var weapon = em.SpawnEntity(id, map.GridCoords);
                Assert.That(em.HasComponent<WieldableComponent>(weapon), Is.True, id);
            }
            var bruiser = em.SpawnEntity("ReclamationWastelanderKitBruiser", map.GridCoords);
            var random = new RobustRandom();
            random.SetSeed(42);
            var seen = new HashSet<string>();
            for (var roll = 0; roll < 100; roll++)
            {
                var contents = EntitySpawnCollection.GetSpawns(em.GetComponent<SpawnItemsOnUseComponent>(bruiser).Items, random);
                Assert.That(contents, Has.Count.EqualTo(3));
                Assert.That(contents, Does.Contain("N14Psycho"));
                Assert.That(contents, Does.Contain("N14BuffoutPill"));
                var weapon = contents.Except(new[] { "N14Psycho", "N14BuffoutPill" }).Single();
                Assert.That(allowed, Does.Contain(weapon));
                seen.Add(weapon);
            }
            Assert.That(seen, Is.EquivalentTo(allowed));
            var roller = em.SpawnEntity("ReclamationWastelanderKitHighRoller", map.GridCoords);
            var rollerItems = EntitySpawnCollection.GetSpawns(em.GetComponent<SpawnItemsOnUseComponent>(roller).Items);
            Assert.That(rollerItems, Is.EquivalentTo(new[] { "ReclamationWeaponPistol45ColtPristine", "MagazineBox45", "ReclamationClothingUniformHighRoller", "ClothingHeadHatFedoraGrey" }));
            foreach (var id in supplies.Concat(rollerItems).Distinct())
                Assert.That(prototypes.HasIndex<EntityPrototype>(id), Is.True, id);
            var suit = em.SpawnEntity("ReclamationClothingUniformHighRoller", map.GridCoords);
            var armor = em.GetComponent<ArmorComponent>(suit);
            foreach (var type in new[] { "Blunt", "Slash", "Piercing" })
                Assert.That((float) armor.Modifiers.Coefficients[type], Is.EqualTo(0.75f));
            var colt = em.SpawnEntity("ReclamationWeaponPistol45ColtPristine", map.GridCoords);
            Assert.That(em.HasComponent<ChamberMagazineAmmoProviderComponent>(colt), Is.True);
        });
        await pair.CleanReturnAsync();
    }
}
