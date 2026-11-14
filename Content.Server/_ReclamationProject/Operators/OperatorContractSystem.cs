using Content.Shared.Examine;
using System.Linq;
using Content.Server.Administration;
using Content.Server._Misfits.Currency.Systems;
using Content.Shared._ReclamationProject.Operators;
using Content.Shared.Actions;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Shared.Player;

namespace Content.Server._ReclamationProject.Operators;

[RegisterComponent]
public sealed partial class OperatorContractIssuerComponent : Component
{
    public EntityUid? ContractAction;
}

[RegisterComponent]
public sealed partial class OperatorContractComponent : Component
{
    public EntityUid? Issuer;
    public int Price;
    public string Signature = string.Empty;
    public string Job = string.Empty;
    public EntityUid? Client;
    public bool Complete;
}

public sealed class OperatorContractSystem : EntitySystem
{
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly QuickDialogSystem _dialog = default!;
    [Dependency] private readonly PersistentCurrencySystem _currency = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly MobStateSystem _mob = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<OperatorContractIssuerComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<OperatorContractIssuerComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<OperatorContractIssuerComponent, OperatorCreateContractEvent>(OnCreate);
        SubscribeLocalEvent<OperatorContractComponent, UseInHandEvent>(OnReview);
        SubscribeLocalEvent<OperatorContractComponent, ExaminedEvent>(OnExamine);
    }

    private void OnExamine(EntityUid uid, OperatorContractComponent contract, ExaminedEvent args)
    {
        args.PushText(Loc.GetString("operators-contract-review", ("price", contract.Price), ("job", contract.Job)));
        if (contract.Complete)
            args.PushText(Loc.GetString("operators-contract-completed"));
    }

    private void OnStartup(EntityUid uid, OperatorContractIssuerComponent component, ComponentStartup args)
        => _actions.AddAction(uid, ref component.ContractAction, "ActionOperatorCreateContract");

    private void OnShutdown(EntityUid uid, OperatorContractIssuerComponent component, ComponentShutdown args)
        => _actions.RemoveAction(uid, component.ContractAction);

    private void OnCreate(EntityUid uid, OperatorContractIssuerComponent component, OperatorCreateContractEvent args)
    {
        if (args.Handled || !TryComp<ActorComponent>(uid, out var actor))
            return;

        args.Handled = true;
        var session = actor.PlayerSession;
        _dialog.OpenDialog<string, LongString>(session, Loc.GetString("operators-contract-create"),
            Loc.GetString("operators-contract-price"), Loc.GetString("operators-contract-job"), (priceText, terms) =>
            {
                if (session.AttachedEntity != uid || !_mob.IsAlive(uid) || !HasComp<OperatorContractIssuerComponent>(uid))
                    return;

                var job = terms.String.Trim();
                if (!int.TryParse(priceText, out var price) || price <= 0 || job.Length is 0 or > 400)
                {
                    _popup.PopupEntity(Loc.GetString("operators-contract-invalid"), uid, uid);
                    return;
                }

                var scroll = Spawn("OperatorContractScroll", Transform(uid).Coordinates);
                var contract = Comp<OperatorContractComponent>(scroll);
                contract.Issuer = uid;
                contract.Price = price;
                contract.Job = job;
                contract.Signature = Name(uid);
                _hands.TryPickupAnyHand(uid, scroll);
            });
    }

    private void OnReview(EntityUid uid, OperatorContractComponent contract, UseInHandEvent args)
    {
        var user = args.User;
        if (args.Handled || contract.Price <= 0 || !TryComp<ActorComponent>(user, out var actor))
            return;

        args.Handled = true;
        if (contract.Complete || contract.Issuer == user || (contract.Client != null && contract.Client != user))
        {
            _popup.PopupEntity(Loc.GetString(contract.Complete ? "operators-contract-completed" : contract.Client != null ? "operators-contract-client-only" : "operators-contract-own"), uid, user);
            return;
        }

        var session = actor.PlayerSession;
        var confirming = contract.Client == user;
        _dialog.OpenConfirmationDialog(session,
            Loc.GetString(confirming ? "operators-contract-completion-title" : "operators-contract-review-title"),
            Loc.GetString(confirming ? "operators-contract-release" : "operators-contract-submit"), Loc.GetString("operators-contract-cancel"),
            () =>
            {
                if (session.AttachedEntity != user || !_mob.IsAlive(user) || TerminatingOrDeleted(uid) ||
                    contract.Complete || contract.Issuer == user || contract.Client != (confirming ? user : (EntityUid?) null) || !_hands.EnumerateHeld(user).Contains(uid))
                    return;

                if (confirming)
                {
                    if (contract.Issuer is not {} issuer || !_currency.TryAdjustContractBottlecaps(issuer, contract.Price))
                    {
                        _popup.PopupEntity(Loc.GetString("operators-contract-bank-unavailable"), uid, user);
                        return;
                    }

                    contract.Complete = true;
                    _appearance.SetData(uid, OperatorContractVisuals.Stage, OperatorContractStage.Paid);
                    _popup.PopupEntity(Loc.GetString("operators-contract-completed"), uid, user);
                    return;
                }

                if (!_currency.TryAdjustContractBottlecaps(user, -contract.Price))
                {
                    _popup.PopupEntity(Loc.GetString("operators-contract-bank-short"), uid, user);
                    return;
                }

                contract.Client = user;
                _appearance.SetData(uid, OperatorContractVisuals.Stage, OperatorContractStage.Accepted);
                _popup.PopupEntity(Loc.GetString("operators-contract-held"), uid, user);
            }, () => {}, body: Loc.GetString("operators-contract-document", ("price", contract.Price), ("job", contract.Job), ("issuer", contract.Signature)));
    }
}

