// #Misfits Change - Power armor tactical map action support
using Content.Shared.Actions;

namespace Content.Shared._Misfits.WastelandMap;

public sealed class WastelandMapActionSystem : EntitySystem
{
    [Dependency] private readonly ActionContainerSystem _actionContainer = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WastelandMapActionComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WastelandMapActionComponent, GetItemActionsEvent>(OnGetItemActions);
        SubscribeLocalEvent<WastelandMapActionComponent, ComponentRemove>(OnRemove);
    }

    private void OnMapInit(EntityUid uid, WastelandMapActionComponent component, MapInitEvent args)
    {
        _actionContainer.EnsureAction(uid, ref component.ActionEntity, component.Action);
    }

    private void OnGetItemActions(EntityUid uid, WastelandMapActionComponent component, GetItemActionsEvent args)
    {
        if (component.ActionEntity == null || args.SlotFlags != component.RequiredSlot)
            return;

        args.AddAction(component.ActionEntity.Value);
    }

    private void OnRemove(EntityUid uid, WastelandMapActionComponent component, ComponentRemove args)
    {
        _actions.RemoveAction(component.ActionEntity);
    }
}