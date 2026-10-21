using System;
using Robust.Shared.Maths;
using System.Linq;
using Content.Client.Lobby.UI;
using Content.Server.Database;
using Content.Server.Station.Systems;
using Content.Shared._ReclamationProject.Consent;
using Content.Shared.Preferences;
using Content.Shared.Verbs;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;
using Robust.Shared.Network;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture, NonParallelizable]
public sealed class CharacterConsentTests
{
    [Test]
    public async Task CheckboxChoicesAreExclusiveAndCanBeCleared()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            using var control = new ConsentPreferencesControl();
            Assert.That(control.TortureYes.Pressed || control.TortureNo.Pressed || control.SlaveryYes.Pressed || control.SlaveryNo.Pressed, Is.False);
            control.Select(true, ConsentPreference.Consents, true);
            Assert.That(control.TortureYes.Pressed && !control.TortureNo.Pressed, Is.True);
            control.Select(true, ConsentPreference.DoesNotConsent, true);
            Assert.That(!control.TortureYes.Pressed && control.TortureNo.Pressed, Is.True);
            control.Select(false, ConsentPreference.Consents, true);
            control.Select(false, ConsentPreference.DoesNotConsent, true);
            Assert.That(!control.SlaveryYes.Pressed && control.SlaveryNo.Pressed, Is.True);
            Assert.That(control.Torture, Is.EqualTo(ConsentPreference.DoesNotConsent));
            control.Select(false, ConsentPreference.DoesNotConsent, false);
            Assert.That(control.Slavery, Is.EqualTo(ConsentPreference.Unspecified));
            Assert.That(control.SlaveryYes.Pressed || control.SlaveryNo.Pressed, Is.False);
            control.SetPreferences(ConsentPreference.Consents, ConsentPreference.Unspecified);
            Assert.That(control.TortureYes.Pressed && !control.TortureNo.Pressed, Is.True, "Changing characters refreshes checkbox state.");
            Assert.That(control.TortureYes.Label.FontColorOverride, Is.EqualTo(Color.Yellow));
            Assert.That(control.SlaveryYes.Label.FontColorOverride, Is.EqualTo(Color.Yellow));
            Assert.That(control.TortureNo.Label.FontColorOverride, Is.EqualTo(Color.Red));
            Assert.That(control.SlaveryNo.Label.FontColorOverride, Is.EqualTo(Color.Red));
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(ConsentPreference.Consents, ConsentPreference.DoesNotConsent)]
    [TestCase(ConsentPreference.DoesNotConsent, ConsentPreference.Consents)]
    [TestCase(ConsentPreference.Unspecified, ConsentPreference.Unspecified)]
    public async Task CharacterSlotsSaveConsentAndMigrationsMatchTheModel(ConsentPreference torture, ConsentPreference slavery)
    {
        await using var pair = await PoolManager.GetServerClient();
        var cfg = pair.Server.ResolveDependency<IConfigurationManager>();
        var log = pair.Server.ResolveDependency<ILogManager>().GetSawmill("db.ops");
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var builder = new DbContextOptionsBuilder<SqliteServerDbContext>().UseSqlite(connection);
        var db = new ServerDbSqlite(() => builder.Options, true, cfg, true, log);
        var user = new NetUserId(Guid.NewGuid());
        var original = new HumanoidCharacterProfile().WithConsent(torture, slavery);
        Assert.That(original.Clone().TortureConsent, Is.EqualTo(torture));
        Assert.That(original.WithName("Consent Test").SlaveryConsent, Is.EqualTo(slavery));
        await db.InitPrefsAsync(user, new HumanoidCharacterProfile());
        await db.SaveCharacterSlotAsync(user, original, 1);
        var prefs = await db.GetPlayerPreferencesAsync(user);
        var saved = (HumanoidCharacterProfile) prefs!.Characters[1];
        Assert.That(saved.TortureConsent, Is.EqualTo(torture));
        Assert.That(saved.SlaveryConsent, Is.EqualTo(slavery));
        var unset = (HumanoidCharacterProfile) prefs.Characters[0];
        Assert.That(unset.TortureConsent, Is.EqualTo(ConsentPreference.Unspecified));
        Assert.That(unset.SlaveryConsent, Is.EqualTo(ConsentPreference.Unspecified));
        await db.SaveCharacterSlotAsync(user, saved.WithConsent(ConsentPreference.Unspecified, ConsentPreference.Unspecified), 1);
        saved = (HumanoidCharacterProfile) (await db.GetPlayerPreferencesAsync(user))!.Characters[1];
        Assert.That(saved.TortureConsent, Is.EqualTo(ConsentPreference.Unspecified));
        Assert.That(saved.SlaveryConsent, Is.EqualTo(ConsentPreference.Unspecified));
        Assert.That(await db.HasPendingModelChanges(), Is.False);
        using var pg = new PostgresServerDbContext(new DbContextOptionsBuilder<PostgresServerDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused").Options);
        Assert.That(pg.Database.HasPendingModelChanges(), Is.False);
        Assert.That(HumanoidCharacterProfile.ValidateConsent((ConsentPreference) 257), Is.EqualTo(ConsentPreference.Unspecified));
        await pair.CleanReturnAsync();
    }

    [TestCase(ConsentPreference.Consents, ConsentPreference.Unspecified, "YELLOW", "Consent to being tortured", "slavery")]
    [TestCase(ConsentPreference.DoesNotConsent, ConsentPreference.Unspecified, "RED", "Does not consent to torture", "slavery")]
    [TestCase(ConsentPreference.Unspecified, ConsentPreference.Consents, "YELLOW", "Consent to slavery", "torture")]
    [TestCase(ConsentPreference.Unspecified, ConsentPreference.DoesNotConsent, "RED", "Does not consent to slavery", "torture")]
    public async Task SpawnedCharactersExposeOnlySelectedChoicesInTheRightColor(ConsentPreference torture, ConsentPreference slavery, string color, string expected, string absent)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid human = default;
        NetEntity net = default;
        await pair.Server.WaitAssertion(() =>
        {
            var profile = HumanoidCharacterProfile.DefaultWithSpecies().WithConsent(torture, slavery);
            human = em.System<StationSpawningSystem>().SpawnPlayerMob(map.GridCoords, null, profile, null);
            net = em.GetNetEntity(human);
            var consent = em.GetComponent<CharacterConsentComponent>(human);
            var message = CharacterConsentSystem.Describe(consent).ToMarkup();
            Assert.That(message, Does.Contain(expected));
            Assert.That(message.ToUpperInvariant(), Does.Contain(color));
            Assert.That(message.ToLowerInvariant(), Does.Not.Contain(absent));
            var verbs = new GetVerbsEvent<ExamineVerb>(human, human, null, null, false, false, true, new());
            em.EventBus.RaiseLocalEvent(human, verbs);
            Assert.That(verbs.Verbs.Count(verb => verb.Text == "Consent"), Is.EqualTo(1));
        });
        await pair.RunTicksSync(20);
        await pair.Client.WaitAssertion(() =>
        {
            var client = pair.Client.ResolveDependency<IEntityManager>();
            var consent = client.GetComponent<CharacterConsentComponent>(client.GetEntity(net));
            Assert.That(consent.Torture, Is.EqualTo(torture));
            Assert.That(consent.Slavery, Is.EqualTo(slavery));
        });
        await pair.Server.WaitAssertion(() =>
        {
            em.System<CharacterConsentSystem>().ApplyPreferences(human, new HumanoidCharacterProfile());
            Assert.That(CharacterConsentSystem.Describe(em.GetComponent<CharacterConsentComponent>(human)).IsEmpty, Is.True);
            var verbs = new GetVerbsEvent<ExamineVerb>(human, human, null, null, false, false, true, new());
            em.EventBus.RaiseLocalEvent(human, verbs);
            Assert.That(verbs.Verbs.Any(verb => verb.Text == "Consent"), Is.False);
            em.DeleteEntity(human);
        });
        await pair.CleanReturnAsync();
    }
}
