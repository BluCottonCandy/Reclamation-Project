using Content.Shared._Misfits.Special;
using Content.Shared._Misfits.Clothing;
using Robust.Shared.Network;

namespace Content.Shared.Inventory;

public partial class InventorySystem
{
    [Dependency] private readonly SharedSpecialSystem _backpackSpecial = default!;
    [Dependency] private readonly INetManager _backpackNet = default!;

    private void InitializeStrengthBackpackSlot()
    {
        SubscribeLocalEvent<SpecialChangedEvent>(OnBackpackStrengthChanged);
        SubscribeLocalEvent<SpecialShutdownEvent>(OnBackpackSpecialShutdown);
    }

    private void OnBackpackStrengthChanged(ref SpecialChangedEvent args) => RefreshStrengthBackpackSlot(args.ChangedEntity);
    private void OnBackpackSpecialShutdown(ref SpecialShutdownEvent args) => RefreshStrengthBackpackSlot(args.Entity, true);

    protected bool HasStrengthBackpackSlot(EntityUid uid, SlotDefinition slot)
        => slot.Name == "back2" && _backpackSpecial.HasRequirement(uid, SpecialStat.Strength, 10);

    protected bool AreSlotDepsMet(EntityUid uid, SlotDefinition slot, bool ignoreStrength = false)
    {
        if (!ignoreStrength && HasStrengthBackpackSlot(uid, slot) || slot.DependsOn == null)
            return true;
        if (!TryGetSlotEntity(uid, slot.DependsOn, out var item) || item == null)
            return false;
        if (slot.DependsOnComponents != null)
            foreach (var (_, entry) in slot.DependsOnComponents)
                if (!HasComp(item.Value, entry.Component.GetType()))
                    return false;
        return true;
    }

    protected virtual void RefreshStrengthBackpackSlot(EntityUid uid, bool ignoreStrength = false)
    {
        if (!_backpackNet.IsServer || TerminatingOrDeleted(uid) ||
            !TryGetSlot(uid, "back2", out var slot) || AreSlotDepsMet(uid, slot, ignoreStrength))
            return;
        TryUnequip(uid, uid, "back2", silent: true, force: true);
    }
}
