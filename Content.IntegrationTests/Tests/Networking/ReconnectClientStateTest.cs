using System.Linq;
using Content.Client.Gameplay;
using Content.Server.GameTicking;
using Content.Shared.CCVar;
using Robust.Client.Console;
using Robust.Client.State;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;

namespace Content.IntegrationTests.Tests.Networking;

[TestFixture]
public sealed class ReconnectClientStateTest
{
    [Test]
    public async Task ReconnectReturnsToGameplay()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            InLobby = true,
            Map = "CommonWealth",
            Dirty = true
        });
        var serverEntities = pair.Server.ResolveDependency<IEntityManager>();
        var serverConfig = pair.Server.ResolveDependency<IConfigurationManager>();
        var serverConsole = pair.Server.ResolveDependency<Robust.Server.Console.IServerConsoleHost>();
        var clientConsole = pair.Client.ResolveDependency<IClientConsoleHost>();
        var clientNetwork = pair.Client.ResolveDependency<IClientNetManager>();
        var states = pair.Client.ResolveDependency<IStateManager>();
        var players = pair.Server.ResolveDependency<Robust.Server.Player.IPlayerManager>();
        var username = players.Sessions.Single().Name;
        await pair.Server.WaitPost(() =>
        {
            serverConfig.SetCVar(CCVars.GameMap, "CommonWealth");
            serverConsole.ExecuteCommand("cvar game.map_rotation false");
            serverConsole.ExecuteCommand("setgamepreset Sandbox");
            serverEntities.System<GameTicker>().StartRound();
        });
        await pair.RunTicksSync(20);
        await pair.Client.WaitPost(() => clientConsole.ExecuteCommand("observe"));
        await pair.RunTicksSync(20);
        await pair.Client.WaitAssertion(() => Assert.That(states.CurrentState, Is.TypeOf<GameplayState>()));
        var commands = new[] { "raid", "group", "war", "warjoin", "forcewar", "surrender" };
        for (var i = 0; i < 3; i++)
        {
            await pair.Client.WaitPost(() => clientConsole.ExecuteCommand("disconnect"));
            await pair.RunTicksSync(10);
            await pair.Client.WaitAssertion(() =>
            {
                foreach (var command in commands)
                    Assert.That(clientConsole.AvailableCommands.ContainsKey(command), Is.False, $"{command} remained registered after disconnect");
            });
            pair.Client.SetConnectTarget(pair.Server);
            await pair.Client.WaitPost(() => clientNetwork.ClientConnect(null!, 0, username));
            await pair.RunTicksSync(30);
            await pair.Client.WaitAssertion(() =>
            {
                Assert.That(states.CurrentState, Is.TypeOf<GameplayState>(), $"Reconnect {i + 1} stayed on the connection screen");
                foreach (var command in commands)
                    Assert.That(clientConsole.AvailableCommands.ContainsKey(command), Is.True, $"{command} was not restored after reconnect");
            });
        }
        await pair.CleanReturnAsync();
    }
}
