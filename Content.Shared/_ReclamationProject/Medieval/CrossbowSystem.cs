// #Reclamation Project - medieval weapons: a crossbow must be spanned (cranked) before an arrow can go in.
using Content.Shared.Containers.ItemSlots;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Wieldable;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;

namespace Content.Shared._ReclamationProject.Medieval;

public sealed class CrossbowSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedHandsSystem _hands = default!;

    public override void Initialize()
    {
        base.Initialize();
        // before wielding: "use" on an uncranked crossbow cranks it; once cranked, "use" wields it as normal
        SubscribeLocalEvent<CrossbowComponent, UseInHandEvent>(OnUse, before: new[] { typeof(SharedGunSystem), typeof(WieldableSystem) });
        SubscribeLocalEvent<CrossbowComponent, InteractUsingEvent>(OnInteractUsing, before: new[] { typeof(SharedGunSystem), typeof(ItemSlotsSystem) });
        // crank it where it lies, foot in the stirrup: E on it, or the Crank verb
        SubscribeLocalEvent<CrossbowComponent, ActivateInWorldEvent>(OnActivate);
        SubscribeLocalEvent<CrossbowComponent, GetVerbsEvent<AlternativeVerb>>(OnVerbs);
        SubscribeLocalEvent<CrossbowComponent, ContainerIsInsertingAttemptEvent>(OnInsertAttempt);
        SubscribeLocalEvent<CrossbowComponent, CrossbowSpanDoAfterEvent>(OnSpanned);
        SubscribeLocalEvent<CrossbowComponent, GunShotEvent>(OnShot);
    }

    private void OnUse(EntityUid uid, CrossbowComponent comp, UseInHandEvent args)
    {
        if (args.Handled || comp.Spanned)
            return;
        args.Handled = TryStartSpan(uid, comp, args.User);
    }

    private void OnActivate(EntityUid uid, CrossbowComponent comp, ActivateInWorldEvent args)
    {
        if (args.Handled || comp.Spanned || !args.Complex)
            return;
        args.Handled = TryStartSpan(uid, comp, args.User);
    }

    private void OnVerbs(EntityUid uid, CrossbowComponent comp, GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || comp.Spanned)
            return;
        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("rp-crossbow-crank-verb"),
            Act = () => TryStartSpan(uid, comp, user),
        });
    }

    /// <summary>Starts cranking the string back. Works in hand or with the crossbow on the ground.</summary>
    public bool TryStartSpan(EntityUid uid, CrossbowComponent comp, EntityUid user)
    {
        if (comp.Spanned)
        {
            _popup.PopupClient(Loc.GetString("rp-crossbow-already-spanned"), uid, user);
            return false;
        }

        var held = _hands.IsHolding(user, uid);
        var doAfter = new DoAfterArgs(EntityManager, user, comp.SpanTime, new CrossbowSpanDoAfterEvent(), uid, target: uid, used: held ? uid : null)
        {
            BreakOnMove = true,
            BreakOnDropItem = held,
            NeedHand = true,
            BlockDuplicate = true,
            DistanceThreshold = 1.5f,
        };
        if (!_doAfter.TryStartDoAfter(doAfter))
            return false;
        _popup.PopupClient(Loc.GetString("rp-crossbow-spanning"), uid, user);
        _audio.PlayPredicted(comp.SpanSound, uid, user);
        return true;
    }

    private void OnInteractUsing(EntityUid uid, CrossbowComponent comp, InteractUsingEvent args)
    {
        if (args.Handled || comp.Spanned)
            return;
        // nothing goes in until the string is drawn
        args.Handled = true;
        _popup.PopupClient(Loc.GetString("rp-crossbow-not-spanned"), uid, args.User);
    }

    private void OnInsertAttempt(EntityUid uid, CrossbowComponent comp, ContainerIsInsertingAttemptEvent args)
    {
        if (!comp.Spanned)
            args.Cancel();
    }

    private void OnSpanned(EntityUid uid, CrossbowComponent comp, CrossbowSpanDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;
        args.Handled = true;
        comp.Spanned = true;
        Dirty(uid, comp);
        _audio.PlayPredicted(comp.SpannedSound, uid, args.User);
    }

    private void OnShot(EntityUid uid, CrossbowComponent comp, ref GunShotEvent args)
    {
        comp.Spanned = false;
        Dirty(uid, comp);
    }

    public void SetSpanned(EntityUid uid, CrossbowComponent comp, bool spanned)
    {
        comp.Spanned = spanned;
        Dirty(uid, comp);
    }
}
