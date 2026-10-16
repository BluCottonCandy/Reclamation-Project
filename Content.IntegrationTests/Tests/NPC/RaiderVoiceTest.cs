using System;
using Content.Server.NPC.Systems;
using Content.Shared.Audio;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Sound.Components;
using Robust.Client.ResourceManagement;
using Robust.Shared.Audio;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests.NPC;

[TestFixture]
public sealed class RaiderVoiceTest
{
    [Test]
    public async Task RaiderVoicesSleepAndStopOnDeath()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
        var timing = pair.Server.ResolveDependency<IGameTiming>();
        var cache = pair.Client.ResolveDependency<IResourceCache>();
        var collection = prototypes.Index<SoundCollectionPrototype>("ReclamationRaiderVoice");

        await pair.Client.WaitAssertion(() =>
        {
            Assert.That(collection.PickFiles.Count, Is.EqualTo(5));
            foreach (var file in collection.PickFiles)
                Assert.That(cache.TryGetResource<AudioResource>(file, out _), Is.True, file.ToString());
        });

        await pair.Server.WaitAssertion(() =>
        {
            var npc = entities.System<NPCSystem>();
            var states = entities.System<MobStateSystem>();
            foreach (var prototype in new[] { "N14MobRaiderPsycho", "N14MobRaiderPsychoRanged",
                         "N14MobRaiderBoss", "N14MobRaiderHunter", "N14MobRaiderSkrimisher" })
            {
                var raider = entities.SpawnEntity(prototype, map.GridCoords);
                var voice = entities.GetComponent<SpamEmitSoundComponent>(raider);
                Assert.That(entities.HasComponent<SoundWhileAliveComponent>(raider), Is.True);
                Assert.That(voice.Enabled, Is.False, "A distant raider must start silent.");

                npc.WakeNPC(raider);
                Assert.That(voice.Enabled, Is.True);
                Assert.That(voice.NextSound - timing.CurTime, Is.EqualTo(TimeSpan.FromSeconds(10)));
                Assert.That(voice.MinInterval, Is.EqualTo(TimeSpan.FromSeconds(10)));
                Assert.That(voice.MaxInterval, Is.EqualTo(TimeSpan.FromSeconds(10)));

                npc.SleepNPC(raider);
                Assert.That(voice.Enabled, Is.False, "Leaving the wake radius must silence raiders.");
                npc.WakeNPC(raider);
                states.ChangeMobState(raider, MobState.Dead);
                Assert.That(voice.Enabled, Is.False, "Dead raiders must not speak.");
                Assert.That(npc.IsAwake(raider), Is.False);
                entities.DeleteEntity(raider);
            }
        });
        await pair.CleanReturnAsync();
    }
}
