using Robust.Client.Graphics;
using System.Numerics;
using Content.Shared._ReclamationProject.Triggermen;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Timing;

namespace Content.Client._ReclamationProject.Triggermen;

public sealed class SlotsBoundUserInterface(EntityUid owner, Enum key) : BoundUserInterface(owner, key)
{
    private SlotsWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<SlotsWindow>();
        _window.Bet += amount => SendMessage(new SlotsBet(amount));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is SlotsResult result)
            _window?.Spin(result);
    }
}

public sealed class SlotsWindow : DefaultWindow
{
    private static readonly string[] Symbols = ["CAP", "NUKA", "VAULT", "ATOM", "7", "GEAR", "BAR", "RAD"];
    private readonly Label[] _reels = new Label[3];
    private readonly Label _result = new();
    private readonly LineEdit _bet = new() { Text = "5" };
    private SlotsResult? _spin;
    private float _elapsed;

    public event Action<int>? Bet;

    public SlotsWindow()
    {
        Title = "Lucky 38 • Wasteland Slots";
        MinSize = new Vector2(460, 350);
        var body = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 9 };
        var cabinet = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat { BackgroundColor = Color.FromHex("#3e2b19") }
        };
        Contents.AddChild(cabinet);
        cabinet.AddChild(body);
        body.AddChild(new Label { Text = "◆  LUCKY 38  ◆", Align = Label.AlignMode.Center, FontColorOverride = Color.Goldenrod });
        var row = new BoxContainer { SeparationOverride = 10 };
        body.AddChild(row);
        for (var i = 0; i < _reels.Length; i++)
        {
            var frame = new PanelContainer
            {
                MinSize = new Vector2(100, 62),
                PanelOverride = new StyleBoxFlat { BackgroundColor = Color.FromHex("#292820") }
            };
            _reels[i] = new Label { Text = "7", Align = Label.AlignMode.Center, FontColorOverride = Color.FromHex("#f2cd78") };
            frame.AddChild(_reels[i]);
            row.AddChild(frame);
        }

        body.AddChild(new Label { Text = "PAYTABLE" });
        body.AddChild(new Label { Text = "Three 7s: 3×  •  Two NUKAs: 2×  •  Three VAULTs: 1.5×" });
        body.AddChild(new Label { Text = "Two ATOMs: 1×  •  One CAP: 0.25× (minimum 1 cap)" });
        body.AddChild(new Label { Text = "Wager (1–50 caps)" });
        body.AddChild(_bet);
        var spin = new Button { Text = "SPIN" };
        spin.OnPressed += _ =>
        {
            if (_spin == null && int.TryParse(_bet.Text, out var amount) && amount is >= 1 and <= 50)
                Bet?.Invoke(amount);
        };
        body.AddChild(spin);
        body.AddChild(_result);
    }

    public void Spin(SlotsResult result)
    {
        _spin = result;
        _elapsed = 0;
        _result.Text = "Spinning…";
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (_spin is not {} spin)
            return;

        _elapsed += args.DeltaSeconds;
        for (var i = 0; i < _reels.Length; i++)
            _reels[i].Text = Symbols[_elapsed >= 0.8f + i * 0.3f
                ? spin.Reels[i]
                : ((int) (_elapsed * (14 + i * 4)) + i) % Symbols.Length];

        if (_elapsed < 1.4f)
            return;

        _result.Text = spin.Prize > 0 ? $"{(spin.Reels[0] == 4 ? "Jackpot" : "Payout")}! {spin.Prize} caps paid." : "No win this time.";
        _spin = null;
    }
}
