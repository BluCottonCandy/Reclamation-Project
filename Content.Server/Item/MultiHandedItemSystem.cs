using Content.Server.Hands.Systems;
using Content.Server.Inventory;
using Content.Shared.Hands;
using Content.Shared.Item;
using Content.Shared._Misfits.Special;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory.VirtualItem;
using System.Linq;

namespace Content.Server.Item;

public sealed class MultiHandedItemSystem : SharedMultiHandedItemSystem
{
    [Dependency] private readonly SharedHandsSystem _strengthHands = default!;
    [Dependency] private readonly VirtualItemSystem _virtualItem = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SpecialChangedEvent>(OnSpecialChanged);
    }

    private void OnSpecialChanged(ref SpecialChangedEvent args)
    {
        var user = args.ChangedEntity;
        if (TerminatingOrDeleted(user))
            return;
        var held = _strengthHands.EnumerateHeld(user).ToArray();
        foreach (var item in held)
        {
            if (!TryComp<MultiHandedItemComponent>(item, out var multi) || multi.MinimumStrengthForOneHand == null)
                continue;
            var occupied = 1 + held.Count(other => TryComp<VirtualItemComponent>(other, out var virtualItem) && virtualItem.BlockingEntity == item);
            if (occupied == GetHandsNeeded(user, multi))
                continue;
            // Regrip using the normal pickup rules; if the new requirement cannot fit, leave it safely on the floor.
            if (_strengthHands.TryDrop(user, item))
                _strengthHands.TryPickupAnyHand(user, item);
        }
    }

    protected override void OnEquipped(EntityUid uid, MultiHandedItemComponent component, GotEquippedHandEvent args)
    {
        for (var i = 0; i < GetHandsNeeded(args.User, component) - 1; i++)
        {
            _virtualItem.TrySpawnVirtualItemInHand(uid, args.User);
        }
    }

    protected override void OnUnequipped(EntityUid uid, MultiHandedItemComponent component, GotUnequippedHandEvent args)
    {
        _virtualItem.DeleteInHandsMatching(args.User, uid);
    }
}
