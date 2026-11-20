using Content.Shared._ReclamationProject.PowerArmor;
using Content.Shared.Clothing.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.DoAfter;
using Content.Shared.Hands;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Paint;
using Content.Shared.Popups;
using Content.Shared.RadialSelector;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._ReclamationProject.PowerArmor;

public sealed class ArmorStencilCanSystem : EntitySystem
{
    [Dependency] private readonly ModularPowerArmorSystem _armor = default!;
    [Dependency] private readonly ItemSlotsSystem _slots = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly IComponentFactory _factory = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<ArmorStencilCanComponent, BeforeRangedInteractEvent>(OnClick);
        SubscribeLocalEvent<ArmorStencilCanComponent, RadialSelectorSelectedMessage>(OnSelected);
        SubscribeLocalEvent<ArmorStencilCanComponent, BoundUIClosedEvent>((_, can, _) => can.Selection = null);
        SubscribeLocalEvent<ArmorStencilCanComponent, HandDeselectedEvent>((uid, _, ev) =>
            _ui.CloseUi(uid, RadialSelectorUiKey.Key, ev.User));
        SubscribeLocalEvent<ArmorStencilCanComponent, SprayArmorStencilEvent>(OnPaint);
    }

    private EntityUid? GetSurface(EntityUid clicked)
    {
        if (HasComp<PowerArmorPlateComponent>(clicked) || HasComp<ModularPowerArmorComponent>(clicked))
            return clicked;

        if (TryComp<AttachedClothingComponent>(clicked, out var attachment) &&
            HasComp<ModularPowerArmorComponent>(attachment.AttachedUid) &&
            TryComp<ClothingComponent>(clicked, out var clothing))
        {
            ArmorPlateSlot? section = clothing.InSlot switch
            {
                "head" => ArmorPlateSlot.Helmet,
                "gloves" => ArmorPlateSlot.Gloves,
                "shoes" => ArmorPlateSlot.Chest,
                _ => null,
            };
            return section is {} plate ? _slots.GetItemOrNull(attachment.AttachedUid, PowerArmorPlates.Container(plate)) : null;
        }

        if (HasComp<InventoryComponent>(clicked) && _inventory.TryGetSlotEntity(clicked, "outerClothing", out var worn) &&
            HasComp<ModularPowerArmorComponent>(worn))
            return worn;

        return null;
    }

    private bool CanSpray(EntityUid user, EntityUid can, EntityUid clicked) =>
        _hands.GetActiveItem(user) == can && !Deleted(clicked) && _interaction.InRangeAndAccessible(user, clicked);

    private void OnClick(Entity<ArmorStencilCanComponent> can, ref BeforeRangedInteractEvent ev)
    {
        if (ev.Handled || !ev.CanReach || ev.Target is not {} clicked ||
            !CanSpray(ev.User, can, clicked) || GetSurface(clicked) is not {} surface)
            return;

        ev.Handled = true;
        if (_ui.IsUiOpen(can.Owner, RadialSelectorUiKey.Key))
            return;

        var slot = CompOrNull<PowerArmorPlateComponent>(surface)?.Slot ?? ArmorPlateSlot.Chest;
        var preview = slot.ToString().ToLowerInvariant() + "-item";
        var choices = new List<RadialSelectorEntry>();
        foreach (var stencil in can.Comp.Stencils)
        {
            if (!_prototypes.Index(stencil).TryGetComponent<PaintComponent>(out var paint, _factory) ||
                paint.ArmorFinish is not {} finish)
                continue;
            choices.Add(new RadialSelectorEntry
            {
                Prototype = stencil.Id,
                Icon = new SpriteSpecifier.Rsi(new ResPath("/Textures/" + finish), preview),
            });
        }
        if (choices.Count == 0)
            return;

        _ui.SetUiState(can.Owner, RadialSelectorUiKey.Key, new RadialSelectorState(choices));
        if (_ui.TryOpenUi(can.Owner, RadialSelectorUiKey.Key, ev.User))
            can.Comp.Selection = (clicked, surface);
    }

    private void OnSelected(Entity<ArmorStencilCanComponent> can, ref RadialSelectorSelectedMessage ev)
    {
        if (can.Comp.Selection is not {} target || !_ui.IsUiOpen(can.Owner, RadialSelectorUiKey.Key, ev.Actor))
            return;
        _ui.CloseUi(can.Owner, RadialSelectorUiKey.Key, ev.Actor);
        EntProtoId stencil = ev.SelectedItem;
        if (!can.Comp.Stencils.Contains(stencil) || !CanSpray(ev.Actor, can, target.Clicked) ||
            GetSurface(target.Clicked) != target.Surface ||
            !_prototypes.Index(stencil).TryGetComponent<PaintComponent>(out var paint, _factory))
            return;

        var spray = new SprayArmorStencilEvent { Surface = GetNetEntity(target.Surface), Stencil = stencil };
        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, ev.Actor, paint.Delay, spray, can, target.Clicked, can)
        {
            NeedHand = true,
            BreakOnMove = true,
        });
    }

    private void OnPaint(Entity<ArmorStencilCanComponent> can, ref SprayArmorStencilEvent ev)
    {
        if (ev.Handled || ev.Cancelled || ev.Target is not {} clicked || !CanSpray(ev.User, can, clicked) ||
            GetSurface(clicked) is not {} surface || GetNetEntity(surface) != ev.Surface ||
            !can.Comp.Stencils.Contains(ev.Stencil) ||
            !_prototypes.Index(ev.Stencil).TryGetComponent<PaintComponent>(out var paint, _factory) ||
            paint.ArmorFinish is not {} finish)
            return;

        ev.Handled = true;
        _armor.ApplyPaint(surface, finish, paint.Color);
        _audio.PlayPvs(paint.Spray, can);
        _popup.PopupEntity(Loc.GetString("paint-success", ("target", surface)), ev.User, ev.User);
    }
}
