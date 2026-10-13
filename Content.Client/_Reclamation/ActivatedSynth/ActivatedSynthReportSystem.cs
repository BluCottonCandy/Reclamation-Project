using Content.Client.CharacterInfo;
using Content.Client.UserInterface.Controls;
using Content.Shared._Reclamation.ActivatedSynth;
using Content.Shared.Mind;
using Robust.Client.Player;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._Reclamation.ActivatedSynth;

/// <summary>Extends the existing character menu, rather than introducing a separate antagonist menu.</summary>
public sealed class ActivatedSynthReportSystem : EntitySystem
{
    [Dependency] private readonly SharedMindSystem _minds = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    private FancyWindow? _window;
    private TextEdit? _text;
    private Label? _status;
    private readonly Dictionary<string, CheckBox> _objectives = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CharacterInfoSystem.GetCharacterInfoControlsEvent>(OnCharacterControls);
        SubscribeNetworkEvent<SynthReportStateEvent>(OnState);
        SubscribeNetworkEvent<SynthActivatedEvent>(OnActivated);
    }

    private void OnActivated(SynthActivatedEvent msg, EntitySessionEventArgs args)
    {
        var notice = new FancyWindow { Title = Loc.GetString("activated-synth-name"), MinWidth = 450 };
        var content = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        var text = new RichTextLabel { MaxWidth = 450 };
        text.SetMessage(FormattedMessage.FromUnformatted(Loc.GetString("activated-synth-briefing")));
        content.AddChild(text);
        var close = new Button { Text = Loc.GetString("activated-synth-acknowledge") };
        close.OnPressed += _ => notice.Close();
        content.AddChild(close);
        notice.ContentsContainer.AddChild(content);
        notice.OnClose += notice.Dispose;
        notice.OpenCentered();
    }

    private void OnCharacterControls(ref CharacterInfoSystem.GetCharacterInfoControlsEvent args)
    {
        if (_players.LocalSession is not {} session ||
            !_minds.TryGetMind(session.UserId, out var mind, out _) || !HasComp<ActivatedSynthComponent>(mind))
            return;
        var button = new Button { Text = Loc.GetString("activated-synth-report-button") };
        button.OnPressed += _ => RaiseNetworkEvent(new RequestSynthReportEvent());
        args.Controls.Add(button);
    }

    private void OnState(SynthReportStateEvent msg, EntitySessionEventArgs args)
    {
        if (_window is { Disposed: false })
        {
            if (_status != null)
                _status.Text = Loc.GetString("activated-synth-report-saved");
            return;
        }
        _objectives.Clear();
        _window = new FancyWindow
        {
            Title = Loc.GetString("activated-synth-report-title"),
            MinSize = new System.Numerics.Vector2(500, 400),
        };
        var content = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        var help = new RichTextLabel { MaxWidth = 480 };
        help.SetMessage(FormattedMessage.FromUnformatted(Loc.GetString("activated-synth-report-help")));
        content.AddChild(help);
        foreach (var (id, completed) in msg.RoleplayObjectives)
        {
            var checkbox = new CheckBox
            {
                Text = Loc.GetString($"activated-synth-task-{id}"),
                Pressed = completed,
                Disabled = !msg.CanEdit,
            };
            _objectives[id] = checkbox;
            content.AddChild(checkbox);
        }
        _text = new TextEdit { MinHeight = 220, VerticalExpand = true, Editable = msg.CanEdit };
        _text.InsertAtCursor(msg.Text);
        content.AddChild(_text);
        _status = new Label();
        content.AddChild(_status);
        var save = new Button { Text = Loc.GetString("activated-synth-report-save"), Disabled = !msg.CanEdit };
        save.OnPressed += _ =>
        {
            var text = Rope.Collapse(_text.TextRope);
            if (text.Length > 2000)
            {
                _status.Text = Loc.GetString("activated-synth-report-too-long");
                return;
            }
            var request = new SubmitSynthReportEvent { Text = text };
            foreach (var (id, checkbox) in _objectives)
            {
                if (checkbox.Pressed)
                    request.CompletedObjectives.Add(id);
            }
            RaiseNetworkEvent(request);
        };
        content.AddChild(save);
        _window.ContentsContainer.AddChild(content);
        _window.OnClose += () =>
        {
            _window?.Dispose();
            _window = null;
        };
        _window.OpenCentered();
    }

    public override void Shutdown()
    {
        _window?.Dispose();
        base.Shutdown();
    }
}
