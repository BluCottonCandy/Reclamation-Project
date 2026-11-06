using System;
using System.Numerics;
using System.Reflection;
using Content.Client.Weapons.Melee;
using NUnit.Framework;
using Robust.Client.Animations;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture]
public sealed class WideSwingAnimationTest
{
    [TestCase(1f)]
    [TestCase(3f)]
    public void WideSwingPassesThroughAimedSide(float durationMultiplier)
    {
        var build = typeof(MeleeWeaponSystem).GetMethod("GetSlashAnimation", BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (var degrees in new[] { 0, 90, 180, 270 })
        foreach (var sweep in new[] { -200, 200 })
        {
            var aim = Angle.FromDegrees(degrees);
            var correction = Angle.FromDegrees(-135);
            var animation = (Animation) build.Invoke(null, new object[] { aim, Angle.FromDegrees(sweep), correction, durationMultiplier })!;
            var rotation = (AnimationTrackProperty) animation.AnimationTracks[0];
            var offset = (AnimationTrackProperty) animation.AnimationTracks[1];
            Assert.That(rotation.KeyFrames.Count, Is.EqualTo(4));
            Assert.That(offset.KeyFrames.Count, Is.EqualTo(4));
            var midpoint = (Vector2) offset.KeyFrames[2].Value;
            var forward = aim.RotateVec(new Vector2(0, -1));
            Assert.That(Vector2.Dot(midpoint, forward), Is.GreaterThan(.999f), "Swing must pass in front, not behind");
            var middleRotation = (Angle) rotation.KeyFrames[2].Value;
            Assert.That(Math.Abs(Angle.ShortestDistance(aim + correction, middleRotation).Theta), Is.LessThan(.0001));
            Assert.That(offset.KeyFrames[2].KeyTime + offset.KeyFrames[3].KeyTime, Is.EqualTo(.065f * durationMultiplier).Within(.0001));
        }
    }

    [Test]
    public void OrdinarySwingKeepsExistingTracks()
    {
        var build = typeof(MeleeWeaponSystem).GetMethod("GetSlashAnimation", BindingFlags.NonPublic | BindingFlags.Static)!;
        var animation = (Animation) build.Invoke(null, new object[] { Angle.Zero, Angle.FromDegrees(90), Angle.Zero, 1f })!;
        foreach (var track in animation.AnimationTracks)
            Assert.That(((AnimationTrackProperty) track).KeyFrames.Count, Is.EqualTo(3));
    }
}
