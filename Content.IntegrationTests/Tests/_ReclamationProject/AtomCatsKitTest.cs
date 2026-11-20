using System.Collections.Generic;
using System.Linq;
using Content.Server.Storage.Components;
using Content.Server.UndecidedLoadout.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Interaction.Events;
using Content.Shared.Projectiles;
using Content.Shared.Roles;
using Content.Shared.Storage;
using Content.Shared.UndecidedLoadout;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture]
public sealed class AtomCatsKitTest
{
    [Test]
    public async Task AllTwentyFourKitsRedeemAndSupplyBothGuns()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        var protos = pair.Server.ResolveDependency<IPrototypeManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var user = em.SpawnEntity("MobHuman", map.GridCoords);
            var slots = em.System<ItemSlotsSystem>();
            var checkedKits = new HashSet<string>();
            var boxedRounds = new Dictionary<string, string>
            {
                ["N14CartridgePistol9"] = "MagazineBox9mm", ["N14CartridgePistol10"] = "MagazineBox10mm",
                ["N14CartridgePistol22"] = "MagazineBox22", ["N14CartridgePistol45"] = "MagazineBox45",
                ["N14Cartridge44Magnum"] = "MagazineBox44", ["N14Cartridge45-70"] = "MagazineBox45-70",
                ["N14Cartridge762Rifle"] = "MagazineBox762", ["N14ShellShotgun12"] = "MagazineBox12gauge",
                ["AtomCatsRailroadSpike"] = "AtomCatsSpikeBox",
            };
            foreach (var role in protos.Index<DepartmentPrototype>("AtomCats").Roles)
            {
                for (var i = 0; i < 3; i++)
                {
                    var selector = em.SpawnEntity(role.Id + "KitSelector", map.GridCoords);
                    var options = em.GetComponent<UndecidedLoadoutBackpackComponent>(selector);
                    var set = protos.Index(options.PossibleSets[i]);
                    var boxId = set.Content.Single().Id;
                    Assert.That(checkedKits.Add(boxId), Is.True, boxId);
                    em.EventBus.RaiseLocalEvent(selector, new UndecidedLoadoutBackpackChangeSetMessage(i));
                    em.EventBus.RaiseLocalEvent(selector, new UndecidedLoadoutBackpackApproveMessage());
                    Assert.That(em.HasComponent<UndecidedLoadoutBackpackComponent>(selector), Is.False);
                    var box = em.EntityQuery<MetaDataComponent>().Single(m => m.EntityPrototype?.ID == boxId).Owner;
                    var items = EntitySpawnCollection.GetSpawns(em.GetComponent<SpawnItemsOnUseComponent>(box).Items);
                    Assert.That(items.Count(id => id == "N14BoxCardboardMREBoxCFilled"), Is.EqualTo(1), boxId);
                    var extraGauze = boxId is "AtomCatsKitRunner" or "AtomCatsKitFlashMedic" or "AtomCatsKitRoadsideMedic";
                    Assert.That(items.Count(id => id == "N14CleanGauze10"), Is.EqualTo(extraGauze ? 2 : 1), boxId);
                    var guns = items.Where(id => protos.Index<EntityPrototype>(id).Components.ContainsKey("Gun")).ToArray();
                    Assert.That(guns.Length, Is.EqualTo(2), boxId);
                    foreach (var id in guns)
                    {
                        var gun = em.SpawnEntity(id, map.GridCoords);
                        string reload;
                        string cartridge;
                        if (slots.GetItemOrNull(gun, "gun_magazine") is {} magazine)
                        {
                            reload = em.GetComponent<MetaDataComponent>(magazine).EntityPrototype!.ID;
                            cartridge = em.GetComponent<BallisticAmmoProviderComponent>(magazine).Proto!.Value.Id;
                        }
                        else
                        {
                            cartridge = em.TryGetComponent<RevolverAmmoProviderComponent>(gun, out var cylinder)
                                ? cylinder.FillPrototype!.Value.Id
                                : em.GetComponent<BallisticAmmoProviderComponent>(gun).Proto!.Value.Id;
                            reload = id switch
                            {
                                "N14WeaponRevolver9mm" => "SpeedLoader9",
                                "N14WeaponRevolver10mm" => "SpeedLoader10",
                                "N14WeaponRevolver44Magnum" => "SpeedLoader44",
                                "N14WeaponRevolver45-70Handcannon" => "SpeedLoader45-70",
                                "N14WeaponSniper44LeverCarbine" => "AtomCatsCarbinePack",
                                "N14WeaponShotgun" => "AtomCatsShellPack",
                                "AtomCatsRailroadRifle" => "AtomCatsSpikePack",
                                _ => throw new AssertionException("Unknown reload for " + id)
                            };
                        }
                        Assert.That(items.Count(item => item == reload), Is.GreaterThanOrEqualTo(2), boxId + " " + id);
                        Assert.That(items, Does.Contain(boxedRounds[cartridge]), boxId);
                        var spare = em.SpawnEntity(reload, map.GridCoords);
                        var count = new GetAmmoCountEvent();
                        em.EventBus.RaiseLocalEvent(spare, ref count);
                        Assert.That(count.Count, Is.EqualTo(count.Capacity).And.Positive, reload);
                        var ammo = new TakeAmmoEvent(1, new(), map.GridCoords);
                        em.EventBus.RaiseLocalEvent(spare, ammo);
                        Assert.That(ammo.Ammo, Has.Count.EqualTo(1), reload);
                        var round = em.GetComponent<MetaDataComponent>(ammo.Ammo.Single().Entity!.Value);
                        Assert.That(round.EntityPrototype!.ID, Is.EqualTo(cartridge), boxId);
                        var reserve = em.SpawnEntity(boxedRounds[cartridge], map.GridCoords);
                        Assert.That(em.GetComponent<BallisticAmmoProviderComponent>(reserve).Proto?.Id, Is.EqualTo(cartridge), boxId);
                        em.DeleteEntity(reserve);
                        em.DeleteEntity(spare);
                        em.DeleteEntity(gun);
                    }
                    em.EventBus.RaiseLocalEvent(box, new UseInHandEvent(user));
                    Assert.That(em.HasComponent<SpawnItemsOnUseComponent>(box), Is.False, boxId);
                }
            }
            Assert.That(checkedKits.Count, Is.EqualTo(24));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RailroadRifleFiresTheConfiguredSpike()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var weapon = em.SpawnEntity("AtomCatsRailroadRifle", map.GridCoords);
            Assert.That(em.GetComponent<GunComponent>(weapon).FireRate, Is.EqualTo(0.5f));
            var ammo = new TakeAmmoEvent(1, new(), map.GridCoords);
            em.EventBus.RaiseLocalEvent(weapon, ammo);
            var round = em.GetComponent<CartridgeAmmoComponent>(ammo.Ammo.Single().Entity!.Value);
            var spike = em.SpawnEntity(round.Prototype, map.GridCoords);
            var damage = em.GetComponent<ProjectileComponent>(spike).Damage;
            Assert.That((float) damage.DamageDict["Piercing"], Is.EqualTo(45));
            Assert.That((float) damage.DamageDict["Blunt"], Is.EqualTo(5));
            Assert.That((float) damage.GetTotal(), Is.EqualTo(50));
        });
        await pair.CleanReturnAsync();
    }
}
