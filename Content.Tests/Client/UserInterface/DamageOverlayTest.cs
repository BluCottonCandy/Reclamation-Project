using System.Numerics;
using Content.Client.UserInterface.Systems.DamageOverlays.Overlays;
using NUnit.Framework;

namespace Content.Tests.Client.UserInterface;

[TestFixture]
public sealed class DamageOverlayTest
{
    [TestCase(640, 480)]
    [TestCase(1920, 1080)]
    [TestCase(3440, 1440)]
    public void InjuryGraduallyClosesInFromTheCorners(int width, int height)
    {
        var viewport = new Vector2(width, height);
        var healthy = DamageOverlay.GetInjuryGradient(viewport, 0f);
        var scratch = DamageOverlay.GetInjuryGradient(viewport, 0.05f);
        Assert.That(healthy.Opacity, Is.Zero);
        Assert.That(scratch.Opacity, Is.InRange(0.01f, 0.05f));
        Assert.That(scratch.InnerRadius, Is.GreaterThan(height * 0.5f));
        Assert.That(scratch.InnerRadius, Is.LessThan(viewport.Length() * 0.5f));

        var previous = scratch;
        foreach (var level in new[] { 0.16f, 0.35f, 0.65f, 0.9f, 1f })
        {
            var current = DamageOverlay.GetInjuryGradient(viewport, level);
            Assert.That(current.Opacity, Is.GreaterThan(previous.Opacity));
            Assert.That(current.InnerRadius, Is.LessThan(previous.InnerRadius));
            Assert.That(current.OuterRadius, Is.LessThan(previous.OuterRadius));
            Assert.That(current.OuterRadius, Is.GreaterThan(current.InnerRadius));
            previous = current;
        }
        Assert.That(previous.Opacity, Is.EqualTo(0.8f).Within(0.001f));
        Assert.That(previous.InnerRadius, Is.EqualTo(height * 0.2f).Within(0.001f));
    }
}
