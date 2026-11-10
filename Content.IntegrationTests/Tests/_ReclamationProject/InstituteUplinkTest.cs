using System.Linq;
using Content.Server._ReclamationProject.Institute.Uplink;
using Content.Server.Explosion.EntitySystems;
using Content.Server.GameTicking;
using Content.Server.Ghost.Roles;
using Content.Server.Ghost.Roles.Components;
using Content.Server.Roles;
using Content.Server.Station.Systems;
using Content.Server.Store.Systems;
using Content.Shared._ReclamationProject.Institute.Uplink;
using Content.Shared.CCVar;
using Content.Shared.Damage;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mind;
using Content.Shared.Mobs.Systems;
using Content.Shared.Preferences;
using Content.Shared.Roles.Jobs;
using Content.Shared.Store;
using Content.Shared.Store.Components;
using Robust.Server.Console;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._ReclamationProject;

/// <summary>
/// #Reclamation Project - Institute uplink implant: spawn setup, PIN lock, objectives, payouts,
/// points, grenade synths, reinforcement refunds and ghost takeover. Runs on Common Wealth.
/// </summary>
[TestFixture]
public sealed class InstituteUplinkTest
{
    [Test]
    public async Task InstituteUplinkEndToEnd()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            InLobby = true,
            Map = "CommonWealth",
            Dirty = true,
            Connected = true,
        });
        var server = pair.Server;
        var client = pair.Client;
        var entities = server.ResolveDependency<IEntityManager>();
        var timing = server.ResolveDependency<IGameTiming>();
        var config = server.ResolveDependency<IConfigurationManager>();
        var console = server.ResolveDependency<IServerConsoleHost>();
        var minds = entities.System<SharedMindSystem>();
        var uplinkSys = entities.System<InstituteUplinkSystem>();
        var player = pair.Player!;

        await server.WaitPost(() =>
        {
            config.SetCVar(CCVars.GameMap, "CommonWealth");
            console.ExecuteCommand("cvar game.map_rotation false");
            console.ExecuteCommand("setgamepreset Sandbox");
            entities.System<GameTicker>().StartRound();
        });
        await pair.RunTicksSync(20);

        EntityUid director = default, scientist = default, victim = default;
        EntityUid directorMind = default, scientistMind = default, victimMind = default;
        EntityUid directorImplant = default, scientistImplant = default;

        int Points(EntityUid implant) => entities.GetComponent<StoreComponent>(implant).Balance["ResearchPoint"].Int();

        // --- Spawn: implant, starting points, PIN, antag role, briefing ---
        await server.WaitAssertion(() =>
        {
            var station = entities.System<StationSystem>().GetStations().Single();
            var spawning = entities.System<StationSpawningSystem>();
            director = spawning.SpawnPlayerCharacterOnStation(station, new JobComponent { Prototype = "InstituteDirector" },
                HumanoidCharacterProfile.DefaultWithSpecies())!.Value;
            scientist = spawning.SpawnPlayerCharacterOnStation(station, new JobComponent { Prototype = "InstituteScientist" },
                HumanoidCharacterProfile.DefaultWithSpecies())!.Value;
            victim = entities.SpawnEntity("MobHuman", entities.GetComponent<TransformComponent>(director).Coordinates);

            directorImplant = uplinkSys.FindImplant(director)!.Value;
            scientistImplant = uplinkSys.FindImplant(scientist)!.Value;
            Assert.That(Points(directorImplant), Is.EqualTo(120), "Director should start with 120 RP");
            Assert.That(Points(scientistImplant), Is.EqualTo(40), "Scientist should start with 40 RP");

            var roles = entities.System<RoleSystem>();
            directorMind = minds.CreateMind(player.UserId, "Director Test");
            roles.MindAddRole(directorMind, new JobComponent { Prototype = "InstituteDirector" }, silent: true);
            minds.TransferTo(directorMind, director);
            uplinkSys.RegisterMember(directorMind, entities.GetComponent<MindComponent>(directorMind), directorImplant, "InstituteDirector");

            scientistMind = minds.CreateMind(null, "Scientist Test");
            roles.MindAddRole(scientistMind, new JobComponent { Prototype = "InstituteScientist" }, silent: true);
            minds.TransferTo(scientistMind, scientist);
            uplinkSys.RegisterMember(scientistMind, entities.GetComponent<MindComponent>(scientistMind), scientistImplant, "InstituteScientist");

            victimMind = minds.CreateMind(null, "Victim Test");
            minds.TransferTo(victimMind, victim);

            var pin = entities.GetComponent<InstituteUplinkComponent>(directorImplant).Pin;
            Assert.That(pin, Has.Length.EqualTo(4));
            Assert.That(roles.MindIsAntagonist(directorMind), "Institute members should be antags");
            Assert.That(roles.MindGetBriefing(directorMind), Does.Contain(pin), "PIN missing from the character menu briefing");

            // Rank locks: the command MOD suit only shows for command, the network for everyone.
            var store = entities.System<StoreSystem>();
            var dirListings = store.GetAvailableListings(director, directorImplant, entities.GetComponent<StoreComponent>(directorImplant))
                .Select(l => l.ID).ToHashSet();
            var sciListings = store.GetAvailableListings(scientist, scientistImplant, entities.GetComponent<StoreComponent>(scientistImplant))
                .Select(l => l.ID).ToHashSet();
            Assert.That(dirListings, Does.Contain("InstituteUplinkCommandModsuit"));
            Assert.That(sciListings, Does.Not.Contain("InstituteUplinkCommandModsuit"));
            Assert.That(sciListings, Does.Not.Contain("InstituteUplinkReinforcementCourser"));
            Assert.That(sciListings, Does.Contain("InstituteUplinkNetwork"));
            Assert.That(sciListings, Does.Contain("InstituteUplinkSynthGrenadeEnforcer"));
        });

        void Send(EntityEventArgs ev)
        {
            client.EntMan.EntityNetManager!.SendSystemNetworkMessage(ev);
        }

        // --- PIN: wrong one stays locked, right one unlocks ---
        var implantNet = entities.GetNetEntity(directorImplant);
        await client.WaitPost(() => Send(new InstitutePinSubmitEvent { Implant = implantNet, Pin = "abcd" }));
        await pair.RunTicksSync(10);
        await server.WaitAssertion(() =>
        {
            var uplink = entities.GetComponent<InstituteUplinkComponent>(directorImplant);
            Assert.That(uplink.UnlockedFor, Is.Null, "a wrong PIN unlocked the uplink");
            uplink.NextAttempt = TimeSpan.Zero;
        });
        var rightPin = string.Empty;
        await server.WaitPost(() => rightPin = entities.GetComponent<InstituteUplinkComponent>(directorImplant).Pin);
        await client.WaitPost(() => Send(new InstitutePinSubmitEvent { Implant = implantNet, Pin = rightPin }));
        await pair.RunTicksSync(10);
        var ui = entities.System<Robust.Server.GameObjects.UserInterfaceSystem>();
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<InstituteUplinkComponent>(directorImplant).UnlockedFor, Is.EqualTo(director));
            Assert.That(ui.IsUiOpen(directorImplant, StoreUiKey.Key, director), "the right PIN didn't open the shop");
        });

        // Pressing the button again closes it, and a third press opens it (toggle).
        await server.WaitAssertion(() =>
        {
            var ev = new OpenInstituteUplinkEvent { Performer = director };
            entities.EventBus.RaiseLocalEvent(directorImplant, ev);
            Assert.That(ui.IsUiOpen(directorImplant, StoreUiKey.Key, director), Is.False, "second press didn't close the shop");
            ev = new OpenInstituteUplinkEvent { Performer = director };
            entities.EventBus.RaiseLocalEvent(directorImplant, ev);
            Assert.That(ui.IsUiOpen(directorImplant, StoreUiKey.Key, director), "third press didn't open the shop");
        });
        await pair.RunTicksSync(10);
        await client.WaitAssertion(() =>
        {
            var clientUi = client.EntMan.System<Robust.Client.GameObjects.UserInterfaceSystem>();
            var clientImplant = client.EntMan.GetEntity(implantNet);
            Assert.That(client.EntMan.EntityExists(clientImplant), "the client doesn't know the implant entity");
            Assert.That(clientUi.IsUiOpen(clientImplant, StoreUiKey.Key), "the shop isn't open on the client");
        });

        // --- Tablet: locked for its owner until the PIN is typed, locks again when dropped ---
        EntityUid tablet = default;
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.System<Content.Shared.Inventory.InventorySystem>().TryGetSlotEntity(director, "id", out var id));
            tablet = id!.Value;
            var lockComp = entities.GetComponent<InstituteTabletLockComponent>(tablet);
            Assert.That(lockComp.Owned, "the Director's tablet has no owner");
            Assert.That(lockComp.Pin, Is.EqualTo(entities.GetComponent<InstituteUplinkComponent>(directorImplant).Pin));
            var attempt = new Content.Shared.UserInterface.ActivatableUIOpenAttemptEvent(director);
            entities.EventBus.RaiseLocalEvent(tablet, attempt);
            Assert.That(attempt.Cancelled, "a locked tablet opened without the PIN");
        });
        var tabletNet = entities.GetNetEntity(tablet);
        await client.WaitPost(() => Send(new InstitutePinSubmitEvent { Implant = tabletNet, Pin = rightPin }));
        await pair.RunTicksSync(10);
        await server.WaitAssertion(() =>
        {
            var lockComp = entities.GetComponent<InstituteTabletLockComponent>(tablet);
            Assert.That(lockComp.UnlockedFor, Is.EqualTo(director), "the right PIN didn't unlock the tablet");
            var attempt = new Content.Shared.UserInterface.ActivatableUIOpenAttemptEvent(director);
            entities.EventBus.RaiseLocalEvent(tablet, attempt);
            Assert.That(attempt.Cancelled, Is.False, "an unlocked tablet still refused to open");
            entities.EventBus.RaiseLocalEvent(tablet, new Content.Shared.Interaction.Events.DroppedEvent(director));
            Assert.That(lockComp.UnlockedFor, Is.Null, "dropping the tablet didn't lock it");
        });

        // --- Objectives: custom (force complete), steal (auto), kill (auto) ---
        var sciNet = entities.GetNetEntity(scientistMind);
        var victimNet = entities.GetNetEntity(victimMind);
        await client.WaitPost(() =>
        {
            Send(new InstituteAssignObjectiveEvent { Target = sciNet, Kind = InstituteObjectiveKind.Custom, Title = "Test order", Reward = 30 });
            Send(new InstituteAssignObjectiveEvent { Target = sciNet, Kind = InstituteObjectiveKind.Steal, Item = "C4", Reward = 10 });
            Send(new InstituteAssignObjectiveEvent { Target = sciNet, Kind = InstituteObjectiveKind.Kill, Person = victimNet, Reward = 15 });
        });
        await pair.RunTicksSync(10);

        EntityUid custom = default;
        await server.WaitAssertion(() =>
        {
            var objectives = entities.GetComponent<MindComponent>(scientistMind).Objectives;
            Assert.That(objectives, Has.Count.EqualTo(3), "the scientist didn't get three objectives");
            custom = objectives.First(o => entities.HasComponent<InstituteCustomObjectiveComponent>(o));
            Assert.That(entities.GetComponent<MetaDataComponent>(custom).EntityName, Is.EqualTo("Test order"));
            Assert.That(Points(scientistImplant), Is.EqualTo(40), "something paid out before it was done");

            // Steal: hold a C4. Kill: the victim dies.
            var c4 = entities.SpawnEntity("C4", entities.GetComponent<TransformComponent>(scientist).Coordinates);
            entities.System<SharedHandsSystem>().TryPickupAnyHand(scientist, c4);
            entities.System<DamageableSystem>().TryChangeDamage(victim, new DamageSpecifier { DamageDict = { ["Blunt"] = 400 } }, true);
        });
        await pair.RunTicksSync(150);
        await server.WaitAssertion(() =>
            Assert.That(Points(scientistImplant), Is.EqualTo(40 + 10 + 15), "steal + kill rewards weren't paid"));

        var customNet = entities.GetNetEntity(custom);
        await client.WaitPost(() => Send(new InstituteObjectiveActionEvent { Objective = customNet, Remove = false }));
        await pair.RunTicksSync(10);
        await server.WaitAssertion(() =>
        {
            Assert.That(Points(scientistImplant), Is.EqualTo(95), "force complete didn't pay the custom order");
            Assert.That(entities.GetComponent<InstituteObjectiveComponent>(custom).Paid);
        });

        // --- Send points, reset PIN (shop stays unlocked) ---
        string oldSciPin = string.Empty;
        await server.WaitPost(() => oldSciPin = entities.GetComponent<InstituteUplinkComponent>(scientistImplant).Pin);
        await client.WaitPost(() =>
        {
            Send(new InstituteSendPointsEvent { Target = sciNet, Amount = 20 });
            Send(new InstituteResetPinEvent { Target = null });
        });
        await pair.RunTicksSync(10);
        await server.WaitAssertion(() =>
        {
            Assert.That(Points(directorImplant), Is.EqualTo(100));
            Assert.That(Points(scientistImplant), Is.EqualTo(115));
            Assert.That(entities.GetComponent<InstituteUplinkComponent>(directorImplant).UnlockedFor, Is.EqualTo(director),
                "a PIN reset locked an already unlocked shop");
            // 1 in 10000 that the new random PIN matches the old one.
            Assert.That(entities.GetComponent<InstituteUplinkComponent>(scientistImplant).Pin, Is.Not.EqualTo(oldSciPin));
        });

        // Command can give themselves orders.
        var dirNet = entities.GetNetEntity(directorMind);
        await client.WaitPost(() => Send(new InstituteAssignObjectiveEvent { Target = dirNet, Kind = InstituteObjectiveKind.Custom, Title = "Self order", Reward = 5 }));
        await pair.RunTicksSync(10);
        await server.WaitAssertion(() =>
            Assert.That(entities.GetComponent<MindComponent>(directorMind).Objectives.Any(o => entities.GetComponent<MetaDataComponent>(o).EntityName == "Self order"),
                "the Director couldn't give themselves an objective"));

        await client.WaitPost(() => Send(new InstituteSetPinEvent { Target = sciNet, Pin = "1234" }));
        await pair.RunTicksSync(10);
        await server.WaitAssertion(() =>
            Assert.That(entities.GetComponent<InstituteUplinkComponent>(scientistImplant).Pin, Is.EqualTo("1234"), "command couldn't set a PIN"));

        // --- Synth grenade: 3 synths, the dead one and the timed-out ones relay out ---
        await server.WaitAssertion(() =>
        {
            var grenade = entities.SpawnEntity("InstituteSynthGrenadeEnforcer", entities.GetComponent<TransformComponent>(director).Coordinates);
            entities.System<TriggerSystem>().Trigger(grenade);
        });
        await pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
        {
            var synths = entities.EntityQuery<InstituteTemporarySynthComponent>().Select(c => c.Owner).ToList();
            Assert.That(synths, Has.Count.EqualTo(3), "the grenade didn't relay in 3 synths");
            Assert.That(synths.All(s => entities.GetComponent<MetaDataComponent>(s).EntityPrototype?.ID == "ReclamationMobGenOneSynthBaton"));
            entities.System<DamageableSystem>().TryChangeDamage(synths[0], new DamageSpecifier { DamageDict = { ["Blunt"] = 1000 } }, true);
            entities.GetComponent<InstituteTemporarySynthComponent>(synths[1]).DespawnAt = timing.CurTime;
        });
        await pair.RunTicksSync(90);
        await server.WaitAssertion(() =>
            Assert.That(entities.EntityQuery<InstituteTemporarySynthComponent>().Count(), Is.EqualTo(1),
                "the dead and the timed-out synth should have relayed out"));

        // --- Reinforcement: unanswered = refund, answered = ghost becomes a Courser with an empty wallet ---
        EntityUid beacon = default;
        await server.WaitAssertion(() =>
        {
            entities.EventBus.RaiseLocalEvent(directorImplant, new StoreBuyListingMessage("InstituteUplinkReinforcementSynth") { Actor = director });
            Assert.That(Points(directorImplant), Is.EqualTo(40), "buying the synth reinforcement didn't cost 60");
            beacon = entities.EntityQuery<InstituteReinforcementBeaconComponent>().Single().Owner;
            entities.GetComponent<InstituteReinforcementBeaconComponent>(beacon).RefundAt = timing.CurTime;
        });
        await pair.RunTicksSync(90);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.Deleted(beacon) || entities.IsQueuedForDeletion(beacon), "the unanswered beacon stayed");
            Assert.That(Points(directorImplant), Is.EqualTo(100), "the unanswered reinforcement wasn't refunded");

            // Give the director enough for a Courser, then let the test player's ghost take it.
            entities.GetComponent<StoreComponent>(directorImplant).Balance["ResearchPoint"] = 120;
            entities.EventBus.RaiseLocalEvent(directorImplant, new StoreBuyListingMessage("InstituteUplinkReinforcementCourser") { Actor = director });
            beacon = entities.EntityQuery<InstituteReinforcementBeaconComponent>().Single().Owner;
            minds.TransferTo(directorMind, null);
            var ghostRole = entities.GetComponent<GhostRoleComponent>(beacon);
            Assert.That(entities.System<GhostRoleSystem>().Takeover(player, ghostRole.Identifier), "ghost couldn't take the Courser role");
        });
        await pair.RunTicksSync(10);
        await server.WaitAssertion(() =>
        {
            var body = player.AttachedEntity!.Value;
            Assert.That(entities.System<MobStateSystem>().IsAlive(body));
            Assert.That(minds.TryGetMind(body, out var newMind, out _));
            var member = entities.GetComponent<InstituteMemberComponent>(newMind);
            Assert.That(member.Job.Id, Is.EqualTo("InstituteCourser"));
            var implant = uplinkSys.FindImplant(body);
            Assert.That(implant, Is.Not.Null, "the Courser reinforcement has no uplink");
            Assert.That(Points(implant!.Value), Is.EqualTo(0), "reinforcements must arrive with 0 RP");
            Assert.That(entities.System<RoleSystem>().MindIsAntagonist(newMind));
        });

        await pair.CleanReturnAsync();
    }
}
