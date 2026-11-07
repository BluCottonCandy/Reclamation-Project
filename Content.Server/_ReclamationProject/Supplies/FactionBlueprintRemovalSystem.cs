using Content.Shared._Misfits.Crafting;

namespace Content.Server._ReclamationProject.Supplies;

/// <summary>Retires physical blueprint items, including old inventories and deserialized bunker contents.</summary>
public sealed class FactionBlueprintRemovalSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        // Startup also catches components restored from old saves or added to custom entities.
        SubscribeLocalEvent<BlueprintComponent, ComponentStartup>(OnBlueprintStartup);
    }

    private void OnBlueprintStartup(EntityUid uid, BlueprintComponent component, ComponentStartup args)
        => QueueDel(uid);

    /// <summary>
    /// Strip blueprints synchronously before saving or exposing a loaded room. Unlike display conversion,
    /// this walks through living entities too, so their inventories cannot retain a blueprint in a snapshot.
    /// </summary>
    public void PurgeTree(EntityUid root)
    {
        var pending = new Stack<EntityUid>();
        var visited = new HashSet<EntityUid>();
        pending.Push(root);
        while (pending.TryPop(out var uid))
        {
            if (!visited.Add(uid) || TerminatingOrDeleted(uid) || !TryComp<TransformComponent>(uid, out var transform))
                continue;
            var children = transform.ChildEnumerator;
            while (children.MoveNext(out var child))
                pending.Push(child);
            if (HasComp<BlueprintComponent>(uid))
                Del(uid);
        }
    }
}
