using Content.Server.Hands.Systems;
using Content.Server.Storage.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Storage;
using Content.Shared.Eye.Blinding.Systems;
using Content.Shared._ReclamationProject.Blindness;
using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Content.Shared.Eye.Blinding.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Traits.Assorted.Components;
using Robust.Shared.Timing;

namespace Content.Server._ReclamationProject.Blindness;

public sealed class BlindConcentrationSystem : EntitySystem
{
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly StorageSystem _storage = default!;
    [Dependency] private readonly HandsSystem _hands = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly MobStateSystem _mob = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<BlindConcentrationComponent, MapInitEvent>(OnInit);
        SubscribeLocalEvent<BlindConcentrationComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<BlindConcentrationComponent, BlindConcentrateActionEvent>(OnAction);
        SubscribeLocalEvent<BlindConcentrationComponent, BlindConcentrateDoAfterEvent>(OnFinished);
        SubscribeLocalEvent<BlindConcentrationComponent, MoveEvent>(OnMove);
        SubscribeLocalEvent<BlindConcentrationComponent, ToggleEyesActionEvent>(OnEyesChanged, after: new[] { typeof(EyeClosingSystem) });
        SubscribeLocalEvent<BlindConcentrationComponent, MobStateChangedEvent>(OnMobState);
    }

    private void OnInit(EntityUid uid, BlindConcentrationComponent comp, MapInitEvent args)
    {
        _actions.AddAction(uid, ref comp.Action, "ReclamationActionBlindConcentrate");
        // Exclusive loadouts delete the default bag and its contents later in this tick.
        // Wait for equipment selection to finish before creating the one starting blindfold.
        Timer.Spawn(0, () =>
        {
            if (TerminatingOrDeleted(uid) ||
                !TryComp<BlindConcentrationComponent>(uid, out var current) || current != comp)
                return;
            var blindfold = Spawn("ClothingEyesBlindfold", Transform(uid).Coordinates);
            if (!TryStoreBlindfold(uid, blindfold))
                _hands.TryPickupAnyHand(uid, blindfold, false, false, false);
        });
    }

    private bool TryStoreBlindfold(EntityUid uid, EntityUid blindfold)
    {
        var slots = _inventory.GetSlotEnumerator(uid, SlotFlags.BACK);
        while (slots.MoveNext(out var slot))
        {
            if (slot.ContainedEntity is not { } bag || !TryComp<StorageComponent>(bag, out var storage))
                continue;
            if (storage.Container.Contains(blindfold) ||
                _storage.Insert(bag, blindfold, out _, null, storage, false, false))
                return true;
        }
        return false;
    }

    private void OnShutdown(EntityUid uid, BlindConcentrationComponent comp, ComponentShutdown args)
    {
        _doAfter.Cancel(comp.DoAfter);
        _actions.RemoveAction(comp.Action);
    }

    private void OnAction(EntityUid uid, BlindConcentrationComponent comp, BlindConcentrateActionEvent args)
    {
        if (args.Handled || args.Performer != uid)
            return;
        args.Handled = true;
        if (comp.Concentrating)
        {
            Stop(uid, comp);
            return;
        }
        if (!TryComp<PermanentBlindnessComponent>(uid, out var trait) || trait.Blindness != 0 ||
            !_mob.IsAlive(uid))
            return;

        var progress = comp.Progress(_timing.CurTime);
        comp.ClosedEyeBonus = TryComp<EyeClosingComponent>(uid, out var eyes) && eyes.EyesClosed;
        if (!StartBar(uid, comp, progress))
            return;
        comp.StartProgress = progress;
        comp.TransitionStart = _timing.CurTime;
        comp.Concentrating = true;
        _actions.SetToggled(comp.Action, true);
        Dirty(uid, comp);
    }

    private bool StartBar(EntityUid uid, BlindConcentrationComponent comp, float progress)
    {
        var delay = TimeSpan.FromSeconds(Math.Max(0.1f, (1f - progress) * comp.BuildDuration));
        var doAfter = new DoAfterArgs(EntityManager, uid, delay, new BlindConcentrateDoAfterEvent(), uid)
        {
            BreakOnMove = true,
            MovementThreshold = 0.01f,
            NeedHand = false,
            MultiplyDelay = false,
            Hidden = true,
            CancelDuplicate = false
        };
        if (!_doAfter.TryStartDoAfter(doAfter, out var id))
            return false;
        comp.DoAfter = id;
        return true;
    }

    private void OnEyesChanged(EntityUid uid, BlindConcentrationComponent comp, ToggleEyesActionEvent args)
    {
        if (!comp.Concentrating)
            return;
        var progress = comp.Progress(_timing.CurTime);
        var id = comp.DoAfter;
        comp.DoAfter = null;
        _doAfter.Cancel(id);
        comp.StartProgress = progress;
        comp.TransitionStart = _timing.CurTime;
        comp.ClosedEyeBonus = TryComp<EyeClosingComponent>(uid, out var eyes) && eyes.EyesClosed;
        if (progress < 1f && !StartBar(uid, comp, progress))
            Stop(uid, comp);
        Dirty(uid, comp);
    }

    private void OnFinished(EntityUid uid, BlindConcentrationComponent comp, BlindConcentrateDoAfterEvent args)
    {
        if (args.Handled || comp.DoAfter is not { } id || id.Index != args.DoAfter.Index)
            return;
        args.Handled = true;
        comp.DoAfter = null;
        if (args.Cancelled)
            Stop(uid, comp);
        // Fully concentrated perception persists until movement or toggling off.
    }

    private void OnMove(EntityUid uid, BlindConcentrationComponent comp, ref MoveEvent args)
    {
        if (comp.Concentrating && !args.OnlyRotation)
            Stop(uid, comp);
    }

    private void OnMobState(EntityUid uid, BlindConcentrationComponent comp, ref MobStateChangedEvent args)
    {
        if (!_mob.IsAlive(uid))
            Stop(uid, comp);
    }

    private void Stop(EntityUid uid, BlindConcentrationComponent comp)
    {
        if (!comp.Concentrating)
            return;
        comp.StartProgress = comp.Progress(_timing.CurTime);
        comp.TransitionStart = _timing.CurTime;
        comp.Concentrating = false;
        var id = comp.DoAfter;
        comp.DoAfter = null;
        _doAfter.Cancel(id);
        _actions.SetToggled(comp.Action, false);
        Dirty(uid, comp);
    }
}
