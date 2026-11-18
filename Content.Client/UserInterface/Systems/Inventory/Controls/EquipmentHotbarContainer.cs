using Content.Client.UserInterface.Controls;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.UserInterface.Systems.Inventory.Controls;

/// <summary>Stack optional equipment buttons directly above their normal equipment slot.</summary>
public sealed class EquipmentHotbarContainer : ItemSlotButtonContainer
{
    private readonly Dictionary<string, BoxContainer> _columns = new();

    public override SlotControl? AddButton(SlotControl button)
    {
        if (button is not SlotButton slot || !Buttons.TryAdd(slot.SlotName, slot))
            return null;

        var anchor = slot.AboveSlot ?? slot.SlotName;
        if (!_columns.TryGetValue(anchor, out var column))
        {
            column = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Vertical,
                VerticalAlignment = VAlignment.Bottom,
                SeparationOverride = 0,
            };
            _columns.Add(anchor, column);
            AddChild(column);
            Columns = _columns.Count;
        }

        column.AddChild(slot);
        if (slot.AboveSlot != null)
            slot.SetPositionFirst();

        return slot;
    }

    public override void RemoveButton(SlotControl button)
    {
        if (button is not SlotButton slot || !Buttons.Remove(slot.SlotName))
            return;

        var anchor = slot.AboveSlot ?? slot.SlotName;
        var column = _columns[anchor];
        slot.Dispose();
        if (column.ChildCount != 0)
            return;

        _columns.Remove(anchor);
        column.Dispose();
        Columns = Math.Max(1, _columns.Count);
    }

    public override void ClearButtons()
    {
        base.ClearButtons();
        foreach (var column in _columns.Values)
            column.Dispose();

        _columns.Clear();
    }
}
