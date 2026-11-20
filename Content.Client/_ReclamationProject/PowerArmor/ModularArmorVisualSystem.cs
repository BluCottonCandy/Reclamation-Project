using Content.Client.Alerts;
using Content.Client.Clothing;
using Content.Client.Items.Systems;
using Content.Shared._ReclamationProject.PowerArmor;
using Content.Shared.Clothing;
using Content.Shared.Clothing.Components;
using Content.Shared.Inventory;
using Robust.Client.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._ReclamationProject.PowerArmor;

public sealed class ModularArmorVisualSystem : EntitySystem
{
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly ItemSystem _items = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    private readonly HashSet<EntityUid> _changedFrames = new();
    private const string ModSprite = "_ReclamationProject/Clothing/AtomCats/armor_mods.rsi";

    public override void Initialize()
    {
        SubscribeLocalEvent<ModularPowerArmorComponent, GetEquipmentVisualsEvent>(OnWorn, after: new[] { typeof(ClientClothingSystem) });
        SubscribeLocalEvent<AttachedClothingComponent, GetEquipmentVisualsEvent>(OnHelmetWorn, after: new[] { typeof(ClientClothingSystem) });
        SubscribeLocalEvent<ModularPowerArmorComponent, AfterAutoHandleStateEvent>(OnFrameChanged);
        SubscribeLocalEvent<PowerArmorPlateComponent, AfterAutoHandleStateEvent>(OnPlateChanged);
        SubscribeLocalEvent<InventoryComponent, UpdateAlertSpriteEvent>(OnAlert);
    }

    private void OnWorn(Entity<ModularPowerArmorComponent> ent, ref GetEquipmentVisualsEvent ev)
    {
        if (ev.Slot != "outerClothing") return;
        ev.Layers.Clear();
        ev.Layers.Add(("pa-frame", new PrototypeLayerData { RsiPath = ent.Comp.FrameSprite, State = "equipped-OUTERCLOTHING" }));
        foreach (var slot in PowerArmorPlates.Slots)
        {
            if (slot != ArmorPlateSlot.Helmet && ent.Comp.Visuals.TryGetValue(slot, out var path))
                ev.Layers.Add(("pa-" + slot, new PrototypeLayerData { RsiPath = path, State = slot.ToString().ToLowerInvariant() }));
        }
        foreach (var (slot, kind) in ent.Comp.ModVisuals)
            if (slot != ArmorPlateSlot.Helmet)
                AddWornMod(ev, kind);
    }

    private void OnHelmetWorn(Entity<AttachedClothingComponent> ent, ref GetEquipmentVisualsEvent ev)
    {
        if (ev.Slot != "head" || !TryComp<ModularPowerArmorComponent>(ent.Comp.AttachedUid, out var frame))
            return;
        if (frame.PaintedHelmet && frame.Visuals.TryGetValue(ArmorPlateSlot.Helmet, out var finish) && ev.Layers.Count > 0)
            ev.Layers[0] = (ev.Layers[0].Item1, new PrototypeLayerData { RsiPath = finish, State = "helmet" });
        if (frame.ModVisuals.TryGetValue(ArmorPlateSlot.Helmet, out var visor))
            AddWornMod(ev, visor);
    }

    private static void AddWornMod(GetEquipmentVisualsEvent ev, ArmorModKind kind) =>
        ev.Layers.Add(("pa-mod-" + kind, new PrototypeLayerData { RsiPath = ModSprite, State = ModState(kind, "worn") }));

    private static string ModState(ArmorModKind kind, string view) => kind.ToString().ToLowerInvariant() + "-" + view;

    private void DisplayMod(SpriteComponent sprite, string key, ArmorModKind? kind, string view)
    {
        var index = sprite.LayerMapReserveBlank(key);
        if (kind is {} mod)
            sprite.LayerSetSprite(index, new SpriteSpecifier.Rsi(new ResPath("/Textures/" + ModSprite), ModState(mod, view)));
        sprite.LayerSetVisible(index, kind.HasValue);
    }

    private void OnFrameChanged(Entity<ModularPowerArmorComponent> ent, ref AfterAutoHandleStateEvent ev)
    {
        _changedFrames.Add(ent.Owner);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        // The wearer, the suit and its container may all arrive in the same network update.
        foreach (var uid in _changedFrames)
        {
            if (TryComp<ModularPowerArmorComponent>(uid, out var frame))
                RefreshFrame((uid, frame));
        }
        _changedFrames.Clear();
    }

    private void RefreshFrame(Entity<ModularPowerArmorComponent> ent)
    {
        _items.VisualsChanged(ent.Owner);
        var wearer = Transform(ent).ParentUid;
        if (HasComp<InventoryComponent>(wearer) && _inventory.TryGetSlotEntity(wearer, "head", out var helmet) &&
            TryComp<AttachedClothingComponent>(helmet, out var attached) && attached.AttachedUid == ent.Owner)
            _items.VisualsChanged(helmet.Value);
        if (!TryComp<SpriteComponent>(ent, out var sprite)) return;
        sprite.LayerSetSprite(0, new SpriteSpecifier.Rsi(new ResPath("/Textures/" + ent.Comp.FrameSprite), "equipped-OUTERCLOTHING"));
        foreach (var slot in PowerArmorPlates.Slots)
        {
            var key = "pa-item-" + slot;
            var index = sprite.LayerMapReserveBlank(key);
            var visible = ent.Comp.Visuals.TryGetValue(slot, out var path);
            if (visible)
                sprite.LayerSetSprite(index, new SpriteSpecifier.Rsi(new ResPath("/Textures/" + path), slot.ToString().ToLowerInvariant()));
            sprite.LayerSetVisible(index, visible);
        }
        foreach (var slot in PowerArmorPlates.Slots)
            DisplayMod(sprite, "pa-item-mod-" + slot, ent.Comp.ModVisuals.TryGetValue(slot, out var kind) ? kind : null, "worn");
    }

    private void OnPlateChanged(Entity<PowerArmorPlateComponent> ent, ref AfterAutoHandleStateEvent ev)
    {
        if (TryComp<SpriteComponent>(ent, out var sprite) && _prototypes.TryIndex<PowerArmorModelPrototype>(ent.Comp.Model.Id, out var model))
        {
            sprite.LayerSetSprite(0, new SpriteSpecifier.Rsi(new ResPath("/Textures/" + (ent.Comp.PaintFinish ?? model.Sprite)), ent.Comp.Slot.ToString().ToLowerInvariant() + "-item"));
            DisplayMod(sprite, "pa-fitted-mod", ent.Comp.FittedMod, "piece");
        }
    }

    private void OnAlert(Entity<InventoryComponent> ent, ref UpdateAlertSpriteEvent args)
    {
        if (args.Alert.ID != "ModularPowerArmorRating" || !_inventory.TryGetSlotEntity(ent, "outerClothing", out var suit)
            || !TryComp<ModularPowerArmorComponent>(suit, out var frame)) return;
        var sprite = args.SpriteViewEnt.Comp;
        sprite.LayerSetState("pa-tens", (frame.Rating / 10).ToString());
        sprite.LayerSetState("pa-ones", (frame.Rating % 10).ToString());
    }
}
