// #Reclamation Project - Diamond City: Takahashi's "Make Noodle Cup" power.
using Content.Server.Database;
using Content.Shared._Misfits.Currency.Components;
using Content.Shared._ReclamationProject.DiamondCity;
using Content.Shared.Actions;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Popups;
using Content.Shared.Stacks;

namespace Content.Server._ReclamationProject.DiamondCity;

/// <summary>
/// Spends bottlecaps to make a noodle cup in the user's hand.
/// Caps come out of the persistent wallet first (that is where most players keep them),
/// then out of cap stacks held in hand. Nothing is charged if the user can't pay in full.
/// </summary>
public sealed class NoodleCupMakerSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedStackSystem _stack = default!;
    [Dependency] private readonly IServerDbManager _db = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<NoodleCupMakerComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<NoodleCupMakerComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<NoodleCupMakerComponent, MakeNoodleCupActionEvent>(OnMake);
    }

    private void OnMapInit(Entity<NoodleCupMakerComponent> ent, ref MapInitEvent args)
    {
        _actions.AddAction(ent, ref ent.Comp.Action, ent.Comp.ActionId);
        Dirty(ent);

        if (ent.Comp.StartingItem is { } startingItem)
            _hands.PickupOrDrop(ent, Spawn(startingItem, Transform(ent).Coordinates));
    }

    private void OnShutdown(Entity<NoodleCupMakerComponent> ent, ref ComponentShutdown args)
    {
        _actions.RemoveAction(ent.Owner, ent.Comp.Action);
    }

    private void OnMake(Entity<NoodleCupMakerComponent> ent, ref MakeNoodleCupActionEvent args)
    {
        if (args.Handled)
            return;

        var user = ent.Owner;
        if (!TryPay(user, ent.Comp.Cost))
        {
            _popup.PopupEntity(Loc.GetString("noodle-cup-maker-no-caps", ("cost", ent.Comp.Cost)), user, user);
            return;
        }

        var cup = Spawn(ent.Comp.Result, Transform(user).Coordinates);
        _hands.PickupOrDrop(user, cup);
        _popup.PopupEntity(Loc.GetString("noodle-cup-maker-made", ("cost", ent.Comp.Cost)), user, user);
        args.Handled = true;
    }

    /// <summary>Takes <paramref name="cost"/> caps from the wallet, else from caps held in hand.</summary>
    public bool TryPay(EntityUid user, int cost)
    {
        if (cost <= 0)
            return true;

        if (TryComp<PersistentCurrencyComponent>(user, out var wallet) && wallet.Bottlecaps >= cost)
        {
            wallet.Bottlecaps -= cost;
            Dirty(user, wallet);
            if (wallet.UserId != null && wallet.CharacterName != null && Guid.TryParse(wallet.UserId, out var playerId))
            {
                _ = _db.UpsertCharacterCurrencyAsync(playerId, wallet.CharacterName, wallet.Bottlecaps,
                    wallet.NcrDollars, wallet.Silver, wallet.Gold, wallet.LegionDenarii, wallet.PrewarMoney);
            }
            return true;
        }

        // Count held caps first so a short purse charges nothing.
        var held = new List<(EntityUid Uid, StackComponent Stack)>();
        var total = 0;
        foreach (var item in _hands.EnumerateHeld(user))
        {
            if (!TryComp<ConsumableCurrencyComponent>(item, out var currency)
                || currency.CurrencyType != CurrencyType.Bottlecaps
                || currency.ValuePerUnit != 1
                || !TryComp<StackComponent>(item, out var stack))
            {
                continue;
            }

            held.Add((item, stack));
            total += stack.Count;
        }

        if (total < cost)
            return false;

        var remaining = cost;
        foreach (var (uid, stack) in held)
        {
            var take = Math.Min(remaining, stack.Count);
            _stack.Use(uid, take, stack);
            remaining -= take;
            if (remaining == 0)
                break;
        }

        return true;
    }
}
