using Content.Server.Botany;
using Content.Server.Botany.Components;
using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Serilog;

namespace Content.Server.EntityEffects.Effects;

public sealed partial class PlantSpeciesChange : EntityEffect
{
    public override void Effect(EntityEffectBaseArgs args)
    {
        var prototypeManager = IoCManager.Resolve<IPrototypeManager>();
        var random = IoCManager.Resolve<IRobustRandom>();
        var plantHolder = args.EntityManager.GetComponent<PlantHolderComponent>(args.TargetEntity);
        if (plantHolder.Seed == null || plantHolder.Seed.MutationPrototypes.Count == 0)
            return;

        var targetProto = random.Pick(plantHolder.Seed.MutationPrototypes);
        if (!prototypeManager.TryIndex(targetProto, out SeedPrototype? protoSeed) || protoSeed == null)
        {
            Log.Error($"Seed prototype could not be found: {targetProto}!");
            return;
        }

        plantHolder.Seed = plantHolder.Seed.SpeciesChange(protoSeed);
    }

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
    {
        return null;
    }
}
