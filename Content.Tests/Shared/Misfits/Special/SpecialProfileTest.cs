using Content.Shared._Misfits.Special;
using NUnit.Framework;

namespace Content.Tests.Shared.Misfits.Special;

[TestFixture]
[TestOf(typeof(SpecialProfile))]
public sealed class SpecialProfileTest
{
    [Test]
    public void DefaultProfileStartsAtFiveWithFivePointsAvailable()
    {
        var profile = SpecialProfile.Default();

        Assert.That(profile.Strength, Is.EqualTo(5));
        Assert.That(profile.Perception, Is.EqualTo(5));
        Assert.That(profile.Endurance, Is.EqualTo(5));
        Assert.That(profile.Charisma, Is.EqualTo(5));
        Assert.That(profile.Intelligence, Is.EqualTo(5));
        Assert.That(profile.Agility, Is.EqualTo(5));
        Assert.That(profile.Luck, Is.EqualTo(5));
        Assert.That(profile.AvailablePoints, Is.EqualTo(5));
        Assert.That(profile.IsValid, Is.True);
    }

    [Test]
    public void LoweringStatsRefundsPointsAndRaisingStatsSpendsThem()
    {
        var profile = SpecialProfile.Default()
            .With(SpecialStat.Strength, 4)
            .With(SpecialStat.Luck, 7);

        Assert.That(profile.Strength, Is.EqualTo(4));
        Assert.That(profile.Luck, Is.EqualTo(7));
        Assert.That(profile.AvailablePoints, Is.EqualTo(4));
        Assert.That(profile.IsValid, Is.True);
    }

    [Test]
    public void OverspentProfileIsInvalid()
    {
        var profile = new SpecialProfile
        {
            Strength = 10,
            Perception = 10,
            Endurance = 10,
            Charisma = 10,
            Intelligence = 5,
            Agility = 5,
            Luck = 5,
        };

        Assert.That(profile.Total, Is.GreaterThan(SpecialProfile.MaxTotal));
        Assert.That(profile.IsValid, Is.False);
    }

    [Test]
    public void EnsureValidResetsInvalidProfile()
    {
        var profile = new SpecialProfile
        {
            Strength = 0,
            Perception = 10,
            Endurance = 10,
            Charisma = 10,
            Intelligence = 10,
            Agility = 10,
            Luck = 10,
        };

        var sanitized = SpecialProfile.EnsureValid(profile);

        Assert.That(sanitized.Strength, Is.EqualTo(SpecialProfile.DefaultValue));
        Assert.That(sanitized.Total, Is.EqualTo(SpecialProfile.DefaultTotal));
        Assert.That(sanitized.AvailablePoints, Is.EqualTo(SpecialProfile.BonusPoints));
    }
}
