using Content.Server.Humanoid;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared._ReclamationProject.Magic;
using Content.Shared.Damage;
using Content.Shared.Humanoid;
using Content.Shared.Inventory;
using Content.Shared.Strip.Components;
using Robust.Shared.Containers;
using Robust.Shared.Timing;

namespace Content.Server._ReclamationProject.Magic;

/// <summary>
/// #Reclamation Project - Mirror Image / Shell Game copies. Each copy is a real body of the caster's species
/// with the caster's face, hair, name and visible clothes, wandering about. It pops (a flash, then gone, with
/// everything it wears) the moment it takes any damage or its time runs out. It cannot be stripped, so the
/// copied clothes can never be taken.
/// </summary>
public sealed class MagicMirrorImageSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly HumanoidAppearanceSystem _appearance = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly MetaDataSystem _meta = default!;
    [Dependency] private readonly NPCSystem _npc = default!;
    [Dependency] private readonly MagicSystem _magic = default!;

    /// <summary>Only what people can see. No ID, pockets or suit storage (nothing worth copying).</summary>
    private static readonly string[] CopiedSlots =
    {
        "jumpsuit", "outerClothing", "head", "mask", "eyes", "ears", "neck", "gloves", "shoes", "back", "belt",
    };

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MagicMirrorImageComponent, DamageChangedEvent>(OnDamaged);
        SubscribeLocalEvent<MagicIllusionItemComponent, EntGotRemovedFromContainerMessage>(OnIllusionRemoved);
    }

    /// <summary>Swaps the spell's placeholder for a copy of the caster. Non-humanoid casters keep the placeholder.</summary>
    public void MakeLookalike(EntityUid caster, EntityUid placeholder)
    {
        if (!TryComp<MagicMirrorImageComponent>(placeholder, out var marker))
            return;
        var lifetime = TimeSpan.FromSeconds(marker.Lifetime);

        if (!HasComp<HumanoidAppearanceComponent>(caster) || MetaData(caster).EntityPrototype is not { } body)
        {
            marker.Expires = _timing.CurTime + lifetime;
            return;
        }

        var coords = Transform(placeholder).Coordinates;
        QueueDel(placeholder);

        var copy = Spawn(body.ID, coords);
        _appearance.CloneAppearance(caster, copy);
        _meta.SetEntityName(copy, Name(caster));

        foreach (var slot in CopiedSlots)
        {
            if (!_inventory.TryGetSlotEntity(caster, slot, out var worn) || MetaData(worn.Value).EntityPrototype is not { } wornProto)
                continue;
            var clothes = Spawn(wornProto.ID, coords);
            MarkIllusion(clothes);
            if (!_inventory.TryEquip(copy, clothes, slot, silent: true, force: true))
                QueueDel(clothes);
        }

        // Nobody can take the copied gear.
        RemComp<StrippableComponent>(copy);

        var image = EnsureComp<MagicMirrorImageComponent>(copy);
        image.Expires = _timing.CurTime + lifetime;

        // Wander about like a person would.
        var htn = new HTNComponent { RootTask = new HTNCompoundTask { Task = "IdleCompound" } };
        AddComp(copy, htn, overwrite: true);
        _npc.WakeNPC(copy, htn);
    }

    /// <summary>Marks an item and everything inside it (a backpack's fill) as illusion.</summary>
    private void MarkIllusion(EntityUid item)
    {
        EnsureComp<MagicIllusionItemComponent>(item);
        if (!TryComp<ContainerManagerComponent>(item, out var containers))
            return;
        foreach (var container in containers.Containers.Values)
        {
            foreach (var inner in container.ContainedEntities)
                MarkIllusion(inner);
        }
    }

    /// <summary>Taken off the copy, dropped, or pulled out of its backpack: the illusion fades.</summary>
    private void OnIllusionRemoved(Entity<MagicIllusionItemComponent> ent, ref EntGotRemovedFromContainerMessage args)
    {
        if (!TerminatingOrDeleted(ent) && !TerminatingOrDeleted(args.Container.Owner))
            QueueDel(ent);
    }

    private void OnDamaged(Entity<MagicMirrorImageComponent> ent, ref DamageChangedEvent args)
    {
        if (args.DamageIncreased)
            Pop(ent);
    }

    private void Pop(EntityUid uid)
    {
        if (TerminatingOrDeleted(uid))
            return;
        _magic.Flash(uid, MagicClass.Psychic, "Hit");
        QueueDel(uid); // what it wears goes with it
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<MagicMirrorImageComponent>();
        while (query.MoveNext(out var uid, out var image))
        {
            if (image.Expires != TimeSpan.Zero && image.Expires <= now)
                Pop(uid);
        }
    }
}
