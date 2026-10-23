using System.Linq;
using System.Numerics;
using Content.Server.GameTicking;
using Content.Server.Station.Systems;
using Content.Shared.Alert;
using Content.Shared.CCVar;
using Content.Shared.Storage.EntitySystems;
using Content.Server.Flash.Components;
using Content.Shared._Misfits.Traits;
using Content.Shared._ReclamationProject.Institute.Augment;
using Content.Shared.Damage.Components;
using Content.Shared.Mobs;
using Content.Shared._Misfits.Carrying;
using Content.Shared._ReclamationProject.Medical;
using Content.Shared._Shitmed.Medical.Surgery;
using Content.Shared._Shitmed.Medical.Surgery.Conditions;
using Robust.Shared.Prototypes;
using Content.Shared.Inventory;
using Content.Shared.Preferences;
using Content.Shared.Roles.Jobs;
using Robust.Server.Console;
using Robust.Shared.Configuration;
using Content.Server._ReclamationProject.Institute.Relay;
using Content.Shared._ReclamationProject.Institute.Relay;
using Content.Shared.Damage;
using Content.Shared.Actions;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.NPC.Systems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture]
public sealed class InstituteRelayTest
{
    [Test]
    public async Task DirectorSpawnsOnCommonWealthWithRelayImplant()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            InLobby = true,
            Map = "CommonWealth",
            Dirty = true
        });
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var config = server.ResolveDependency<IConfigurationManager>();
        var console = server.ResolveDependency<IServerConsoleHost>();

        await server.WaitPost(() =>
        {
            config.SetCVar(CCVars.GameMap, "CommonWealth");
            console.ExecuteCommand("cvar game.map_rotation false");
            console.ExecuteCommand("setgamepreset Sandbox");
            entities.System<GameTicker>().StartRound();
        });
        await pair.RunTicksSync(20);

        await server.WaitAssertion(() =>
        {
            var station = entities.System<StationSystem>().GetStations().Single();
            var jobs = entities.System<StationJobsSystem>();
            foreach (var job in new[] { "InstituteDirector", "InstituteDivisionHead", "InstituteScientist", "InstituteCourser", "InstituteSecurity" })
                Assert.That(jobs.TryGetJobSlot(station, job, out _), Is.True, $"{job} has no slot on Common Wealth");

            var director = entities.System<StationSpawningSystem>().SpawnPlayerCharacterOnStation(
                station,
                new JobComponent { Prototype = "InstituteDirector" },
                HumanoidCharacterProfile.DefaultWithSpecies());
            Assert.That(director, Is.Not.Null, "Director did not spawn on Common Wealth");

            var organs = entities.System<SharedBodySystem>().GetBodyOrganComponents<InstituteRelayImplantComponent>(director!.Value);
            Assert.That(organs.Any(o => entities.GetComponent<MetaDataComponent>(o.Comp.Owner).EntityPrototype?.ID == "InstituteRelayImplantCommand"),
                "Director spawned without the command relay implant in their head");
            Assert.That(entities.System<MobStateSystem>().IsAlive(director.Value), "Director was killed by their own implant");

            // Director's own kit and perks.
            var inventory = entities.System<InventorySystem>();
            Assert.That(inventory.TryGetSlotEntity(director.Value, "outerClothing", out var coat)
                && entities.GetComponent<MetaDataComponent>(coat.Value).EntityPrototype?.ID == "InstituteDirectorCoat", "Director has no Director's coat");
            Assert.That(inventory.TryGetSlotEntity(director.Value, "shoes", out var boots)
                && entities.GetComponent<MetaDataComponent>(boots.Value).EntityPrototype?.ID == "N14ClothingBootsCombatMK2Filled", "Director has no combat boots MK2");
            Assert.That(entities.HasComponent<CanFiremanCarryComponent>(director.Value), "Director can't fireman carry");
            Assert.That(entities.HasComponent<TraumaRegenerationComponent>(director.Value), "Director lacks trauma regeneration");
            var regen = entities.GetComponent<PassiveDamageComponent>(director.Value);
            Assert.That(regen.AllowedStates, Does.Contain(MobState.Critical), "Director's regen doesn't work in crit");
            Assert.That(regen.Damage.DamageDict["Blunt"].Float(), Is.EqualTo(-0.5f).Within(0.001f), "Director's regen isn't 0.5 per second");

            // Augments: 15% less damage on top of armour, Adamantium Skeleton, flash-immune eyes.
            Assert.That(entities.GetComponent<InstituteAugmentComponent>(director.Value).DamageMultiplier, Is.EqualTo(0.85f).Within(0.001f));
            Assert.That(entities.GetComponent<GibModifierComponent>(director.Value).Ungibbable, Is.True, "Director lacks Adamantium Skeleton");
            Assert.That(entities.HasComponent<FlashImmunityComponent>(director.Value), "Director's eyes aren't flash immune");

            // 100 Blunt through the coat (x0.60) and the augment (x0.85) = 51. Healing is not cut.
            var damageable = entities.System<DamageableSystem>();
            damageable.TryChangeDamage(director.Value, new DamageSpecifier { DamageDict = { ["Blunt"] = 100 } });
            Assert.That(entities.GetComponent<DamageableComponent>(director.Value).TotalDamage.Float(), Is.EqualTo(51f).Within(0.5f),
                "augment + coat didn't reduce a 100 Blunt hit to 51");
            damageable.TryChangeDamage(director.Value, new DamageSpecifier { DamageDict = { ["Blunt"] = -20 } });
            Assert.That(entities.GetComponent<DamageableComponent>(director.Value).TotalDamage.Float(), Is.EqualTo(31f).Within(0.5f),
                "the augment shrank healing");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RelayCarriesBodyAndPunishesEarlyUse()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var containers = entities.System<SharedContainerSystem>();
        var xforms = entities.System<SharedTransformSystem>();
        var relay = entities.System<InstituteRelaySystem>();

        EntityUid mob = default;
        EntityUid implant = default;
        EntityUid eye = default;

        await server.WaitAssertion(() =>
        {
            mob = entities.SpawnEntity("MobHuman", map.GridCoords);
            entities.System<NpcFactionSystem>().AddFaction(mob, "Institute");
            implant = relay.TryAddRelayOrgan(mob, "InstituteRelayImplantCommand")!.Value;

            Assert.That(entities.System<MobStateSystem>().IsAlive(mob), "an Institute member was killed by their own implant");
            Assert.That(entities.GetComponent<InstituteRelayImplantComponent>(implant).ActionEntity, Is.Not.Null, "command implant has no Relay action");
            Assert.That(entities.GetComponent<OrganComponent>(implant).Body, Is.EqualTo(mob), "relay implant is not in the head");

            // Start: the body rides inside the signal.
            Assert.That(relay.TryStartRelay(mob, implant), Is.True);
            Assert.That(containers.IsEntityInContainer(mob), Is.True);
            eye = entities.GetComponent<InstituteRelayRiderComponent>(mob).Eye!.Value;

            // Fly 3 tiles east, then relay in there.
            xforms.SetMapCoordinates(eye, map.MapCoords.Offset(new Vector2(3, 0)));
            entities.EventBus.RaiseLocalEvent(eye, new InstituteRelayArriveEvent { Performer = mob });

            Assert.That(containers.IsEntityInContainer(mob), Is.False);
            Assert.That(xforms.GetWorldPosition(mob).X, Is.EqualTo(map.MapCoords.Position.X + 3).Within(0.01f));
            Assert.That(entities.HasComponent<InstituteRelayRiderComponent>(mob), Is.False);
            Assert.That(entities.GetComponent<DamageableComponent>(mob).TotalDamage.Float(), Is.Zero, "first relay should not hurt");
        });

        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.Deleted(eye), Is.True, "relay signal was not cleaned up");

            // Cancel goes back to where you left and costs nothing.
            var before = xforms.GetWorldPosition(mob);
            Assert.That(relay.TryStartRelay(mob, implant), Is.True);
            var cancelEye = entities.GetComponent<InstituteRelayRiderComponent>(mob).Eye!.Value;
            xforms.SetMapCoordinates(cancelEye, map.MapCoords.Offset(new Vector2(-5, 0)));
            entities.EventBus.RaiseLocalEvent(cancelEye, new InstituteRelayCancelEvent { Performer = mob });
            Assert.That(xforms.GetWorldPosition(mob).X, Is.EqualTo(before.X).Within(0.01f));
            Assert.That(entities.GetComponent<DamageableComponent>(mob).TotalDamage.Float(), Is.Zero, "cancel should not hurt");

            // The first arrival started the cooldown, with its alert up.
            var relayImplant = entities.GetComponent<InstituteRelayImplantComponent>(implant);
            Assert.That(relayImplant.CoolingDown, Is.True, "no cooldown after relaying in");
            Assert.That(entities.System<AlertsSystem>().IsShowingAlert(mob, relayImplant.CooldownAlert), Is.True, "cooldown alert not shown");

            // Relaying in again inside the minute: humans have a stomach, so 15 Heat.
            Assert.That(relay.TryStartRelay(mob, implant), Is.True);
            var earlyEye = entities.GetComponent<InstituteRelayRiderComponent>(mob).Eye!.Value;
            entities.EventBus.RaiseLocalEvent(earlyEye, new InstituteRelayArriveEvent { Performer = mob });
            var damage = entities.GetComponent<DamageableComponent>(mob).Damage.DamageDict;
            Assert.That(damage.TryGetValue("Heat", out var heat) ? heat.Float() : 0f, Is.EqualTo(15f).Within(0.5f));

            // Wearing an Institute MOD suit soaks the early-relay damage.
            var suited = entities.SpawnEntity("MobHuman", map.GridCoords);
            entities.System<NpcFactionSystem>().AddFaction(suited, "Institute");
            var suit = entities.SpawnEntity("ReclamationClothingModsuitInstitute", map.GridCoords);
            Assert.That(entities.System<InventorySystem>().TryEquip(suited, suit, "back", silent: true, force: true), Is.True);
            var suitedImplant = relay.TryAddRelayOrgan(suited, "InstituteRelayImplantCommand")!.Value;
            for (var i = 0; i < 2; i++)
            {
                Assert.That(relay.TryStartRelay(suited, suitedImplant), Is.True);
                var suitedEye = entities.GetComponent<InstituteRelayRiderComponent>(suited).Eye!.Value;
                entities.EventBus.RaiseLocalEvent(suitedEye, new InstituteRelayArriveEvent { Performer = suited });
            }
            Assert.That(entities.GetComponent<DamageableComponent>(suited).TotalDamage.Float(), Is.Zero, "MOD suit did not stop relay damage");

            // Shorten the cooldown so the test can watch it finish and clear the alert.
            relayImplant.Cooldown = TimeSpan.FromSeconds(0.5);

            // Deleting the signal mid-flight must not delete the body.
            Assert.That(relay.TryStartRelay(mob, implant), Is.True);
            var doomedEye = entities.GetComponent<InstituteRelayRiderComponent>(mob).Eye!.Value;
            entities.DeleteEntity(doomedEye);
            Assert.That(entities.EntityExists(mob), Is.True, "deleting the relay signal deleted the body");
            Assert.That(containers.IsEntityInContainer(mob), Is.False);
        });

        await server.WaitAssertion(() =>
        {
            // Grab chain: user grabs A, A grabs B. Both come along and land with the user.
            var pulling = entities.System<PullingSystem>();
            var a = entities.SpawnEntity("MobHuman", map.GridCoords);
            var b = entities.SpawnEntity("MobHuman", map.GridCoords);
            Assert.That(pulling.TryStartPull(mob, a), Is.True);
            Assert.That(pulling.TryStartPull(a, b), Is.True);

            Assert.That(relay.TryStartRelay(mob, implant), Is.True);
            var groupEye = entities.GetComponent<InstituteRelayRiderComponent>(mob).Eye!.Value;
            Assert.That(containers.IsEntityInContainer(a) && containers.IsEntityInContainer(b), Is.True, "grab chain was left behind");

            var speed = entities.GetComponent<MovementSpeedModifierComponent>(groupEye);
            Assert.That(speed.BaseSprintSpeed, Is.EqualTo(24f), "relay signal is slower than an admin ghost");

            xforms.SetMapCoordinates(groupEye, map.MapCoords.Offset(new Vector2(0, 4)));
            entities.EventBus.RaiseLocalEvent(groupEye, new InstituteRelayArriveEvent { Performer = mob });
            foreach (var body in new[] { mob, a, b })
            {
                Assert.That(containers.IsEntityInContainer(body), Is.False);
                Assert.That(xforms.GetWorldPosition(body).Y, Is.EqualTo(map.MapCoords.Position.Y + 4).Within(0.01f));
            }

            // Dragged boxes come along too, contents and all, and don't get hurt.
            var crate = entities.SpawnEntity("CrateGenericSteel", map.GridCoords);
            var loot = entities.SpawnEntity("FoodApple", map.GridCoords);
            Assert.That(entities.System<SharedEntityStorageSystem>().Insert(loot, crate), Is.True);
            Assert.That(pulling.TryStartPull(mob, crate), Is.True, "can't drag the crate");
            Assert.That(relay.TryStartRelay(mob, implant), Is.True);
            var crateEye = entities.GetComponent<InstituteRelayRiderComponent>(mob).Eye!.Value;
            Assert.That(containers.IsEntityInContainer(crate), Is.True, "dragged crate was left behind");
            xforms.SetMapCoordinates(crateEye, map.MapCoords.Offset(new Vector2(6, 6)));
            entities.EventBus.RaiseLocalEvent(crateEye, new InstituteRelayArriveEvent { Performer = mob });
            Assert.That(xforms.GetWorldPosition(crate).X, Is.EqualTo(map.MapCoords.Position.X + 6).Within(0.01f), "crate didn't land with the user");
            Assert.That(containers.ContainsEntity(crate, loot), Is.True, "crate lost its contents");
            if (entities.TryGetComponent<DamageableComponent>(crate, out var crateDamage))
                Assert.That(crateDamage.TotalDamage.Float(), Is.Zero, "the crate took relay damage");

            // A non-Institute head gets its brain fried.
            var outsider = entities.SpawnEntity("MobHuman", map.GridCoords);
            Assert.That(relay.TryAddRelayOrgan(outsider, "InstituteRelayImplant"), Is.Not.Null);
            Assert.That(entities.System<MobStateSystem>().IsDead(outsider), Is.True, "outsider survived the Institute implant");

            // Base implant (scientist/security/courser) has no Relay button.
            var scientist = entities.SpawnEntity("MobHuman", map.GridCoords);
            entities.System<NpcFactionSystem>().AddFaction(scientist, "Institute");
            var plain = relay.TryAddRelayOrgan(scientist, "InstituteRelayImplant")!.Value;
            Assert.That(entities.GetComponent<InstituteRelayImplantComponent>(plain).ActionEntity, Is.Null);
            Assert.That(entities.System<MobStateSystem>().IsAlive(scientist), Is.True);

            // Taking the command implant out (surgery's remove step) takes the Relay button with it.
            var bodies = entities.System<SharedBodySystem>();
            var director = entities.SpawnEntity("MobHuman", map.GridCoords);
            entities.System<NpcFactionSystem>().AddFaction(director, "Institute");
            var chip = relay.TryAddRelayOrgan(director, "InstituteRelayImplantCommand")!.Value;
            var relayAction = entities.GetComponent<InstituteRelayImplantComponent>(chip).ActionEntity!.Value;

            // The head surgery menu offers Remove (chip present), not Insert.
            var surgery = entities.System<SharedSurgerySystem>();
            Assert.That(surgery.AllSurgeries, Does.Contain(new EntProtoId("SurgeryRemoveInstituteRelay")));
            Assert.That(surgery.AllSurgeries, Does.Contain(new EntProtoId("SurgeryInsertInstituteRelay")));
            var directorHead = bodies.GetBodyChildrenOfType(director, BodyPartType.Head).First().Id;
            Assert.That(SurgeryValid(director, directorHead, "SurgeryRemoveInstituteRelay"), Is.True, "can't remove the relay implant by surgery");
            Assert.That(SurgeryValid(director, directorHead, "SurgeryInsertInstituteRelay"), Is.False, "insert offered while the chip is in");
            Assert.That(bodies.RemoveOrgan(chip), Is.True);
            Assert.That(entities.System<SharedActionsSystem>().GetActions(director).Any(a => a.Id == relayAction), Is.False,
                "Relay button stayed after the implant was removed");

            // Every head has a relay slot, so surgery can put the chip into anyone. An outsider dies.
            var thief = entities.SpawnEntity("MobHuman", map.GridCoords);
            var (thiefHead, thiefHeadComp) = bodies.GetBodyChildrenOfType(thief, BodyPartType.Head).First();
            Assert.That(thiefHeadComp.Organs.ContainsKey(InstituteRelayImplantComponent.SlotId), Is.True, "head has no relay slot");
            Assert.That(SurgeryValid(thief, thiefHead, "SurgeryInsertInstituteRelay"), Is.True, "can't insert the relay implant by surgery");
            Assert.That(SurgeryValid(thief, thiefHead, "SurgeryRemoveInstituteRelay"), Is.False, "remove offered on an empty head");
            Assert.That(bodies.InsertOrgan(thiefHead, chip, InstituteRelayImplantComponent.SlotId, thiefHeadComp), Is.True);
            Assert.That(entities.System<MobStateSystem>().IsDead(thief), Is.True, "outsider survived a surgically inserted relay implant");

            bool SurgeryValid(EntityUid patient, EntityUid part, string id)
            {
                var ev = new SurgeryValidEvent(patient, part);
                entities.EventBus.RaiseLocalEvent(surgery.GetSingleton(id)!.Value, ref ev);
                return !ev.Cancelled;
            }
        });

        await server.WaitRunTicks(90);

        await server.WaitAssertion(() =>
        {
            var relayImplant = entities.GetComponent<InstituteRelayImplantComponent>(implant);
            Assert.That(relayImplant.CoolingDown, Is.False, "cooldown never finished");
            Assert.That(entities.System<AlertsSystem>().IsShowingAlert(mob, relayImplant.CooldownAlert), Is.False, "cooldown alert stuck after it finished");
        });

        await pair.CleanReturnAsync();
    }
}
