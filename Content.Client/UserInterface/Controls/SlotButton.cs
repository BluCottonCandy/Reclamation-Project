using System.Numerics;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface.Controls;
using static Content.Client.Inventory.ClientInventorySystem;

namespace Content.Client.UserInterface.Controls
{
    public sealed class SlotButton : SlotControl
    {
        private readonly TextureRect? _emptyIcon;
        public string? AboveSlot { get; }

        public SlotButton() { }

        public SlotButton(SlotData slotData)
        {
            AboveSlot = slotData.SlotDef.AboveSlot;
            if (slotData.SlotDef.SlotTooltip is {} tooltip)
                ButtonRect.ToolTip = Loc.GetString(tooltip);

            if (slotData.SlotDef.EmptySlotIcon is {} icon)
            {
                var sprites = IoCManager.Resolve<IEntityManager>().System<SpriteSystem>();
                _emptyIcon = new TextureRect
                {
                    Texture = sprites.GetPrototypeIcon(icon.Id).Default,
                    TextureScale = new Vector2(1.5f),
                    Modulate = Color.White.WithAlpha(0.55f),
                    HorizontalAlignment = HAlignment.Center,
                    VerticalAlignment = VAlignment.Center,
                    MouseFilter = MouseFilterMode.Ignore,
                };
                AddChild(_emptyIcon);
                _emptyIcon.SetPositionInParent(1);
            }
            ButtonTexturePath = _emptyIcon == null ? slotData.TextureName : slotData.FullTextureName;
            FullButtonTexturePath = slotData.FullTextureName;
            Blocked = slotData.Blocked;
            Highlight = slotData.Highlighted;
            StorageTexturePath = "Slots/back";
            SlotName = slotData.SlotName;
        }

        public override void SetEntity(EntityUid? ent)
        {
            base.SetEntity(ent);
            if (_emptyIcon != null)
                _emptyIcon.Visible = ent == null;
        }
    }
}
