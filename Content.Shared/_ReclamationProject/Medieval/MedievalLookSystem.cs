// #Reclamation Project - medieval weapons: keeps sprite + in-hand sprite in step with toggle and wield.
using Content.Shared.Item;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;

namespace Content.Shared._ReclamationProject.Medieval;

public sealed class MedievalLookSystem : EntitySystem
{
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedItemSystem _item = default!;
    [Dependency] private ItemToggleSystem _toggle = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MedievalLookComponent, MapInitEvent>((u, c, _) => Refresh(u, c));
        SubscribeLocalEvent<MedievalLookComponent, ItemToggledEvent>(OnToggled);
        // run after WieldableSystem, which sets/restores the "wielded" held prefix itself
        SubscribeLocalEvent<MedievalLookComponent, ItemWieldedEvent>(OnWielded, after: new[] { typeof(WieldableSystem) });
        SubscribeLocalEvent<MedievalLookComponent, ItemUnwieldedEvent>(OnUnwielded, after: new[] { typeof(WieldableSystem) });
    }

    private void OnToggled(EntityUid uid, MedievalLookComponent comp, ref ItemToggledEvent args) => Refresh(uid, comp);

    private void OnWielded(EntityUid uid, MedievalLookComponent comp, ref ItemWieldedEvent args) => Refresh(uid, comp, true);

    private void OnUnwielded(EntityUid uid, MedievalLookComponent comp, ItemUnwieldedEvent args) => Refresh(uid, comp, false);

    private void Refresh(EntityUid uid, MedievalLookComponent comp, bool? wieldedOverride = null)
    {
        var on = _toggle.IsActivated(uid);
        var wielded = wieldedOverride ?? (TryComp<WieldableComponent>(uid, out var w) && w.Wielded);

        string? state = null;
        if (on)
            state = wielded && comp.WieldedOnState != null ? comp.WieldedOnState : comp.OnState;

        _appearance.SetData(uid, MedievalLookVisuals.State, state ?? "icon");

        string? prefix = state;
        if (wielded)
            prefix = prefix == null ? "wielded" : $"{prefix}-wielded";
        _item.SetHeldPrefix(uid, prefix);
    }
}
