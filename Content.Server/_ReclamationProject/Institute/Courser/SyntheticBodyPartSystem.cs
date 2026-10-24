using Content.Shared._ReclamationProject.Institute.Courser;
using Content.Shared.Body.Systems;
using Robust.Shared.Containers;

namespace Content.Server._ReclamationProject.Institute.Courser;

/// <summary>
/// Gives a Courser part or organ its synthetic name and description the first time it leaves the body.
/// </summary>
public sealed class SyntheticBodyPartSystem : EntitySystem
{
    [Dependency] private MetaDataSystem _meta = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SyntheticBodyPartComponent, EntGotRemovedFromContainerMessage>(OnRemoved);
    }

    private void OnRemoved(Entity<SyntheticBodyPartComponent> ent, ref EntGotRemovedFromContainerMessage args)
    {
        string slot = args.Container.ID;
        if (ent.Comp.Revealed || TerminatingOrDeleted(ent)
            || !slot.StartsWith(SharedBodySystem.PartSlotContainerIdPrefix)
            && !slot.StartsWith(SharedBodySystem.OrganSlotContainerIdPrefix))
            return;

        Reveal(ent);
    }

    public void Reveal(Entity<SyntheticBodyPartComponent> ent)
    {
        ent.Comp.Revealed = true;
        _meta.SetEntityName(ent, Loc.GetString(ent.Comp.RevealedName));
        _meta.SetEntityDescription(ent, Loc.GetString(ent.Comp.RevealedDescription));
    }
}
