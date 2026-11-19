using System.Collections.Generic;
using Content.Client.Lobby;
using Content.Client.Lobby.UI;
using Content.Shared.Preferences;
using Robust.Client.State;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;

namespace Content.IntegrationTests.Tests.Lobby;

[TestFixture]
public sealed class BlacksmithPerkTest
{
    [Test]
    public async Task DrawbackPointsCanPayForBlacksmith()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true, Dirty = true });
        var client = pair.Client;
        var states = client.ResolveDependency<IStateManager>();
        await PoolManager.WaitUntil(client, () => states.CurrentState is LobbyState { Lobby: not null }, 600);
        HumanoidProfileEditor editor = null!;
        TalentTreeNode blacksmith = null!;
        await client.WaitAssertion(() =>
        {
            var lobby = (LobbyState) states.CurrentState;
            editor = Descendants(lobby.Lobby!.CharacterSetupState).OfType<HumanoidProfileEditor>().Single();
            var profile = HumanoidCharacterProfile.DefaultWithSpecies("Human");
            editor.SetProfile(profile, 0);
            editor.UpdateTraits();
            TalentTreeNode Blacksmith() => Descendants(editor.TalentTree).OfType<TalentTreeNode>()
                .Single(n => n.TraitId == "ReclamationBlacksmith");

            Assert.That(Blacksmith().Disabled, Is.True, "10 starting points cannot buy a 15-point perk");
            editor.SetProfile(profile.WithTraitPreference("Unstable", true), 0);
            editor.UpdateTraits();
            blacksmith = Blacksmith();
            Assert.That(blacksmith.Disabled, Is.False, "the +5 drawback supplies the missing points");
            blacksmith.Mode = BaseButton.ActionMode.Press;
        });

        await client.DoGuiEvent(blacksmith, new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick,
            BoundKeyState.Down, default, default, default, default));
        await client.WaitAssertion(() =>
        {
            Assert.That(editor.Profile!.TraitPreferences, Does.Contain("ReclamationBlacksmith"));
            Assert.That(editor.FindControl<ProgressBar>("TraitPointsBar").Value, Is.Zero);
        });
        await pair.CleanReturnAsync();
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (var child in parent.Children)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }
}
