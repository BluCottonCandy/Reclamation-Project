using System;
using System.Numerics;
using Content.Server._ReclamationProject.Symptoms;
using Content.Shared._ReclamationProject.Symptoms;
using Content.Shared.Traits;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;
using System.Threading.Tasks;
namespace Content.IntegrationTests.Tests._ReclamationProject;
[TestFixture]
public sealed class OutspokenPerkTests
{
    [Test]
    public async Task PerkGrantsOnePointAndOptsIntoSymptoms()
    {
        await using var pair = await PoolManager.GetServerClient();
        await pair.Server.WaitAssertion(() => Verify(pair.Server.ResolveDependency<IEntityManager>(), pair.Server.ResolveDependency<IPrototypeManager>(), pair.Server.ResolveDependency<IComponentFactory>(), pair.Server.ResolveDependency<ISerializationManager>()));
        await pair.CleanReturnAsync();
    }
    public static void Verify(IEntityManager em, IPrototypeManager prototypes, IComponentFactory factory, ISerializationManager serialization)
    {
        void Check(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason); }
        var trait = prototypes.Index<TraitPrototype>("ReclamationOutspoken");
        Check(trait.Points == 1, "Outspoken must grant +1 point, not cost a point.");
        Check(trait.Category.ToString() == "Speech", "Outspoken is not in Speech.");
        Check(SymptomSpeechSystem.GlobalCooldown == TimeSpan.FromMinutes(2), "Speech cooldown changed.");
        var map = em.System<SharedMapSystem>().CreateMap();
        try
        {
            var user = em.SpawnEntity("MobHuman", new EntityCoordinates(map, Vector2.Zero));
            var symptoms = em.System<SymptomSpeechSystem>();
            Check(!em.HasComponent<OutspokenComponent>(user), "Outspoken was granted by default.");
            Check(!symptoms.TrySpeak(user, "SymptomBloodLoss"), "Unselected symptom speech was allowed.");
            foreach (var function in trait.Functions) function.OnPlayerSpawn(user, factory, em, serialization);
            Check(em.HasComponent<OutspokenComponent>(user), "Selecting Outspoken did not add its marker.");
            Check(!em.HasComponent<SymptomSpeechComponent>(user), "Selecting the perk unnecessarily started a speech cooldown.");
        }
        finally { em.DeleteEntity(map); }
    }
}
