using System;
using System.Linq;
using System.Numerics;
using Content.Server._ReclamationProject.Triggermen;
using Content.Server.Speech.Components;
using Content.Server.Speech.EntitySystems;
using Content.Shared.Language;
using Content.Server.Language;
using Content.Shared.Roles;
using Content.Shared.Traits;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;
using System.Threading.Tasks;
namespace Content.IntegrationTests.Tests._ReclamationProject;
[TestFixture]
public sealed class TriggermenItalianTests
{
    [Test]
    public async Task FactionLanguageAndDefaultAccentRespectPlayerSelections()
    {
        await using var pair = await PoolManager.GetServerClient();
        await pair.Server.WaitAssertion(() => Verify(pair.Server.ResolveDependency<IEntityManager>(), pair.Server.ResolveDependency<IPrototypeManager>(), pair.Server.ResolveDependency<IComponentFactory>(), pair.Server.ResolveDependency<ISerializationManager>()));
        await pair.CleanReturnAsync();
    }
    public static void Verify(IEntityManager em, IPrototypeManager prototypes, IComponentFactory factory, ISerializationManager serialization)
    {
        void Check(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); }
        Check(prototypes.Index<LanguagePrototype>("Latin").Name == "Italian", "Legacy language is not displayed as Italian.");
        Check(prototypes.Index<LanguagePrototype>("Latin").ChatName == "Italian", "Chat still labels the language Latin.");
        Check(prototypes.Index<TraitPrototype>("N14ItalianAccent").Points == 1, "Existing selectable accent price changed.");
        var map = em.System<SharedMapSystem>().CreateMap();
        var coords = new EntityCoordinates(map, Vector2.Zero);
        var system = em.System<TriggermenItalianSystem>();
        EntityUid Person() => em.SpawnEntity("MobHuman", coords);
        void ApplyTrait(EntityUid mob, string id)
        {
            foreach (var function in prototypes.Index<TraitPrototype>(id).Functions)
                function.OnPlayerSpawn(mob, factory, em, serialization);
        }
        try
        {
            var jobs = prototypes.EnumeratePrototypes<JobPrototype>().Where(j => j.ID == "Triggerman" || j.ID.StartsWith("Triggermen", StringComparison.Ordinal)).ToArray();
            Check(jobs.Length >= 6, "Missing Triggermen job coverage.");
            foreach (var job in jobs)
            {
                var mob = Person();
                system.ApplyJobDefaults(mob, job.ID, Array.Empty<string>());
                var languages = em.GetComponent<LanguageKnowledgeComponent>(mob);
                Check(languages.SpokenLanguages.Contains("Latin") && languages.UnderstoodLanguages.Contains("Latin"), job.ID + " lacks Italian.");
                Check(em.GetComponent<ReplacementAccentComponent>(mob).Accent.Id == "italian", job.ID + " lacks default Italian accent.");
                system.ApplyJobDefaults(mob, job.ID, Array.Empty<string>());
                Check(languages.SpokenLanguages.Count(x => x.Id == "Latin") == 1, "Repeated defaults duplicate languages.");
            }
            var other = Person();
            system.ApplyJobDefaults(other, "Wastelander", Array.Empty<string>());
            Check(!em.HasComponent<ReplacementAccentComponent>(other), "Non-Triggermen received a default accent.");
            ApplyTrait(other, "N14ItalianAccent");
            Check(em.GetComponent<ReplacementAccentComponent>(other).Accent.Id == "italian", "Other roles cannot pick Italian accent.");
            var cowboy = Person();
            ApplyTrait(cowboy, "N14CowboyAccent");
            system.ApplyJobDefaults(cowboy, "TriggermenBoss", new[] { "N14CowboyAccent" });
            Check(em.GetComponent<ReplacementAccentComponent>(cowboy).Accent.Id == "cowboy", "Selected replacement accent was overwritten.");
            Check(em.GetComponent<LanguageKnowledgeComponent>(cowboy).SpokenLanguages.Contains("Latin"), "Changing accent removed Italian language.");
            var mobster = Person();
            ApplyTrait(mobster, "N14MobsterAccent");
            system.ApplyJobDefaults(mobster, "Triggerman", new[] { "N14MobsterAccent" });
            Check(em.HasComponent<MobsterAccentComponent>(mobster) && !em.HasComponent<ReplacementAccentComponent>(mobster), "Selected custom accent stacked with Italian.");
            var speech = em.System<ReplacementAccentSystem>();
            Check(speech.ApplyReplacements("Hello, my friend. We have a deal.", "italian") == "Salve, amico mio. We have an understanding.", "Italian phrases changed unexpectedly.");
            Check(speech.ApplyReplacements("It's a gun. I am cooking.", "italian") == "It's a gun. I am cooking.", "Cartoon vowel endings remain.");
            Check(speech.ApplyReplacements("HELLO", "italian") == "SALVE", "Capitalization is not preserved.");
            Check(speech.ApplyReplacements("", "italian") == "", "Empty speech is not safe.");
        }
        finally { em.DeleteEntity(map); }
    }
}
