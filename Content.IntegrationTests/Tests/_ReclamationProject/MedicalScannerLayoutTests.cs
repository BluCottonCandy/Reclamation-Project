using System.Linq;
using System.Numerics;
using Content.Client.HealthAnalyzer.UI;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture, NonParallelizable]
public sealed class MedicalScannerLayoutTests
{
    [Test]
    public async Task LongInjuryReportScrollsAndWindowCanGrowPastOldHeightLimit()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            using var window = new HealthAnalyzerWindow();
            var report = window.Panel.FindControl<RichTextLabel>("TraumaReport");
            report.SetMessage(string.Join("\n", Enumerable.Repeat(
                "Left arm: 75% to failure. At risk; seek surgical treatment before the limb fails.", 100)));
            Assert.That(window.Resizable, Is.True);
            window.Measure(new Vector2(1280, 900));
            Assert.That(window.DesiredSize.Y, Is.LessThanOrEqualTo(520));
            foreach (var size in new[] { new Vector2(380, 300), new Vector2(700, 750) })
            {
                window.SetSize = size;
                window.InvalidateMeasure();
                window.Measure(new Vector2(1280, 900));
                window.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
                Assert.That(window.Size, Is.EqualTo(size));
                Assert.That(report.Size.X, Is.LessThanOrEqualTo(window.Panel.Size.X));
                Assert.That(report.Size.Y, Is.GreaterThan(window.Panel.Size.Y),
                    "Long report content must stay in the scroll area, not dictate the window height.");
            }
        });
        await pair.CleanReturnAsync();
    }
}
