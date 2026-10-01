using Content.Server.Mining.Components;
using Content.Server.Stack;
using Content.Shared.Destructible;
using Content.Shared.Mining;
using Content.Shared.Random;
using Content.Shared.Random.Helpers;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server.Mining;

/// <summary>
/// This handles creating ores when the entity is destroyed.
/// </summary>
public sealed class MiningSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly StackSystem _stackSystem = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<OreVeinComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<OreVeinComponent, DestructionEventArgs>(OnDestruction);
    }

    private void OnDestruction(EntityUid uid, OreVeinComponent component, DestructionEventArgs args)
    {
        if (component.CurrentOre == null)
            return;

        var proto = _proto.Index<OrePrototype>(component.CurrentOre);

        if (proto.OreEntity == null)
            return;

        var toSpawn = 0;
        var stackCount = _stackSystem.GetCount(uid);
        for (var i = 0; i < stackCount; i++)
        {
            toSpawn += _random.Next(proto.MinOreYield, proto.MaxOreYield);
        }

        _stackSystem.SpawnMultiple(proto.OreEntity, toSpawn, Transform(uid).Coordinates.Offset(_random.NextVector2(0.2f)));
    }

    private void OnMapInit(EntityUid uid, OreVeinComponent component, MapInitEvent args)
    {
        if (component.CurrentOre != null || component.OreRarityPrototypeId == null || !_random.Prob(component.OreChance))
            return;

        component.CurrentOre = _proto.Index<WeightedRandomOrePrototype>(component.OreRarityPrototypeId).Pick(_random);
    }
}
