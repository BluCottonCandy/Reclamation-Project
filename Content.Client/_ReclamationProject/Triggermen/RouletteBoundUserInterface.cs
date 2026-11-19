using System.Linq;
using System.Numerics;
using Content.Shared._ReclamationProject.Triggermen;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Timing;

namespace Content.Client._ReclamationProject.Triggermen;

public sealed class RouletteBoundUserInterface(EntityUid owner, Enum key) : BoundUserInterface(owner, key)
{
    private RouletteWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<RouletteWindow>();
        _window.Bet += (kind, number, amount) => SendMessage(new RouletteBet(kind, number, amount));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is RouletteState roulette)
            _window?.ShowState(roulette);
    }
}

public sealed class RouletteWindow : DefaultWindow
{
    private readonly RouletteWheel _wheel = new();
    private readonly Label _status = new() { Text = "Hold caps in your hands, choose a bet, then spin." };
    private readonly Label _history = new() { Text = "Recent results: —" };
    private readonly Label _selection = new();
    private readonly Label _odds = new();
    private readonly Button _spin = new() { Text = "SPIN", MinSize = new Vector2(110, 32) };
    private readonly LineEdit _amount = new() { Text = "5", MinSize = new Vector2(60, 0) };
    private readonly List<(Button Button, RouletteBetKind Kind, int Number)> _choices = [];
    private RouletteBetKind _kind = RouletteBetKind.Red;
    private int _number;
    private bool _busy;
    public event Action<RouletteBetKind, int, int>? Bet;

    public RouletteWindow()
    {
        Title = "Lucky 38 Roulette";
        var layout = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 6 };
        Contents.AddChild(layout);
        layout.AddChild(new Label { Text = "LUCKY 38  /  DOUBLE-ZERO ROULETTE" });
        var top = new BoxContainer { SeparationOverride = 10 };
        top.AddChild(_wheel);
        var board = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 4 };
        top.AddChild(board);
        board.AddChild(new Label { Text = "Choose one bet per spin" });
        var green = new BoxContainer { SeparationOverride = 4 };
        green.AddChild(Choice(RouletteBetKind.Number, 0));
        green.AddChild(Choice(RouletteBetKind.Number, RouletteRules.DoubleZero));
        green.AddChild(new Label { Text = "Green: outside bets lose" });
        board.AddChild(green);
        for (var row = 3; row >= 1; row--)
        {
            var line = new BoxContainer { SeparationOverride = 2 };
            for (var col = 0; col < 12; col++)
                line.AddChild(Choice(RouletteBetKind.Number, col * 3 + row));
            board.AddChild(line);
        }
        var dozens = new BoxContainer { SeparationOverride = 4 };
        foreach (var kind in new[] { RouletteBetKind.FirstDozen, RouletteBetKind.SecondDozen, RouletteBetKind.ThirdDozen })
            dozens.AddChild(Choice(kind));
        board.AddChild(dozens);
        var outside = new BoxContainer { SeparationOverride = 4 };
        foreach (var kind in new[] { RouletteBetKind.Low, RouletteBetKind.Even, RouletteBetKind.Red,
                     RouletteBetKind.Black, RouletteBetKind.Odd, RouletteBetKind.High })
            outside.AddChild(Choice(kind));
        board.AddChild(outside);
        layout.AddChild(top);
        layout.AddChild(_selection);
        layout.AddChild(_odds);
        var wager = new BoxContainer { SeparationOverride = 8 };
        wager.AddChild(new Label { Text = "Bet (caps)" });
        wager.AddChild(_amount);
        wager.AddChild(_spin);
        layout.AddChild(wager);
        layout.AddChild(_status);
        layout.AddChild(_history);
        layout.AddChild(new Label { Text = "Returns include your stake. Every pocket has the same chance." });
        layout.AddChild(new Label { Text = "House edge: 5.26% on every bet. Maximum profit per spin: 35 caps." });
        _amount.OnTextChanged += _ => UpdateSpin();
        _spin.OnPressed += _ =>
        {
            if (_busy || !int.TryParse(_amount.Text, out var amount) || !RouletteRules.IsValid(_kind, _number, amount))
                return;
            SetBusy(true);
            Bet?.Invoke(_kind, _number, amount);
        };
        Select(_kind, 0);
    }

    private Button Choice(RouletteBetKind kind, int number = 0)
    {
        var button = new Button
        {
            Text = RouletteRules.BetName(kind, number),
            ToggleMode = true,
            MinSize = new Vector2(kind == RouletteBetKind.Number ? 34 : 64, 30),
            HorizontalExpand = kind != RouletteBetKind.Number
        };
        if (kind == RouletteBetKind.Number)
            button.Label.FontColorOverride = number is 0 or RouletteRules.DoubleZero ? Color.FromHex("#74dc9b") :
                RouletteRules.IsRed(number) ? Color.FromHex("#ff8a82") : Color.White;
        else if (kind == RouletteBetKind.Red)
            button.Label.FontColorOverride = Color.FromHex("#ff8a82");
        button.OnPressed += _ => Select(kind, number);
        _choices.Add((button, kind, number));
        return button;
    }

    private void Select(RouletteBetKind kind, int number)
    {
        _kind = kind;
        _number = number;
        foreach (var choice in _choices)
            choice.Button.Pressed = choice.Kind == kind && choice.Number == number;
        var maximum = RouletteRules.MaxBet(kind);
        if (!int.TryParse(_amount.Text, out var amount) || amount > maximum || amount < 1)
            _amount.Text = maximum.ToString();
        _selection.Text = $"Selected: {RouletteRules.BetName(kind, number)}    |    Bet: 1–{maximum} caps";
        var chance = kind == RouletteBetKind.Number ? "1/38 (2.63%)" :
            RouletteRules.Multiplier(kind) == 3 ? "12/38 (31.58%)" : "18/38 (47.37%)";
        _odds.Text = $"Win chance: {chance}    |    Winning return: {RouletteRules.Multiplier(kind)} × your bet";
        UpdateSpin();
    }

    public void ShowState(RouletteState state)
    {
        _wheel.ShowState(state);
        _status.Text = state.Status;
        _history.Text = "Recent results: " + (state.History.Length == 0 ? "—" :
            string.Join("  ·  ", state.History.Select(RouletteRules.FormatNumber)));
        SetBusy(state.Spinning);
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _amount.Editable = !busy;
        foreach (var choice in _choices)
            choice.Button.Disabled = busy;
        UpdateSpin();
    }

    private void UpdateSpin()
        => _spin.Disabled = _busy || !int.TryParse(_amount.Text, out var amount) || !RouletteRules.IsValid(_kind, _number, amount);
}

public sealed class RouletteWheel : Control
{
    // Content assemblies must pass the engine's IL verifier. A stackalloc here
    // emits localloc, which prevents the entire client module from loading.
    // Reuse managed storage so drawing is verifiable and does not allocate per frame.
    private readonly Vector2[] _quad = new Vector2[4];
    private bool _spinning;
    private int _result = -1;
    private float _angle;
    private float _ballAngle = -MathF.PI / 2;

    public RouletteWheel() => MinSize = new Vector2(232, 232);

    public void ShowState(RouletteState state)
    {
        _spinning = state.Spinning;
        _result = state.Result;
        if (!_spinning && _result >= 0)
        {
            _angle = -MathF.PI / 2 - Array.IndexOf(RouletteRules.WheelOrder, _result) * MathF.Tau / RouletteRules.PocketCount;
            _ballAngle = -MathF.PI / 2;
        }
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (!_spinning)
            return;
        _angle = (_angle + args.DeltaSeconds * 2.4f) % MathF.Tau;
        _ballAngle = (_ballAngle - args.DeltaSeconds * 5.2f) % MathF.Tau;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        var scale = UIScale;
        var center = new Vector2(116, 116) * scale;
        var font = UserInterfaceManager.ThemeDefaults.DefaultFont;
        handle.DrawCircle(center, 114 * scale, Color.FromHex("#382116"));
        handle.DrawCircle(center, 110 * scale, Color.FromHex("#ba9152"));
        var quad = _quad;
        var step = MathF.Tau / RouletteRules.PocketCount;
        for (var i = 0; i < RouletteRules.PocketCount; i++)
        {
            var number = RouletteRules.WheelOrder[i];
            var angle = _angle + i * step;
            var first = new Vector2(MathF.Cos(angle - step / 2), MathF.Sin(angle - step / 2));
            var last = new Vector2(MathF.Cos(angle + step / 2), MathF.Sin(angle + step / 2));
            quad[0] = center + first * 77 * scale;
            quad[1] = center + first * 106 * scale;
            quad[2] = center + last * 77 * scale;
            quad[3] = center + last * 106 * scale;
            var color = number is 0 or RouletteRules.DoubleZero ? Color.FromHex("#216543") :
                RouletteRules.IsRed(number) ? Color.FromHex("#a82f35") : Color.FromHex("#20212a");
            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleStrip, quad, color);
            handle.DrawLine(quad[0], quad[1], Color.FromHex("#997442"));
            var text = RouletteRules.FormatNumber(number);
            var position = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 93 * scale;
            handle.DrawString(font, position + new Vector2(-3.5f * text.Length, 4) * scale, text, 0.65f * scale, Color.White);
        }
        handle.DrawCircle(center, 76 * scale, Color.FromHex("#674127"));
        handle.DrawCircle(center, 63 * scale, Color.FromHex("#855734"));
        handle.DrawCircle(center, 49 * scale, Color.FromHex("#233c31"));
        var result = _spinning ? "..." : _result >= 0 ? RouletteRules.FormatNumber(_result) : "38";
        handle.DrawString(font, center + new Vector2(-8 * result.Length, 10) * scale, result, 1.8f * scale, Color.White);
        var ball = center + new Vector2(MathF.Cos(_ballAngle), MathF.Sin(_ballAngle)) * 81 * scale;
        handle.DrawCircle(ball, 3 * scale, Color.FromHex("#fff5d8"));
        handle.DrawLine(center + new Vector2(0, -115) * scale, center + new Vector2(0, -103) * scale, Color.FromHex("#ffe0a1"));
    }
}
