// #Reclamation Project - admin Rejuvenate also regrows missing limbs and organs.
using System.Linq;
using Content.Server.Body.Components;
using Content.Shared.Body.Components;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;
using Content.Shared.Body.Prototypes;
using Content.Shared.Body.Systems;
using Content.Shared.Rejuvenate;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server._ReclamationProject.Medical;

/// <summary>
/// Rejuvenate puts back every limb and organ the body started with that is now missing.
/// Anything that was replaced (a robotic arm, a Doomarm, a cybernetic organ) is left alone, and so is
/// everything attached to a replacement. Heads and brains are never regrown: the player's mind may be in a severed head.
/// </summary>
public sealed class RejuvenateRegrowSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly SharedBodySystem _body = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;

    public override void Initialize()
    {
        // Before the other rejuvenate handlers, so the new parts get healed and refreshed with the rest of the body.
        SubscribeLocalEvent<BodyComponent, RejuvenateEvent>(OnRejuvenate,
            before: new[] { typeof(TraumaSystem) });
    }

    private void OnRejuvenate(Entity<BodyComponent> ent, ref RejuvenateEvent args) => Regrow(ent);

    /// <summary>Puts back missing original limbs and organs. Returns how many were added.</summary>
    public int Regrow(Entity<BodyComponent> ent)
    {
        if (ent.Comp.Prototype is not { } protoId
            || !_prototypes.TryIndex(protoId, out BodyPrototype? prototype)
            || ent.Comp.RootContainer.ContainedEntity is not { } root
            || !prototype.Slots.TryGetValue(prototype.Root, out var rootSlot)
            || MetaData(root).EntityPrototype?.ID != rootSlot.Part)
            return 0;

        var added = RegrowOrgans(root, rootSlot.Organs);

        // Walk the body layout outwards from the root, like the body was first built.
        var visited = new HashSet<string> { prototype.Root };
        var frontier = new Queue<(string Slot, EntityUid Part)>();
        frontier.Enqueue((prototype.Root, root));

        while (frontier.TryDequeue(out var current))
        {
            foreach (var connection in prototype.Slots[current.Slot].Connections)
            {
                if (!visited.Add(connection))
                    continue;

                var slot = prototype.Slots[connection];
                if (slot.Part is null)
                    continue;

                EntityUid child;
                if (_container.TryGetContainer(current.Part, SharedBodySystem.GetPartSlotContainerId(connection), out var container)
                    && container.ContainedEntities.FirstOrDefault() is { Valid: true } existing)
                {
                    // Something is there. A replacement keeps its own subtree.
                    if (MetaData(existing).EntityPrototype?.ID != slot.Part)
                        continue;
                    child = existing;
                }
                else
                {
                    var spawned = Spawn(slot.Part, Transform(ent).Coordinates);
                    if (!TryComp<BodyPartComponent>(spawned, out var part)
                        || part.PartType == BodyPartType.Head
                        || !_body.TryCreatePartSlotAndAttach(current.Part, connection, spawned, part.PartType))
                    {
                        Del(spawned);
                        continue;
                    }
                    child = spawned;
                    added++;
                }

                added += RegrowOrgans(child, slot.Organs);
                frontier.Enqueue((connection, child));
            }
        }

        return added;
    }

    private int RegrowOrgans(EntityUid part, Dictionary<string, string> organs)
    {
        var added = 0;
        foreach (var (slotId, organProto) in organs)
        {
            if (_container.TryGetContainer(part, SharedBodySystem.GetOrganContainerId(slotId), out var container)
                && container.ContainedEntities.Count > 0)
                continue;

            var organ = Spawn(organProto, Transform(part).Coordinates);
            if (HasComp<BrainComponent>(organ)
                || !_body.TryCreateOrganSlot(part, slotId, out _)
                || !_container.TryGetContainer(part, SharedBodySystem.GetOrganContainerId(slotId), out container)
                || !_container.Insert(organ, container))
            {
                Del(organ);
                continue;
            }
            added++;
        }
        return added;
    }
}
