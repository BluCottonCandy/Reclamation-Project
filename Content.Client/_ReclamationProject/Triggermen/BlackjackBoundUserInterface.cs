using System.Numerics;
using Content.Shared._ReclamationProject.Triggermen;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;

namespace Content.Client._ReclamationProject.Triggermen;

public sealed class BlackjackBoundUserInterface(EntityUid owner, Enum key) : BoundUserInterface(owner, key)
{
    private BlackjackWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<BlackjackWindow>();
        _window.Move += (amount, hold) => SendMessage(new BlackjackMove(amount, hold));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is BlackjackState blackjack)
            _window?.ShowHand(blackjack);
    }
}

public sealed class BlackjackWindow : DefaultWindow
{
    private static readonly string[] Ranks = ["A", "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K"];
    private static readonly string[] Suits = ["♠", "♥", "♣", "♦"];
    private readonly BoxContainer _dealer = new() { SeparationOverride = 6 };
    private readonly BoxContainer _player = new() { SeparationOverride = 6 };
    private readonly Label _dealerTotal = new() { Text = "Dealer" };
    private readonly Label _total = new();
    private readonly Label _status = new() { Text = "Ties return your bet. Winning hands pay 2×." };
    private readonly LineEdit _amount = new() { Text = "5", MinSize = new Vector2(65, 0) };
    private readonly Button _bet = new() { Text = "Bet" };
    private readonly Button _hit = new() { Text = "Hit", Disabled = true };
    private readonly Button _hold = new() { Text = "Hold", Disabled = true };

    public event Action<int, bool>? Move;

    public BlackjackWindow()
    {
        Title = "Lucky 38 • Blackjack";
        MinSize = new Vector2(440, 305);
        var felt = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat { BackgroundColor = Color.FromHex("#203a2e") }
        };
        Contents.AddChild(felt);
        var body = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 9 };
        felt.AddChild(body);
        body.AddChild(new Label { Text = "◆  LUCKY 38 BLACKJACK  ◆", FontColorOverride = Color.Goldenrod });
        body.AddChild(_dealerTotal);
        body.AddChild(_dealer);
        body.AddChild(new Label { Text = "Your hand" });
        body.AddChild(_player);
        body.AddChild(_total);
        var controls = new BoxContainer { SeparationOverride = 8 };
        controls.AddChild(new Label { Text = "Caps (1–50)" });
        controls.AddChild(_amount);
        controls.AddChild(_bet);
        controls.AddChild(_hit);
        controls.AddChild(_hold);
        body.AddChild(controls);
        body.AddChild(_status);

        _bet.OnPressed += _ =>
        {
            if (int.TryParse(_amount.Text, out var amount) && amount is >= 1 and <= 50)
                Move?.Invoke(amount, false);
        };
        _hit.OnPressed += _ => Move?.Invoke(0, false);
        _hold.OnPressed += _ => Move?.Invoke(0, true);
    }

    public void ShowHand(BlackjackState state)
    {
        DrawCards(_dealer, state.Dealer);
        DrawCards(_player, state.Player);
        var dealerTotal = CountCards(state.Dealer);
        _dealerTotal.Text = state.Active ? $"Dealer shows: {dealerTotal}" : $"Dealer total: {dealerTotal}";
        _total.Text = $"Total: {CountCards(state.Player)}";
        _bet.Disabled = state.Active;
        _hit.Disabled = !state.Active;
        _hold.Disabled = !state.Active;
        _status.Text = state.Active ? "Hit for another card or Hold to finish your hand." : state.Result;
    }

    private static int CountCards(int[] cards)
    {
        var total = 0;
        var hasAce = false;
        foreach (var card in cards)
        {
            if (card < 0)
                continue;

            var rank = card % 13 + 1;
            total += Math.Min(rank, 10);
            hasAce |= rank == 1;
        }

        if (hasAce && total <= 11)
            total += 10;

        return total;
    }

    private static void DrawCards(BoxContainer row, int[] cards)
    {
        row.RemoveAllChildren();
        foreach (var card in cards)
        {
            var hidden = card < 0;
            var suit = hidden ? 0 : card / 13;
            var frame = new PanelContainer
            {
                MinSize = new Vector2(47, 62),
                PanelOverride = new StyleBoxFlat
                {
                    BackgroundColor = Color.FromHex(hidden ? "#702e26" : "#eee0ba")
                }
            };
            frame.AddChild(new Label
            {
                Text = hidden ? "◆" : $"{Ranks[card % 13]} {Suits[suit]}",
                Align = Label.AlignMode.Center,
                FontColorOverride = Color.FromHex(hidden ? "#efcf82" : suit is 1 or 3 ? "#9b2c28" : "#241f19")
            });
            row.AddChild(frame);
        }
    }
}
