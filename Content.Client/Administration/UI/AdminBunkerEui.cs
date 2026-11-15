using System.Linq;
using System.Numerics;
using Content.Client.Eui;
using Content.Shared._ReclamationProject.Bunkers;
using Content.Shared.Eui;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;

namespace Content.Client.Administration.UI;

public sealed class AdminBunkerEui : BaseEui
{
    private readonly DefaultWindow _window = new() { Title = "Bunker", MinSize = new Vector2(650, 450), SetSize = new Vector2(780, 560) };
    private readonly LineEdit _search = new() { PlaceHolder = "Search character name or Ckey...", HorizontalExpand = true };
    private readonly Label _status = new();
    private readonly BoxContainer _rows = new() { Orientation = BoxContainer.LayoutOrientation.Vertical };
    private readonly Button _refresh = new() { Text = "Refresh" };
    private AdminBunkerEuiState? _state;

    public AdminBunkerEui()
    {
        var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        var header = new BoxContainer();
        var back = new Button { Text = "Return" };
        header.AddChild(_search); header.AddChild(_refresh); header.AddChild(back);
        root.AddChild(header); root.AddChild(_status);
        root.AddChild(new Label { Text = "Character name                         Ckey                                      Visit" });
        var scroll = new ScrollContainer { VerticalExpand = true, HorizontalExpand = true };
        scroll.AddChild(_rows); root.AddChild(scroll); _window.Contents.AddChild(root);
        _search.OnTextChanged += _ => Rebuild();
        _refresh.OnPressed += _ => SendMessage(new AdminBunkerRefreshMessage());
        back.OnPressed += _ => SendMessage(new AdminBunkerReturnMessage());
        _window.OnClose += () => SendMessage(new CloseEuiMessage());
    }
    public override void Opened() { _window.OpenCentered(); }
    public override void Closed() { _window.Close(); _window.Dispose(); }
    public override void HandleState(EuiStateBase state)
    {
        if (state is not AdminBunkerEuiState bunkers) return;
        _state = bunkers; _status.Text = bunkers.Status; _refresh.Disabled = bunkers.Busy; Rebuild();
    }
    private void Rebuild()
    {
        _rows.RemoveAllChildren();
        if (_state == null) return;
        var search = _search.Text.Trim();
        foreach (var entry in _state.Entries.Where(e => e.CharacterName.Contains(search, StringComparison.OrdinalIgnoreCase) || e.Ckey.Contains(search, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(e => e.CharacterName, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Ckey, StringComparer.OrdinalIgnoreCase))
        {
            var row = new BoxContainer();
            row.AddChild(new Label { Text = entry.CharacterName, MinWidth = 250, HorizontalExpand = true });
            row.AddChild(new Label { Text = entry.Ckey, MinWidth = 160 });
            var visit = new Button { Text = entry.Loaded ? "Visit loaded room" : "Load & visit", Disabled = _state.Busy };
            visit.OnPressed += _ => SendMessage(new AdminBunkerVisitMessage(entry.Key));
            row.AddChild(visit); _rows.AddChild(row);
        }
    }
}
