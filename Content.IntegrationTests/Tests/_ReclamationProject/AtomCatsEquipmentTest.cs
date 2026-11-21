using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server.Administration.Managers;
using Content.Server.Database;
using Content.Server.GameTicking.Events;
using Content.Server.Maps;
using Content.Server.Players.JobWhitelist;
using Content.Server.Spawners.Components;
using Content.Server.Station.Components;
using Content.Server.Station.Events;
using Content.Server.Station.Systems;
using Content.Server.UndecidedLoadout.Components;
using Content.Shared.Foldable;
using Content.Shared._Misfits.PowerArmor;
using Content.Shared._ReclamationProject.PowerArmor;
using Content.Shared.Access.Components;
using Content.Shared.CCVar;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Eye.Blinding.Components;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Tools.Components;
using Content.Shared.Hands;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Item;
using Content.Shared.MagicMirror;
using Content.Shared.NPC.Components;
using Content.Shared.Preferences;
using Content.Shared.Radio.Components;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Content.Shared.Storage;
using Content.Shared.Toggleable;
using Content.Shared.Weapons.Melee;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture]
public sealed class AtomCatsEquipmentTest
{
    [Test]
    public async Task AllEightJobsReceiveTheirRankGearAndKits()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        var protos = pair.Server.ResolveDependency<IPrototypeManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var department = protos.Index<DepartmentPrototype>("AtomCats");
            Assert.That(department.UICategory, Is.EqualTo(DepartmentUICategory.MinorFaction));
            Assert.That(department.EditorHidden, Is.False);
            var ranks = new[] { "Lookout", "Greaser", "Muscle", "TattooArtist", "Rocker", "HotRod", "Underboss", "Boss" };
            Assert.That(department.Roles.Select(role => role.Id), Is.EquivalentTo(ranks.Select(rank => "AtomCats" + rank)));
            var station = protos.Index<GameMapPrototype>("ReclamationWendover").Stations["Wendover"];
            var jobs = station.StationComponentOverrides.Values.Select(entry => entry.Component).OfType<StationJobsComponent>().Single();
            var factory = em.System<StationSystem>().InitializeNewStation(station, [map.Grid.Owner], "Factory");

            var inventory = em.System<InventorySystem>();
            var slots = em.System<ItemSlotsSystem>();
            var spawn = em.System<StationSpawningSystem>();
            string Prototype(EntityUid item) => em.GetComponent<MetaDataComponent>(item).EntityPrototype!.ID;
            foreach (var rank in ranks)
            {
                var job = protos.Index<JobPrototype>("AtomCats" + rank);
                var seats = rank switch { "Lookout" or "Greaser" or "Muscle" => 3, "TattooArtist" => 2, _ => 1 };
                var whitelistedRank = rank is "Underboss" or "Boss";
                Assert.That(jobs.SetupAvailableJobs[job.ID], Is.EqualTo(new int?[] { seats, seats }), rank);
                Assert.That(job.SetPreference, Is.True, rank);
                Assert.That(job.Whitelisted, Is.EqualTo(whitelistedRank), rank);
                Assert.That(job.HideWithoutJobWhitelist, Is.EqualTo(whitelistedRank), rank);
                var profile = HumanoidCharacterProfile.DefaultWithSpecies();
                Assert.That(job.AlwaysUseSpawner, Is.True, rank);
                var marker = em.SpawnEntity("SpawnPoint" + job.ID, map.GridCoords);
                var point = em.GetComponent<SpawnPointComponent>(marker);
                Assert.That(point.SpawnType, Is.EqualTo(SpawnPointType.Job));
                Assert.That(point.Job?.ID, Is.EqualTo(job.ID));
                var wearer = spawn.SpawnPlayerCharacterOnStation(factory, new JobComponent { Prototype = job.ID },
                    profile, spawnPointType: SpawnPointType.Job)!.Value;
                Assert.That(em.GetComponent<TransformComponent>(wearer).Coordinates,
                    Is.EqualTo(em.GetComponent<TransformComponent>(marker).Coordinates));
                em.DeleteEntity(marker);
                EntityUid Worn(string slot)
                {
                    Assert.That(inventory.TryGetSlotEntity(wearer, slot, out var item), Is.True, $"{rank}: {slot}");
                    return item!.Value;
                }

                Assert.That(em.GetComponent<NpcFactionMemberComponent>(wearer).Factions.Any(f => f.Id == "AtomCats"));
                Assert.That(em.HasComponent<PowerArmorProficiencyComponent>(wearer));
                Assert.That(Prototype(Worn("jumpsuit")), Is.EqualTo("AtomCatsCoveralls"));
                var headset = Worn("ears");
                Assert.That(Prototype(headset), Is.EqualTo("AtomCatsHeadset"));
                var radio = em.GetComponent<EncryptionKeyHolderComponent>(headset);
                Assert.That(radio.Channels, Is.EquivalentTo(new[] { "AtomCats", "WastelandGlobal" }));
                Assert.That(radio.DefaultChannel, Is.EqualTo("AtomCats"));
                Assert.That(Prototype(Worn("eyes")), Is.EqualTo("AtomCatsShades"));
                Assert.That(Prototype(Worn("shoes")), Is.EqualTo("AtomCatsBoots"));
                var pass = Worn("id");
                Assert.That(Prototype(pass), Is.EqualTo("AtomCatsGaragePass"));
                var id = em.GetComponent<IdCardComponent>(pass);
                Assert.That(id.FullName, Is.EqualTo(profile.Name));
                Assert.That(id.LocalizedJobTitle, Is.EqualTo(job.LocalizedName));
                Assert.That(em.GetComponent<MetaDataComponent>(pass).EntityName, Does.Contain("switchblade ID"));
                Assert.That(em.GetComponent<AccessComponent>(pass).Tags.Any(tag => tag.Id == "AtomCats"));
                var carried = em.GetComponent<StorageComponent>(Worn("back")).Container.ContainedEntities.Select(Prototype).ToArray();
                Assert.That(carried.Count(id => id == "AtomCatsStencilCan"), Is.EqualTo(1), rank);
                var selector = Worn("pocket2");
                Assert.That(Prototype(selector), Is.EqualTo($"AtomCats{rank}KitSelector"));
                var choices = em.GetComponent<UndecidedLoadoutBackpackComponent>(selector);
                Assert.That(choices.PossibleSets.Distinct().Count(), Is.EqualTo(3));
                Assert.That(choices.MaxSelectedSets, Is.EqualTo(1));
                if (rank == "TattooArtist")
                {
                    Assert.That(job.Supervisors, Is.EqualTo("job-supervisors-atom-cats-tattoo-artist"));
                    Assert.That(inventory.TryGetSlotEntity(wearer, "belt", out _), Is.False);
                    var stretcher = Worn("pocket1");
                    Assert.That(Prototype(stretcher), Is.EqualTo("AtomCatsCompactStretcher"));
                    Assert.That(em.GetComponent<ItemComponent>(stretcher).Size.Id, Is.EqualTo("Small"));
                    Assert.That(em.GetComponent<FoldableComponent>(stretcher).IsFolded);
                    Assert.That(carried, Does.Contain("N14MagazineTabooTattoos"));
                }
                else
                {
                    var belt = Worn("belt");
                    Assert.That(Prototype(belt), Is.EqualTo("N14ClothingBeltUtilityFilled"));
                    Assert.That(em.GetComponent<StorageComponent>(belt).Container.ContainedEntities.Select(Prototype),
                        Is.EquivalentTo(new[] { "N14Crowbar", "N14Wrench", "N14Screwdriver", "N14Wirecutter", "N14Welder", "N14Multitool" }));
                }

                var frame = Worn("outerClothing");
                if (rank is "Lookout" or "Greaser")
                {
                    Assert.That(Prototype(frame), Is.EqualTo("AtomCatsCut"));
                    Assert.That(carried, Does.Not.Contain("AtomCatsCut"));
                    Assert.That(inventory.TryGetSlotEntity(wearer, "head", out _), Is.False);
                    em.DeleteEntity(wearer);
                    continue;
                }
                Assert.That(carried, Does.Contain("AtomCatsCut"));
                var model = Prototype(frame);
                var finish = rank switch
                {
                    "Muscle" or "Boss" => "Red",
                    "Rocker" or "Underboss" => "Blue",
                    "HotRod" => "Lowrider",
                    "TattooArtist" => "Medic",
                    _ => throw new System.InvalidOperationException(rank),
                };
                Assert.That(model, Is.EqualTo("AtomCatsT45" + finish), rank);
                Assert.That(Prototype(Worn("head")), Is.EqualTo(model + "Helmet"));
                var armor = em.GetComponent<ModularPowerArmorComponent>(frame);
                Assert.That(armor.Sealed);
                Assert.That(armor.Visuals.Values.All(sprite => sprite.EndsWith("/" + finish.ToLowerInvariant() + ".rsi")), Is.True, rank);
                Assert.That(armor.Rating, Is.EqualTo(40));
                var parts = PowerArmorPlates.Slots.Select(s =>
                    em.GetComponent<PowerArmorPlateComponent>(slots.GetItemOrNull(frame, PowerArmorPlates.Container(s))!.Value)).ToArray();
                Assert.That(parts.All(plate => plate.Model.Id == model));
                Assert.That(parts.Select(plate => plate.Rating), Is.EqualTo(new[] { 4, 10, 2, 4 }));
                Assert.That(parts.Select(plate => plate.HitPoints), Is.EqualTo(new[] { 80, 192, 48, 80 }));
                em.DeleteEntity(wearer);
            }
            em.DeleteEntity(factory);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RockerHeadlampOnlyTogglesTheLampOnItsBlueHelmet()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var map = await pair.CreateTestMap();
        var server = pair.Server.ResolveDependency<IEntityManager>();
        var client = pair.Client.ResolveDependency<IEntityManager>();
        var session = pair.Server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);
        EntityUid rocker = default, helmet = default;
        NetEntity netRocker = default;
        await pair.Server.WaitAssertion(() =>
        {
            rocker = server.System<StationSpawningSystem>().SpawnPlayerMob(map.GridCoords,
                new JobComponent { Prototype = "AtomCatsRocker" }, HumanoidCharacterProfile.DefaultWithSpecies(), null);
            pair.Server.PlayerMan.SetAttachedEntity(session, rocker);
            netRocker = server.GetNetEntity(rocker);
            Assert.That(server.System<InventorySystem>().TryGetSlotEntity(rocker, "head", out var head));
            helmet = head!.Value;
            Assert.That(server.GetComponent<MetaDataComponent>(helmet).EntityPrototype!.ID, Is.EqualTo("AtomCatsT45BlueHelmet"));
        });
        await pair.RunTicksSync(15);

        async Task CheckHelmet(bool lit)
        {
            await pair.Client.WaitAssertion(() =>
            {
                var wearer = client.GetEntity(netRocker);
                Assert.That(client.System<InventorySystem>().TryGetSlotEntity(wearer, "head", out var head));
                Assert.That(client.GetComponent<Robust.Client.GameObjects.PointLightComponent>(head!.Value).Enabled, Is.EqualTo(lit));
                var sprite = client.GetComponent<Robust.Client.GameObjects.SpriteComponent>(wearer);
                Assert.That(sprite.LayerMapTryGet("pa-boot-liners", out var liners));
                Assert.That(sprite[liners].RsiState.Name, Is.EqualTo("equipped-FEET"));
                Assert.That(sprite.LayerMapTryGet("pa-frame", out var frameLayer));
                Assert.That(sprite.LayerMapTryGet("pa-Chest", out var chestLayer));
                Assert.That(liners, Is.LessThan(frameLayer));
                Assert.That(frameLayer, Is.LessThan(chestLayer));
                Assert.That(sprite.LayerMapTryGet("head-0", out var shell));
                Assert.That(sprite[shell].ActualRsi?.Path.ToString(), Does.EndWith("/blue-helmet.rsi"));
                Assert.That(sprite[shell].RsiState.Name, Is.EqualTo("off-equipped-HELMET"));
                Assert.That(sprite.LayerMapTryGet("head-1", out _), Is.False);
                Assert.That(sprite.LayerMapTryGet("head-toggle", out var lamp), Is.EqualTo(lit));
                foreach (var hand in new[] { HandLocation.Left, HandLocation.Right })
                {
                    var held = new GetInhandVisualsEvent(wearer, hand);
                    client.EventBus.RaiseLocalEvent(head.Value, held);
                    var side = hand.ToString().ToLowerInvariant();
                    var expected = lit ? new[] { $"off-inhand-{side}", $"on-inhand-{side}-unshaded" } : new[] { $"off-inhand-{side}" };
                    Assert.That(held.Layers.Select(layer => layer.Item2.State), Is.EqualTo(expected));
                }
                if (lit)
                {
                    Assert.That(lamp, Is.GreaterThan(shell));
                    Assert.That(sprite[lamp].ActualRsi, Is.SameAs(sprite[shell].ActualRsi));
                    Assert.That(sprite[lamp].RsiState.Name, Is.EqualTo("equipped-HELMET-unshaded"));
                }
            });
        }

        await CheckHelmet(false);
        foreach (var enabled in new[] { true, false })
        {
            await pair.Server.WaitAssertion(() =>
            {
                var toggle = new ToggleActionEvent { Performer = rocker };
                server.EventBus.RaiseLocalEvent(helmet, toggle);
                Assert.That(toggle.Handled);
            });
            await pair.RunTicksSync(15);
            await CheckHelmet(enabled);
        }
        await pair.Server.WaitPost(() => pair.Server.PlayerMan.SetAttachedEntity(session, null));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task WhitelistedPlayersCanJoinOnlyTheirApprovedLeadershipRanks()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var session = server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);
        var users = server.ResolveDependency<UserDbDataManager>();
        var admins = server.ResolveDependency<IAdminManager>();
        var whitelists = server.ResolveDependency<JobWhitelistManager>();
        var config = server.ResolveDependency<IConfigurationManager>();
        var events = server.ResolveDependency<IEntityManager>().EventBus;
        await PoolManager.WaitUntil(server, () => users.IsLoadComplete(session));

        void Check(bool bossAllowed, bool underbossAllowed)
        {
            var candidates = new StationJobsGetCandidatesEvent(session.UserId,
                ["AtomCatsBoss", "AtomCatsUnderboss", "AtomCatsHotRod"]);
            events.RaiseEvent(EventSource.Local, ref candidates);
            var expected = new[] { ("AtomCatsBoss", bossAllowed), ("AtomCatsUnderboss", underbossAllowed), ("AtomCatsHotRod", true) };
            foreach (var (rank, allowed) in expected)
            {
                Assert.That(candidates.Jobs.Contains(rank), Is.EqualTo(allowed), $"Round start: {rank}");
                var lateJoin = new IsJobAllowedEvent(session, rank);
                events.RaiseEvent(EventSource.Local, ref lateJoin);
                Assert.That(!lateJoin.Cancelled, Is.EqualTo(allowed), $"Late join: {rank}");
            }
        }

        await server.WaitAssertion(() =>
        {
            var wasAdmin = admins.IsAdmin(session);
            var oldWhitelist = config.GetCVar(CCVars.GameRoleWhitelist);
            try
            {
                config.SetCVar(CCVars.GameRoleWhitelist, true);
                if (wasAdmin)
                    admins.DeAdmin(session);
                Assert.That(admins.IsAdmin(session), Is.False);
                Check(false, false);
                whitelists.AddWhitelist(session.UserId, "AtomCatsBoss");
                Check(true, false);
                whitelists.AddWhitelist(session.UserId, "AtomCatsUnderboss");
                Check(true, true);
            }
            finally
            {
                whitelists.RemoveWhitelist(session.UserId, "AtomCatsBoss");
                whitelists.RemoveWhitelist(session.UserId, "AtomCatsUnderboss");
                if (wasAdmin)
                    admins.ReAdmin(session);
                config.SetCVar(CCVars.GameRoleWhitelist, oldWhitelist);
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task QuarterMileShadesProtectBareheadedWelders()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            EntityUid Spawn(string id) => em.SpawnEntity(id, map.GridCoords);
            var user = Spawn("MobHuman");
            var welder = Spawn("N14WelderIndustrial");
            var eyes = em.GetComponent<BlindableComponent>(user);
            var inventory = em.System<InventorySystem>();
            var shades = Spawn("AtomCatsShades");
            Assert.That(em.GetComponent<EyeProtectionComponent>(shades).ProtectionTime,
                Is.EqualTo(em.GetComponent<EyeProtectionComponent>(Spawn("N14ClothingEyesGlassesWelding")).ProtectionTime));
            Assert.That(em.System<SharedHandsSystem>().TryPickup(user, welder));
            Assert.That(em.System<ItemToggleSystem>().TryActivate(welder, user));
            Assert.That(inventory.TryEquip(user, shades, "eyes", force: true));
            em.EventBus.RaiseLocalEvent(welder, new ToolUseAttemptEvent(user, 1f));
            Assert.That(eyes.EyeDamage, Is.Zero);
            Assert.That(inventory.TryUnequip(user, "eyes", force: true));
            em.EventBus.RaiseLocalEvent(welder, new ToolUseAttemptEvent(user, 1f));
            Assert.That(eyes.EyeDamage, Is.Positive);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SwitchbladeCyclesInHandAndOnlyTheCombCanCutHair()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        var session = pair.Server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);
        await pair.Server.WaitAssertion(() =>
        {
            var owner = em.SpawnEntity("MobHuman", map.GridCoords);
            var customer = em.SpawnEntity("MobHuman", map.GridCoords);
            var pass = em.SpawnEntity("AtomCatsGaragePass", map.GridCoords);
            var scissors = em.GetComponent<MagicMirrorComponent>(em.SpawnEntity("BarberScissors", map.GridCoords));
            var access = em.GetComponent<AccessComponent>(pass);
            var identity = em.GetComponent<IdCardComponent>(pass);
            var hands = em.System<SharedHandsSystem>();
            var interaction = em.System<SharedInteractionSystem>();
            var ui = em.System<SharedUserInterfaceSystem>();
            pair.Server.PlayerMan.SetAttachedEntity(session, owner);
            Assert.That(hands.TryPickup(owner, pass));
            void Cycle(string expected)
            {
                Assert.That(interaction.UseInHandInteraction(owner, pass));
                Assert.That(hands.GetActiveItem(owner), Is.EqualTo(pass));
                Assert.That(em.GetComponent<ItemComponent>(pass).HeldPrefix, Is.EqualTo(expected));
            }
            for (var i = 0; i < 2; i++)
            {
                Assert.That(em.HasComponent<MeleeWeaponComponent>(pass), Is.False);
                Assert.That(em.HasComponent<MagicMirrorComponent>(pass), Is.False);
                Cycle("blade");
                Assert.That(em.System<SharedMeleeWeaponSystem>().TryGetWeapon(owner, out var weapon, out var blade));
                Assert.That(weapon, Is.EqualTo(pass));
                Assert.That(blade!.Damage.DamageDict["Slash"].Int(), Is.EqualTo(25));
                interaction.InteractUsing(owner, pass, customer, map.GridCoords);
                Assert.That(ui.IsUiOpen(pass, MagicMirrorUiKey.Key, owner), Is.False);
                Cycle("comb");
                Assert.That(em.HasComponent<MeleeWeaponComponent>(pass), Is.False);
                var comb = em.GetComponent<MagicMirrorComponent>(pass);
                Assert.That(comb.RequireConfirm, Is.EqualTo(scissors.RequireConfirm));
                Assert.That(comb.SelectSlotTime, Is.EqualTo(scissors.SelectSlotTime));
                interaction.InteractUsing(owner, pass, customer, map.GridCoords);
                Assert.That(ui.IsUiOpen(pass, MagicMirrorUiKey.Key, owner));
                Assert.That(comb.Target, Is.EqualTo(customer));
                Cycle("folded");
                Assert.That(ui.IsUiOpen(pass, MagicMirrorUiKey.Key, owner), Is.False);
                Assert.That(em.GetComponent<AccessComponent>(pass), Is.SameAs(access));
                Assert.That(em.GetComponent<IdCardComponent>(pass), Is.SameAs(identity));
            }
            pair.Server.PlayerMan.SetAttachedEntity(session, null);
        });
        await pair.CleanReturnAsync();
    }
}
