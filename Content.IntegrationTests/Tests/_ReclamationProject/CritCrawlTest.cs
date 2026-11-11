using Content.Shared.ActionBlocker;
using Content.Shared.Damage;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Roles;
using Content.Shared.Station;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._ReclamationProject;

/// <summary>
/// Coursers and the Institute Director crawl in crit at the normal lying-down speed; everyone else
/// gets the much slower crit crawl.
/// </summary>
[TestFixture]
public sealed class CritCrawlTest
{
    // Human crit crawl sprint speed is about 0.14; lying-down speed is about 0.5.
    [TestCase("MobHuman", null, false)]
    [TestCase("MobCourser", null, true)]
    [TestCase("MobHuman", "InstituteDirector", true)]
    [TestCase("MobCourser", "InstituteCourser", true)]
    public async Task CrawlSpeedInCrit(string mob, string jobId, bool steady)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        var protos = pair.Server.ResolveDependency<IPrototypeManager>();
        var session = pair.Server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);
        EntityUid uid = default;

        await pair.Server.WaitPost(() =>
        {
            uid = entities.SpawnEntity(mob, map.GridCoords);
            if (jobId != null)
            {
                var job = protos.Index<JobPrototype>(jobId);
                if (job.StartingGear != null)
                    entities.System<SharedStationSpawningSystem>().EquipStartingGear(uid, job.StartingGear);
                foreach (var special in job.Special)
                    special.AfterEquip(uid);
            }
            pair.Server.PlayerMan.SetAttachedEntity(session, uid);
        });
        await pair.RunTicksSync(30);

        await pair.Server.WaitPost(() =>
        {
            entities.System<MobThresholdSystem>().TryGetThresholdForState(uid, Content.Shared.Mobs.MobState.Critical, out var crit);
            entities.System<DamageableSystem>().TryChangeDamage(uid,
                new DamageSpecifier { DamageDict = { ["Blunt"] = crit!.Value + 5 } }, ignoreResistances: true,
                targetPart: Content.Shared._Shitmed.Targeting.TargetBodyPart.Torso);
        });
        await pair.RunTicksSync(60);

        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(entities.System<MobStateSystem>().IsCritical(uid), "not in crit");
            Assert.That(entities.System<ActionBlockerSystem>().CanMove(uid), "can't move in crit");
            var sprint = entities.GetComponent<MovementSpeedModifierComponent>(uid).CurrentSprintSpeed;
            if (steady)
                Assert.That(sprint, Is.GreaterThan(0.4f), "crawls no faster than a normal crit crawl");
            else
                Assert.That(sprint, Is.LessThan(0.2f), "normal humans shouldn't get the faster crawl");
        });

        await pair.Server.WaitPost(() =>
        {
            pair.Server.PlayerMan.SetAttachedEntity(session, null);
            entities.DeleteEntity(uid);
        });
        await pair.CleanReturnAsync();
    }
}
