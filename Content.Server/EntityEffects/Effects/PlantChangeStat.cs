using Content.Server.Botany;
using Content.Server.Botany.Components;
using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server.EntityEffects.Effects;

public sealed partial class PlantChangeStat : EntityEffect
{
    [DataField]
    public string TargetValue = string.Empty;

    [DataField]
    public float MinValue;

    [DataField]
    public float MaxValue;

    [DataField]
    public int Steps;

    public override void Effect(EntityEffectBaseArgs args)
    {
        var plantHolder = args.EntityManager.GetComponent<PlantHolderComponent>(args.TargetEntity);
        if (plantHolder.Seed == null)
            return;

        var member = plantHolder.Seed.GetType().GetField(TargetValue);
        if (member == null)
        {
            args.EntityManager.System<MutationSystem>().Log.Error(
                $"{GetType().Name} Error: Member {TargetValue} not found on {plantHolder.Seed.GetType().Name}.");
            return;
        }

        var currentValObj = member.GetValue(plantHolder.Seed);
        if (currentValObj == null)
            return;

        if (member.FieldType == typeof(float))
        {
            var floatVal = (float) currentValObj;
            MutateFloat(ref floatVal, MinValue, MaxValue, Steps);
            member.SetValue(plantHolder.Seed, floatVal);
        }
        else if (member.FieldType == typeof(int))
        {
            var intVal = (int) currentValObj;
            MutateInt(ref intVal, (int) MinValue, (int) MaxValue, Steps);
            member.SetValue(plantHolder.Seed, intVal);
        }
        else if (member.FieldType == typeof(bool))
        {
            var boolVal = (bool) currentValObj;
            member.SetValue(plantHolder.Seed, !boolVal);
        }
    }

    private void MutateFloat(ref float val, float min, float max, int bits)
    {
        if (min == max)
        {
            val = min;
            return;
        }

        var valInt = (int) MathF.Round((val - min) / (max - min) * bits);
        valInt = Math.Clamp(valInt, 0, bits);

        var probIncrease = 1 - (float) valInt / bits;
        var valIntMutated = Random(probIncrease) ? valInt + 1 : valInt - 1;
        val = Math.Clamp((float) valIntMutated / bits * (max - min) + min, min, max);
    }

    private void MutateInt(ref int val, int min, int max, int bits)
    {
        if (min == max)
        {
            val = min;
            return;
        }

        var valInt = (int) MathF.Round((val - min) / (float) (max - min) * bits);
        valInt = Math.Clamp(valInt, 0, bits);

        var probIncrease = 1 - (float) valInt / bits;
        var valMutated = Random(probIncrease) ? val + 1 : val - 1;
        val = Math.Clamp(valMutated, min, max);
    }

    private bool Random(float odds)
    {
        return IoCManager.Resolve<IRobustRandom>().Prob(odds);
    }

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
    {
        return null;
    }
}
