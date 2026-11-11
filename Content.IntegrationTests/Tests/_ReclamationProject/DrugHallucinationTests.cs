using System;
using System.Linq;
using System.Threading.Tasks;
using Content.Server._ReclamationProject.Hallucinations;
using Content.Shared._ReclamationProject.Hallucinations;
using Content.Shared.Traits;
using Content.Shared.Chemistry.Components;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture]
public sealed class DrugHallucinationTests
{
    [Test]
    public async Task VisualShellsHaveNoGameplayComponentsAndDoNotDependOnSpeechTraits()
    {
        await using var pair = await PoolManager.GetServerClient();
        await pair.Server.WaitAssertion(() => Verify(pair.Server.ResolveDependency<IEntityManager>(), pair.Server.ResolveDependency<IPrototypeManager>()));
        await pair.Client.WaitAssertion(() =>
        {
            var prototypes = pair.Client.ResolveDependency<IPrototypeManager>();
            foreach (var id in prototypes.EnumeratePrototypes<EntityPrototype>().Where(p => p.ID.StartsWith("ReclamationPhantom", StringComparison.Ordinal)).Select(p => p.ID))
                Assert.That(prototypes.Index<EntityPrototype>(id).Components.ContainsKey("Sprite"), Is.True, id + " lacks client visuals.");
        });
        await pair.CleanReturnAsync();
    }

    public static void Verify(IEntityManager em, IPrototypeManager prototypes)
    {
        void Check(bool condition, string reason)
        {
            if (!condition) throw new InvalidOperationException(reason);
        }
        Check(HallucinationRules.MinimumMinutes == 5 && HallucinationRules.MaximumMinutes == 20,
            "Recurring enemy attempts must be 5-20 minutes apart.");
        Check(HallucinationRules.AppetiteCooldown == TimeSpan.FromMinutes(10), "Food/drink cooldown must be ten minutes.");
        for (var i = 0; i < 1000; i++)
        {
            var roll = i / 1000f;
            Check(HallucinationRules.Occurs(true, roll), "A high must not fail a probability roll.");
            Check(HallucinationRules.Occurs(false, roll) == (i < 800), "Unstable must use an 80 percent roll.");
        }
        Check(HallucinationRules.WeatherDuration == TimeSpan.FromSeconds(20), "Private weather must expire after twenty seconds.");
        Check(prototypes.EnumeratePrototypes<Content.Shared.Weather.WeatherPrototype>().Any(w=>w.Sprite!=null), "No existing weather visuals available.");
        var trait = prototypes.Index<TraitPrototype>("Unstable");
        Check(trait.Points == 3 && trait.Category.Id == "Mental", "Unstable must be a +3 Mental drawback.");
        foreach (var id in prototypes.EnumeratePrototypes<EntityPrototype>().Where(p => p.ID.StartsWith("ReclamationPhantom", StringComparison.Ordinal)).Select(p => p.ID))
        {
            var prototype = prototypes.Index<EntityPrototype>(id);
            foreach (var dangerous in new[] { "MeleeWeapon", "Gun", "Damageable", "HTN", "Physics", "Fixtures", "Inventory", "Body", "NpcFactionMember", "Clickable", "InteractionOutline", "Item", "Food", "Drink", "SolutionContainerManager" })
                Check(!prototype.Components.ContainsKey(dangerous), id + " has gameplay component " + dangerous);
        }
        var user = em.SpawnEntity(null, Robust.Shared.Map.MapCoordinates.Nullspace);
        try
        {
            var system = em.System<DrugHighSystem>();
            var solution = new Solution();
            solution.AddReagent("DamageResistMixture", FixedPoint2.New(5));
            Check(system.RecordDose(user, solution), "A new Med-X dose was not counted.");
            Check(!system.RecordDose(user, solution), "Unchanged solution rerolled the chance.");
            solution.RemoveReagent("DamageResistMixture", FixedPoint2.New(1));
            Check(!system.RecordDose(user, solution), "Metabolism rerolled the chance.");
            solution.AddReagent("DamageResistMixture", FixedPoint2.New(5));
            Check(system.RecordDose(user, solution) && em.GetComponent<DrugDoseTrackerComponent>(user).Doses == 2,
                "A second dose during the same high must count once.");
            var freshBody = em.SpawnEntity(null, Robust.Shared.Map.MapCoordinates.Nullspace);
            Check(!em.HasComponent<UnstableComponent>(freshBody) && !em.HasComponent<DrugDoseTrackerComponent>(freshBody),
                "Acquired instability leaked to a fresh body.");
            em.DeleteEntity(freshBody);
            system.Refresh(user, new[] { "drug-ambience-radaway-1" });
            Check(!em.HasComponent<DrugHighComponent>(user), "Ordinary treatment caused a high.");
            system.Refresh(user, new[] { "drug-ambience-damage-resist-mixture-1" });
            Check(em.HasComponent<DrugHighComponent>(user), "Med-X did not trigger hallucinations without Outspoken.");
            var high = em.GetComponent<DrugHighComponent>(user);
            var before = high.Expires;
            system.Refresh(user, new[] { "drug-ambience-movespeed-mixture-1" });
            Check(ReferenceEquals(high, em.GetComponent<DrugHighComponent>(user)) && high.Expires >= before,
                "Stacked chems must refresh the same high rather than create another source.");
        }
        finally { em.DeleteEntity(user); }
    }
}
