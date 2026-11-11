using Content.Client.Eui;
using Content.Shared._Reclamation.Bunkers;
using Content.Shared.Eui;
using JetBrains.Annotations;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;

namespace Content.Client._Reclamation.Bunkers;

[UsedImplicitly]
public sealed class BunkerVisitPromptEui : BaseEui
{
    private readonly DefaultWindow _window = new() { Title = Loc.GetString("bunker-visit-prompt-title") };
    private readonly Label _request = new();
    private readonly Button _approve = new() { Text = Loc.GetString("bunker-visit-approve"), Disabled = true };
    private readonly Button _deny = new() { Text = Loc.GetString("bunker-visit-deny") };
    private bool _closed;

    public BunkerVisitPromptEui()
    {
        var layout = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 8 };
        var buttons = new BoxContainer { SeparationOverride = 8 };
        buttons.AddChild(_approve);
        buttons.AddChild(_deny);
        layout.AddChild(_request);
        layout.AddChild(buttons);
        _window.Contents.AddChild(layout);
        _approve.OnPressed += _ => SendMessage(new BunkerVisitAnswerMessage(true));
        _deny.OnPressed += _ => SendMessage(new BunkerVisitAnswerMessage(false));
        _window.OnClose += () =>
        {
            if (!_closed)
                SendMessage(new CloseEuiMessage());
        };
    }

    public override void HandleState(EuiStateBase state)
    {
        if (state is not BunkerVisitPromptState prompt)
            return;

        _request.Text = Loc.GetString("bunker-visit-prompt", ("requester", prompt.Requester));
        _approve.Disabled = false;
        if (!_window.IsOpen)
            _window.OpenCentered();
    }

    public override void Closed()
    {
        _closed = true;
        _window.Dispose();
    }
}
