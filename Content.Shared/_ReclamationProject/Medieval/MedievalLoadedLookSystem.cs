// #Reclamation Project - medieval weapons: crossbow and sling show their loaded sprite (world and in hand).
using Content.Shared.Item;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Robust.Shared.Containers;

namespace Content.Shared._ReclamationProject.Medieval;

public sealed class MedievalLoadedLookSystem : EntitySystem
{
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedItemSystem _item = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MedievalLoadedLookComponent, MapInitEvent>((u, c, _) => Refresh(u, c));
        SubscribeLocalEvent<MedievalLoadedLookComponent, EntInsertedIntoContainerMessage>((u, c, _) => Refresh(u, c));
        SubscribeLocalEvent<MedievalLoadedLookComponent, EntRemovedFromContainerMessage>((u, c, _) => Refresh(u, c));
        // run after WieldableSystem, which sets/restores the "wielded" held prefix itself
        SubscribeLocalEvent<MedievalLoadedLookComponent, ItemWieldedEvent>(OnWielded, after: new[] { typeof(WieldableSystem) });
        SubscribeLocalEvent<MedievalLoadedLookComponent, ItemUnwieldedEvent>(OnUnwielded, after: new[] { typeof(WieldableSystem) });
    }

    public bool IsLoaded(EntityUid uid, MedievalLoadedLookComponent comp)
    {
        return _container.TryGetContainer(uid, comp.Container, out var cont) && cont.ContainedEntities.Count > 0;
    }

    private void OnWielded(EntityUid uid, MedievalLoadedLookComponent comp, ref ItemWieldedEvent args) => Refresh(uid, comp, true);

    private void OnUnwielded(EntityUid uid, MedievalLoadedLookComponent comp, ItemUnwieldedEvent args) => Refresh(uid, comp, false);

    private void Refresh(EntityUid uid, MedievalLoadedLookComponent comp, bool? wieldedOverride = null)
    {
        var loaded = IsLoaded(uid, comp);
        _appearance.SetData(uid, MedievalLookVisuals.State, loaded ? comp.State : "icon");
        var wielded = wieldedOverride ?? (TryComp<WieldableComponent>(uid, out var w) && w.Wielded);
        string? prefix = loaded ? comp.State : null;
        if (wielded)
            prefix = prefix == null ? "wielded" : $"{prefix}-wielded";
        _item.SetHeldPrefix(uid, prefix);
    }
}
