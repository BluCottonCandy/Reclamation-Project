using Content.Shared.Hands;
using Content.Shared._Misfits.Special;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Popups;
using Robust.Shared.Timing;

namespace Content.Shared.Item;

public abstract class SharedMultiHandedItemSystem : EntitySystem
{
    [Dependency] private readonly SharedSpecialSystem _special = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        SubscribeLocalEvent<MultiHandedItemComponent, GettingPickedUpAttemptEvent>(OnAttemptPickup);
        SubscribeLocalEvent<MultiHandedItemComponent, VirtualItemDeletedEvent>(OnVirtualItemDeleted);
        SubscribeLocalEvent<MultiHandedItemComponent, GotEquippedHandEvent>(OnEquipped);
        SubscribeLocalEvent<MultiHandedItemComponent, GotUnequippedHandEvent>(OnUnequipped);
    }

    protected abstract void OnEquipped(EntityUid uid, MultiHandedItemComponent component, GotEquippedHandEvent args);
    protected abstract void OnUnequipped(EntityUid uid, MultiHandedItemComponent component, GotUnequippedHandEvent args);

    public int GetHandsNeeded(EntityUid user, MultiHandedItemComponent component)
    {
        return component.MinimumStrengthForOneHand is { } minimum &&
            _special.HasRequirement(user, SpecialStat.Strength, minimum) ? 1 : component.HandsNeeded;
    }

    private void OnAttemptPickup(EntityUid uid, MultiHandedItemComponent component, GettingPickedUpAttemptEvent args)
    {
        if (TryComp<HandsComponent>(args.User, out var hands) && hands.CountFreeHands() >= GetHandsNeeded(args.User, component))
            return;

        args.Cancel();
        if (_timing.IsFirstTimePredicted)
        {
            _popup.PopupCursor(Loc.GetString("multi-handed-item-pick-up-fail",
                ("number", GetHandsNeeded(args.User, component) - 1), ("item", uid)), args.User);
        }
    }

    private void OnVirtualItemDeleted(EntityUid uid, MultiHandedItemComponent component, VirtualItemDeletedEvent args)
    {
        // A strong carrier can release a voluntary wield grip without dropping the item.
        if (args.BlockingEntity != uid || GetHandsNeeded(args.User, component) <= 1)
            return;

        _hands.TryDrop(args.User, uid);
    }
}
