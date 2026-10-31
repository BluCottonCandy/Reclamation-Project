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

    [Test]
    public void MultipleTensRefundPointsWithoutChangingOtherStats()
    {
        var old = new SpecialProfile { Strength = 10, Perception = 10, Endurance = 4, Charisma = 4, Intelligence = 4, Agility = 4, Luck = 4 };
        Assert.That(old.Total, Is.EqualTo(SpecialProfile.MaxTotal));
        Assert.That(old.IsValid, Is.False);
        var migrated = SpecialProfile.EnsureValid(old);
        Assert.That(migrated.Strength, Is.EqualTo(9));
        Assert.That(migrated.Perception, Is.EqualTo(9));
        Assert.That(migrated.Endurance, Is.EqualTo(4));
        Assert.That(migrated.Charisma, Is.EqualTo(4));
        Assert.That(migrated.Intelligence, Is.EqualTo(4));
        Assert.That(migrated.Agility, Is.EqualTo(4));
        Assert.That(migrated.Luck, Is.EqualTo(4));
        Assert.That(migrated.AvailablePoints, Is.EqualTo(old.AvailablePoints + 2));
        Assert.That(migrated.IsValid, Is.True);
        Assert.That(old.Strength, Is.EqualTo(10), "Migration must not mutate the source object.");
        var selected = migrated.With(SpecialStat.Strength, 10);
        Assert.That(selected.IsValid, Is.True);
        Assert.That(selected.GetMaximum(SpecialStat.Perception), Is.EqualTo(9));
        Assert.That(selected.With(SpecialStat.Perception, 10).IsValid, Is.False);
        Assert.That(SpecialProfile.EnsureValid(migrated).MemberwiseEquals(migrated), Is.True);
    }

    [Test]
    public void EveryStatCanBeTheSingleTen()
    {
        foreach (var selected in SpecialStats.All)
        {
            var profile = SpecialProfile.Default().With(selected, 10);
            Assert.That(profile.IsValid, Is.True);
            Assert.That(SpecialProfile.EnsureValid(profile).MemberwiseEquals(profile), Is.True);
            foreach (var stat in SpecialStats.All)
                Assert.That(profile.GetMaximum(stat), Is.EqualTo(stat == selected ? 10 : 9));
        }
    }
}
