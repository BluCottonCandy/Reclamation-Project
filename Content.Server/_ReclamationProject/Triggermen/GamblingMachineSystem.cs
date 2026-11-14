using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using System.Linq;
using Content.Shared._ReclamationProject.Triggermen;
using Robust.Shared.Timing;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Mind;
using Content.Shared.Popups;
using Content.Server.Roles.Jobs;
using Content.Shared.Stacks;
using Content.Shared.Verbs;
using Robust.Shared.Random;

namespace Content.Server._ReclamationProject.Triggermen;

[RegisterComponent]
public sealed partial class GamblingMachineComponent : Component
{
    [DataField] public int Bank;
    public TimeSpan NextSpin;
    [DataField] public bool Blackjack;
    public EntityUid? BlackjackPlayer;
    public int BlackjackBet;
    public List<int> PlayerCards = [];
    public List<int> DealerCards = [];
}

public sealed class GamblingMachineSystem : EntitySystem
{
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedStackSystem _stacks = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly JobSystem _jobs = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    public override void Initialize()
    {
        Subs.BuiEvents<GamblingMachineComponent>(BlackjackKey.Key, subs =>
        {
            subs.Event<BlackjackMove>(OnBlackjack);
            subs.Event<BoundUIClosedEvent>((uid, machine, args) =>
            {
                if (machine.BlackjackPlayer == args.Actor)
                    FinishBlackjack(uid, machine, false);
            });
        });
        Subs.BuiEvents<GamblingMachineComponent>(SlotsKey.Key, subs => subs.Event<SlotsBet>(OnBet));
        SubscribeLocalEvent<GamblingMachineComponent, GetVerbsEvent<AlternativeVerb>>(OnVerbs);
    }

    private void OnBlackjack(EntityUid uid, GamblingMachineComponent machine, BlackjackMove args)
    {
        if (!machine.Blackjack)
            return;

        var user = args.Actor;
        if (args.Amount > 0)
        {
            var bet = args.Amount;
            if (bet > 50 || machine.BlackjackPlayer != null || machine.Bank < bet ||
                machine.Bank > int.MaxValue - bet)
                return;

            var caps = _hands.EnumerateHeld(user)
                .Where(item => TryComp<StackComponent>(item, out var stack) && stack.StackTypeId.Id == "Caps")
                .ToArray();
            if (caps.Sum(item => (long) _stacks.GetCount(item)) < bet)
            {
                _popup.PopupEntity(Loc.GetString("triggermen-slots-unavailable"), uid, user);
                return;
            }

            var remaining = bet;
            foreach (var item in caps)
            {
                var count = _stacks.GetCount(item);
                var spent = Math.Min(count, remaining);
                _stacks.SetCount(item, count - spent);
                remaining -= spent;
                if (remaining == 0)
                    break;
            }

            machine.Bank += bet;
            machine.BlackjackBet = bet;
            machine.BlackjackPlayer = user;
            machine.PlayerCards = [DrawCard(), DrawCard()];
            machine.DealerCards = [DrawCard(), DrawCard()];
            if (Total(machine.PlayerCards) == 21)
                FinishBlackjack(uid, machine, true);
            else
                ShowBlackjack(uid, machine, true, "");
            return;
        }

        if (machine.BlackjackPlayer != user)
            return;

        if (!args.Hold)
            machine.PlayerCards.Add(DrawCard());

        var total = Total(machine.PlayerCards);
        if (args.Hold || total >= 21)
            FinishBlackjack(uid, machine, total <= 21);
        else
            ShowBlackjack(uid, machine, true, "");
    }

    private int DrawCard() => _random.Next(52);

    private void FinishBlackjack(EntityUid uid, GamblingMachineComponent machine, bool stand)
    {
        if (machine.BlackjackPlayer is not {} user)
            return;

        machine.BlackjackPlayer = null;
        while (stand && Total(machine.DealerCards) < 17)
            machine.DealerCards.Add(DrawCard());

        var player = Total(machine.PlayerCards);
        var dealer = Total(machine.DealerCards);
        var won = stand && player <= 21 && (dealer > 21 || player > dealer);
        var push = stand && player <= 21 && player == dealer;
        var payout = won ? machine.BlackjackBet * 2 : push ? machine.BlackjackBet : 0;
        if (payout > 0)
        {
            machine.Bank -= payout;
            GiveCaps(uid, user, payout);
        }

        ShowBlackjack(uid, machine, false, won ? $"You win {payout} caps." : push ? "Tie. Your bet was returned." : "House wins.");
    }

    private void ShowBlackjack(EntityUid uid, GamblingMachineComponent machine, bool active, string result)
        => _ui.SetUiState(uid, BlackjackKey.Key, new BlackjackState(machine.PlayerCards.ToArray(),
            active ? [machine.DealerCards[0], -1] : machine.DealerCards.ToArray(), active, result));

    private static int Total(List<int> cards)
    {
        var total = cards.Sum(card => Math.Min(10, card % 13 + 1));
        return total <= 11 && cards.Any(card => card % 13 == 0) ? total + 10 : total;
    }

    private void OnBet(EntityUid uid, GamblingMachineComponent machine, SlotsBet args)
    {
        var wager = args.Amount;
        var prize = wager * 3;
        if (wager is < 1 or > 50 || _timing.CurTime < machine.NextSpin)
            return;

        var user = args.Actor;
        var cash = _hands.EnumerateHeld(user)
            .Where(item => TryComp<StackComponent>(item, out var stack) && stack.StackTypeId.Id == "Caps")
            .ToArray();
        if (machine.Bank < prize - wager || machine.Bank > int.MaxValue - wager ||
            cash.Sum(item => (long) _stacks.GetCount(item)) < wager)
        {
            _popup.PopupEntity(Loc.GetString("triggermen-slots-unavailable"), uid, user);
            return;
        }

        machine.NextSpin = _timing.CurTime + TimeSpan.FromSeconds(1.5);
        var remaining = wager;
        foreach (var item in cash)
        {
            var count = _stacks.GetCount(item);
            var taken = Math.Min(remaining, count);
            _stacks.SetCount(item, count - taken);
            remaining -= taken;
            if (remaining == 0)
                break;
        }

        machine.Bank += wager;
        var roll = _random.Next(100);
        var (reels, payout) = roll switch
        {
            < 10 => (new[] { 4, 4, 4 }, prize),
            < 18 => (new[] { 1, 1, 7 }, wager * 2),
            < 24 => (new[] { 2, 2, 2 }, wager * 3 / 2),
            < 32 => (new[] { 3, 3, 7 }, wager),
            < 48 => (new[] { 0, 5, 6 }, Math.Max(1, wager / 4)),
            _ => (new[] { _random.Next(5, 8), _random.Next(5, 8), _random.Next(5, 8) }, 0),
        };
        for (var i = reels.Length - 1; i > 0; i--)
        {
            var j = _random.Next(i + 1);
            (reels[i], reels[j]) = (reels[j], reels[i]);
        }

        if (payout > 0)
        {
            machine.Bank -= payout;
            GiveCaps(uid, user, payout);
        }

        _ui.SetUiState(uid, SlotsKey.Key, new SlotsResult(reels, payout));
        PlaySpinSounds(uid, payout, roll < 10);
    }

    private void PlaySpinSounds(EntityUid uid, int payout, bool jackpot)
    {
        var click = new SoundPathSpecifier("/Audio/_RMC/Machines/click.ogg");
        for (var tick = 0; tick < 10; tick++)
            PlayAfter(tick * 140, click);

        if (payout > 0)
        {
            PlayAfter(1400, new SoundPathSpecifier(jackpot
                ? "/Audio/Machines/vending_jingle.ogg"
                : "/Audio/Machines/ding.ogg"));
        }

        void PlayAfter(int milliseconds, SoundSpecifier sound)
        {
            Timer.Spawn(TimeSpan.FromMilliseconds(milliseconds), () =>
            {
                if (!TerminatingOrDeleted(uid))
                    _audio.PlayPvs(sound, uid);
            });
        }
    }

    private bool CanManageHouse(EntityUid user)
        => _mind.TryGetMind(user, out var mind, out _) &&
           _jobs.MindTryGetJob(mind, out _, out var job) && job.ID is "TriggermenBoss" or "TriggermenCommissionMember" or "TriggermenCapoDeiCapi";

    private void OnVerbs(EntityUid uid, GamblingMachineComponent machine, GetVerbsEvent<AlternativeVerb> args)
    {
        var user = args.User;
        if (!args.CanInteract || !args.CanAccess || !CanManageHouse(user))
            return;

        if (machine.Bank > 0)
            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString("triggermen-slots-withdraw", ("amount", machine.Bank)),
                Act = () =>
                {
                    if (!CanManageHouse(user) || TerminatingOrDeleted(uid) || machine.Bank <= 0 || machine.BlackjackPlayer != null)
                        return;

                    var amount = machine.Bank;
                    machine.Bank = 0;
                    GiveCaps(uid, user, amount);
                }
            });

        if (args.Using is not {} cash || !TryComp<StackComponent>(cash, out var stack) ||
            stack.StackTypeId.Id != "Caps")
            return;

        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("triggermen-slots-fund"),
            Act = () =>
            {
                if (!CanManageHouse(user) || TerminatingOrDeleted(uid) || TerminatingOrDeleted(cash) ||
                    !_hands.TryGetActiveItem(user, out var held) || held != cash)
                    return;

                var amount = _stacks.GetCount(cash);
                if (amount <= 0 || machine.Bank > int.MaxValue - amount)
                    return;

                _stacks.SetCount(cash, 0);
                machine.Bank += amount;
            }
        });
    }

    private void GiveCaps(EntityUid machine, EntityUid user, int amount)
    {
        var caps = Spawn("N14CurrencyCap", Transform(machine).Coordinates);
        _stacks.SetCount(caps, amount);
        _hands.TryPickupAnyHand(user, caps);
    }
}
