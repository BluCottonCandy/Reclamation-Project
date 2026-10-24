using Content.Shared._Shitmed.Medical.Surgery.Conditions;
using Content.Shared.Body.Systems;
using Robust.Shared.Containers;
using Robust.Shared.GameStates;

namespace Content.Shared._ReclamationProject.Institute.Relay;

/// <summary>
/// Surgery condition: the part's organ slot must be empty (e.g. the brain has to come out first).
/// Checks the slot's container, which both client and server can see.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class SurgeryOrganSlotEmptyConditionComponent : Component
{
    [DataField]
    public string SlotId = "brain";
}

public sealed class SurgeryOrganSlotEmptyConditionSystem : EntitySystem
{
    [Dependency] private SharedContainerSystem _container = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SurgeryOrganSlotEmptyConditionComponent, SurgeryValidEvent>(OnValid);
    }

    private void OnValid(Entity<SurgeryOrganSlotEmptyConditionComponent> ent, ref SurgeryValidEvent args)
    {
        if (_container.TryGetContainer(args.Part, SharedBodySystem.GetOrganContainerId(ent.Comp.SlotId), out var slot)
            && slot.ContainedEntities.Count > 0)
            args.Cancelled = true;
    }
}
