using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._ReclamationProject.Medical;
using Content.Server.Body.Components;
using Content.Server.GameTicking;
using Content.Server.Power.Components;
using Content.Server.Spawners.Components;
using Content.Shared._Shitmed.Autodoc.Components;
using Content.Shared._Shitmed.Autodoc.Systems;
using Content.Shared._Shitmed.Body.Organ;
using Content.Shared.Body.Systems;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.CCVar;
using Content.Shared.Damage;
using Content.Shared.DeviceLinking;
using Content.Shared.Mobs.Systems;
using Robust.Server.Console;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._ReclamationProject;

/// <summary>
/// #Reclamation Project - on the real Common Wealth map, a powered stasis bed works as an Autodoc operating table
/// and keeps the patient alive past the organ-loss death countdown with their heart and brain out.
/// The same body on a plain operating table dies.
/// (Blank test maps make a lying body slide off its bed within a second, so this uses the real map.)
/// </summary>
[TestFixture]
public sealed class StasisLifeSupportTest
{
    [TestCase("StasisBed", true)]
    [TestCase("OperatingTable", false)]
    public async Task StasisBedKeepsOrganlessPatientAlive(string bedId, bool survives)
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
        EntityUid bed = default, patient = default;

        await server.WaitPost(() =>
        {
            config.SetCVar(CCVars.GameMap, "CommonWealth");
            console.ExecuteCommand("cvar game.map_rotation false");
            console.ExecuteCommand("setgamepreset Sandbox");
            entities.System<GameTicker>().StartRound();
        });
        await pair.RunTicksSync(20);

        EntityCoordinates spot = default;
        await server.WaitPost(() =>
        {
            var spawnPoint = entities.EntityQuery<SpawnPointComponent>().First().Owner;
            spot = entities.GetComponent<TransformComponent>(spawnPoint).Coordinates;
            bed = entities.SpawnEntity(bedId, spot);
            if (entities.TryGetComponent<ApcPowerReceiverComponent>(bed, out var power))
                power.NeedsPower = false;
            // Start next to the bed and get put on it, like a player.
            patient = entities.SpawnEntity("MobHuman", spot.Offset(new Vector2(0, 1)));
        });
        await pair.RunTicksSync(5); // the power system applies NeedsPower on its next tick

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.System<SharedBuckleSystem>().TryBuckle(patient, patient, bed, popup: false), "couldn't lie down");
            Assert.That(entities.System<StasisLifeSupportSystem>().IsOnLifeSupport(patient), Is.EqualTo(survives),
                "life support state is wrong right after lying down");

            // The Autodoc links to it and sees the patient.
            var autodoc = entities.SpawnEntity("Autodoc", spot.Offset(new Vector2(1, 0)));
            entities.System<SharedDeviceLinkSystem>().SaveLinks(null, bed, autodoc,
                new List<(string, string)> { ("OperatingTable", "OperatingTableReceiver") });
            Assert.That(entities.System<SharedAutodocSystem>().GetPatient((autodoc, entities.GetComponent<AutodocComponent>(autodoc))),
                Is.EqualTo(patient), $"Autodoc can't use a {bedId}");

            // Take the heart and brain out.
            var body = entities.System<SharedBodySystem>();
            foreach (var organ in body.GetBodyOrgans(patient).ToArray())
            {
                if (entities.HasComponent<HeartComponent>(organ.Id) || entities.HasComponent<BrainComponent>(organ.Id))
                {
                    Assert.That(body.RemoveOrgan(organ.Id), $"couldn't remove {entities.ToPrettyString(organ.Id)}");
                    entities.DeleteEntity(organ.Id); // the Autodoc keeps removed organs in its storage, not on the floor
                }
            }
        });

        // Past the 60 s organ-loss death countdown.
        await pair.RunSeconds(65);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<BuckleComponent>(patient).Buckled, "patient left the bed during the test");
            var alive = !entities.System<MobStateSystem>().IsDead(patient);
            Assert.That(alive, Is.EqualTo(survives), survives
                ? "patient died on a powered stasis bed with no heart or brain"
                : "patient survived with no heart or brain on a plain table");

            if (survives)
            {
                Assert.That(entities.GetComponent<DamageableComponent>(patient).TotalDamage.Float(), Is.Zero,
                    "organ-loss damage got through stasis");

                // Normal damage still gets through.
                entities.System<DamageableSystem>().TryChangeDamage(patient, new DamageSpecifier { DamageDict = { ["Blunt"] = 10 } });
                Assert.That(entities.GetComponent<DamageableComponent>(patient).TotalDamage.Float(), Is.GreaterThan(0f),
                    "stasis blocked ordinary damage");
            }
        });

        await pair.CleanReturnAsync();
    }
}
