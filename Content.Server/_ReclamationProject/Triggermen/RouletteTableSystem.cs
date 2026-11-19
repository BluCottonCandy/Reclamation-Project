using System.Linq;
using Content.Shared._ReclamationProject.Triggermen;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._ReclamationProject.Triggermen;

[RegisterComponent]
public sealed partial class ReclamationRouletteTableComponent : Component
{
    public EntityUid? Player;
    public string PlayerName = "";
    public RouletteBetKind Kind;
    public int Choice;
    public int Wager;
    public int PendingNumber = -1;
    public int LastNumber = -1;
    public int SpinId;
    public TimeSpan FinishAt;
    public readonly List<int> History = [];
    public string Status = "Hold caps, choose a bet, and spin. One wager per spin.";
}

public sealed class RouletteTableSystem : EntitySystem
{
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedStackSystem _stacks = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    public override void Initialize()
    {
        Subs.BuiEvents<ReclamationRouletteTableComponent>(RouletteKey.Key, subs =>
        {
            subs.Event<RouletteBet>(OnBet);
            subs.Event<BoundUIOpenedEvent>((uid, table, _) => Publish(uid, table));
        });
    }

    private void OnBet(EntityUid uid, ReclamationRouletteTableComponent table, RouletteBet args)
    {
        if (table.Player != null || !TryComp<GamblingMachineComponent>(uid, out var bank))
        {
            Reject(uid, table, args.Actor, "Wait for the wheel to stop.");
            return;
        }
        if (!RouletteRules.IsValid(args.Kind, args.Number, args.Amount))
        {
            Reject(uid, table, args.Actor, "That wager is outside the table limits.");
            return;
        }

        var maximumReturn = args.Amount * RouletteRules.Multiplier(args.Kind);
        if (bank.Bank < maximumReturn - args.Amount || bank.Bank > int.MaxValue - args.Amount)
        {
            Reject(uid, table, args.Actor, "The house cannot cover that wager. Ask the house to fund the table.");
            return;
        }
        var cash = _hands.EnumerateHeld(args.Actor)
            .Where(item => TryComp<StackComponent>(item, out var stack) && stack.StackTypeId.Id == "Caps")
            .ToArray();
        if (cash.Sum(item => (long) _stacks.GetCount(item)) < args.Amount)
        {
            Reject(uid, table, args.Actor, "Hold enough caps in your hands to place that bet.");
            return;
        }
        var remaining = args.Amount;
        foreach (var item in cash)
        {
            var count = _stacks.GetCount(item);
            var spent = Math.Min(count, remaining);
            _stacks.SetCount(item, count - spent);
            remaining -= spent;
            if (remaining == 0)
                break;
        }

        bank.Bank += args.Amount;
        bank.RouletteSpinning = true;
        table.Player = args.Actor;
        table.PlayerName = Name(args.Actor);
        table.Kind = args.Kind;
        table.Choice = args.Number;
        table.Wager = args.Amount;
        // Exactly one uniform roll. This number stays server-side until settlement.
        table.PendingNumber = _random.Next(RouletteRules.PocketCount);
        table.SpinId++;
        table.FinishAt = _timing.CurTime + TimeSpan.FromSeconds(RouletteRules.SpinSeconds);
        table.Status = $"{table.PlayerName}: {args.Amount} caps on {RouletteRules.BetName(args.Kind, args.Number)}. Spinning...";
        _audio.PlayPvs(new SoundPathSpecifier("/Audio/_ReclamationProject/Casino/roulette_spin.ogg"), uid,
            AudioParams.Default.WithVolume(-5f).WithMaxDistance(8f));
        Publish(uid, table);
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<ReclamationRouletteTableComponent, GamblingMachineComponent>();
        while (query.MoveNext(out var uid, out var table, out var bank))
        {
            if (table.Player is not {} player || _timing.CurTime < table.FinishAt)
                continue;
            var won = RouletteRules.Wins(table.Kind, table.Choice, table.PendingNumber);
            var returned = won ? table.Wager * RouletteRules.Multiplier(table.Kind) : 0;
            bank.Bank -= returned;
            bank.RouletteSpinning = false;
            table.Player = null;
            table.LastNumber = table.PendingNumber;
            table.PendingNumber = -1;
            table.History.Insert(0, table.LastNumber);
            if (table.History.Count > 12)
                table.History.RemoveAt(12);
            var color = table.LastNumber is 0 or RouletteRules.DoubleZero ? "green" : RouletteRules.IsRed(table.LastNumber) ? "red" : "black";
            var result = $"{RouletteRules.FormatNumber(table.LastNumber)} {color}. ";
            table.Status = result + (won
                ? $"{table.PlayerName} receives {returned} caps (+{returned - table.Wager})."
                : $"House wins {table.Wager} caps.");
            if (returned > 0)
            {
                var caps = Spawn("N14CurrencyCap", Transform(uid).Coordinates);
                _stacks.SetCount(caps, returned);
                // Closing the window never cancels a paid bet or forfeits its payout.
                if (!TerminatingOrDeleted(player))
                    _hands.TryPickupAnyHand(player, caps);
            }
            var sound = won ? "slots_win" : "slots_lose";
            _audio.PlayPvs(new SoundPathSpecifier($"/Audio/_ReclamationProject/Casino/{sound}.ogg"), uid,
                AudioParams.Default.WithVolume(-6f).WithMaxDistance(8f));
            Publish(uid, table);
        }
    }

    private void Reject(EntityUid uid, ReclamationRouletteTableComponent table, EntityUid actor, string message)
    {
        _popup.PopupEntity(message, uid, actor);
        Publish(uid, table);
    }

    private void Publish(EntityUid uid, ReclamationRouletteTableComponent table)
        => _ui.SetUiState(uid, RouletteKey.Key, new RouletteState(table.SpinId, table.Player != null,
            table.Player != null ? -1 : table.LastNumber, table.Status, table.History.ToArray()));
}
