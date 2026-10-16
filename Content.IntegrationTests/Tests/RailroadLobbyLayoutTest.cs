using System.Numerics;
using Content.Client.Lobby.UI;
using Content.Client.Credits;
using Content.Client.Message;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests;

[TestFixture]
public sealed class RailroadLobbyLayoutTest
{
    [Test]
    public async Task LongLobbyCreditsKeepChatOnScreen()
    {
        await using var pair = await PoolManager.GetServerClient();
        await pair.Client.WaitAssertion(() =>
        {
            using var credits = new CreditsWindow();
            Assert.That(credits.CreditsTabs.ChildCount, Is.EqualTo(2));
            using var lobby = new LobbyGui();
            lobby.ServerName.Text = "Reclamation - Railroad Common Wealth Local Test";
            lobby.LobbySong.SetMarkup("Lobby music: A long wasteland radio song credit with several performers");
            lobby.LobbyBackground.SetMarkup("Background: A long Commonwealth scenery credit with multiple contributors");
            lobby.MapCredit.SetMarkup("Map: Common Wealth (Railroad Test), Reclamation Project mapping team");
            foreach (var size in new[] { new Vector2(1280, 720), new Vector2(1920, 1080) })
            {
                lobby.InvalidateMeasure();
                lobby.Measure(size);
                lobby.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
                Assert.That(lobby.Chat.GlobalRect.Right, Is.LessThanOrEqualTo(size.X));
                Assert.That(lobby.Chat.GlobalRect.Left, Is.GreaterThanOrEqualTo(0));
                Assert.That(lobby.Chat.Size.X, Is.GreaterThan(200));
            }
        });
        await pair.CleanReturnAsync();
    }
}
