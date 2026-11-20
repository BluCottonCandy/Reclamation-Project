using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client.Weapons.Ranged.Components;
using Content.IntegrationTests.Pair;
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
using Content.Shared.Weapons.Ranged.Systems;
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
                    if (boxId == "AtomCatsKitDealMaker")
                        Assert.That(items, Does.Not.Contain("AtomCatsGarageStamp"));
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
    public async Task EngravedPistolDisplaysBothSlidePositionsWithItsMagazineInserted()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var map = await pair.CreateTestMap();
        var server = pair.Server.ResolveDependency<IEntityManager>();
        var client = pair.Client.ResolveDependency<IEntityManager>();
        var session = pair.Server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);
        EntityUid user = default, pistol = default;
        NetEntity netPistol = default;
        await pair.Server.WaitAssertion(() =>
        {
            user = server.SpawnEntity("MobHuman", map.GridCoords);
            pair.Server.PlayerMan.SetAttachedEntity(session, user);
            pistol = server.SpawnEntity("AtomCatsEngravedPistol", map.GridCoords);
            netPistol = server.GetNetEntity(pistol);
            Assert.That(server.System<ItemSlotsSystem>().GetItemOrNull(pistol, "gun_magazine"), Is.Not.Null);
        });
        foreach (var closed in new[] { true, false, true })
        {
            await pair.Server.WaitPost(() => server.System<SharedGunSystem>().SetBoltClosed(pistol,
                server.GetComponent<ChamberMagazineAmmoProviderComponent>(pistol), closed, user));
            await pair.RunTicksSync(15);
            await pair.Client.WaitAssertion(() =>
            {
                var sprite = client.GetComponent<Robust.Client.GameObjects.SpriteComponent>(client.GetEntity(netPistol));
                Assert.That(sprite.LayerMapTryGet(GunVisualLayers.Base, out var slide));
                var state = closed ? "base" : "bolt-open";
                Assert.That(sprite[slide].RsiState.Name, Is.EqualTo(state));
                Assert.That(sprite[slide].ActualRsi!.TryGetState(state, out _));
                Assert.That(sprite.LayerMapTryGet(GunVisualLayers.Mag, out _), Is.False);
            });
        }
        await pair.Server.WaitPost(() => pair.Server.PlayerMan.SetAttachedEntity(session, null));
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
            var transform = em.System<SharedTransformSystem>();
            var guns = em.System<SharedGunSystem>();
            // The tip in the spike artwork points along its local positive X axis.
            foreach (var direction in new[] { Vector2.UnitX, Vector2.UnitY, -Vector2.UnitX, -Vector2.UnitY })
            {
                guns.ShootProjectile(spike, direction, Vector2.Zero, weapon);
                var tip = transform.GetWorldRotation(spike).ToVec();
                Assert.That(Vector2.Dot(tip, direction), Is.EqualTo(1f).Within(0.0001f));
            }
        });
        await pair.CleanReturnAsync();
    }
}
