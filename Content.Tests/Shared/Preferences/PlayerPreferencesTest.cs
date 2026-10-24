using System.Collections.Generic;
using System.Linq;
using Content.Shared.Preferences;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared.Preferences;

[TestFixture]
public sealed class PlayerPreferencesTest
{
    [TestCase(-1)]
    [TestCase(2)]
    [TestCase(100)]
    public void MissingSelectionPreservesExistingCharacters(int selected)
    {
        var first = new HumanoidCharacterProfile { Name = "First survivor" };
        var second = new HumanoidCharacterProfile { Name = "Second survivor" };
        var prefs = new PlayerPreferences(new Dictionary<int, ICharacterProfile>
        {
            [7] = second,
            [3] = first,
        }, selected, Color.Red, true);

        Assert.Multiple(() =>
        {
            Assert.That(prefs.SelectedCharacterIndex, Is.EqualTo(3));
            Assert.That(prefs.SelectedCharacter, Is.SameAs(first));
            Assert.That(prefs.GetProfile(7), Is.SameAs(second));
            Assert.That(prefs.Characters.Count, Is.EqualTo(2));
            Assert.That(prefs.AnonymousRoundEndReport, Is.True);
            Assert.That(prefs.AdminOOCColor, Is.EqualTo(Color.Red));
        });
    }

    [Test]
    public void ExistingSelectionIsUnchanged()
    {
        var profiles = new Dictionary<int, ICharacterProfile>
        {
            [0] = new HumanoidCharacterProfile(),
            [2] = new HumanoidCharacterProfile(),
        };
        var prefs = new PlayerPreferences(profiles, 2, Color.Red);
        Assert.That(prefs.SelectedCharacter, Is.SameAs(profiles[2]));
        Assert.That(prefs.SelectedCharacterIndex, Is.EqualTo(2));
    }

    [Test]
    public void DeletingSelectedCharacterLeavesUsableSelection()
    {
        var profiles = new Dictionary<int, ICharacterProfile>
        {
            [0] = new HumanoidCharacterProfile(),
            [2] = new HumanoidCharacterProfile(),
        };
        var before = new PlayerPreferences(profiles, 2, Color.Red);
        var after = new PlayerPreferences(before.Characters.Where(p => p.Key != 2),
            before.SelectedCharacterIndex, before.AdminOOCColor);

        Assert.That(after.SelectedCharacter, Is.SameAs(profiles[0]));
        Assert.That(after.SelectedCharacterIndex, Is.Zero);
    }

    [Test]
    public void EmptyPreferencesRecoverDefaultCharacter()
    {
        var prefs = new PlayerPreferences(new Dictionary<int, ICharacterProfile>(), 2, Color.Red);
        Assert.That(prefs.SelectedCharacter, Is.TypeOf<HumanoidCharacterProfile>());
        Assert.That(prefs.Characters.Keys, Is.EquivalentTo(new[] { 0 }));
    }
}
