using Content.Shared.Atmos;
using Content.Shared.EntityEffects;
using Content.Shared.Random;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using System.Linq;

namespace Content.Server.Botany;

public sealed class MutationSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _robustRandom = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

    private RandomPlantMutationListPrototype _randomMutations = default!;

    public override void Initialize()
    {
        _randomMutations = _prototypeManager.Index<RandomPlantMutationListPrototype>("RandomPlantMutations");
    }

    /// <summary>
    ///     For each random mutation, see if it occurs on this plant this check.
    /// </summary>
    public void CheckRandomMutations(EntityUid plantHolder, ref SeedData seed, float severity)
    {
        foreach (var mutation in _randomMutations.Mutations)
        {
            if (!Random(mutation.BaseOdds * severity))
                continue;

            if (mutation.AppliesToPlant)
            {
                var args = new EntityEffectBaseArgs(plantHolder, EntityManager);
                mutation.Effect.Effect(args);
            }

            if (mutation.Persists && !seed.Mutations.Any(m => m.Name == mutation.Name))
                seed.Mutations.Add(mutation);
        }
    }

    /// <summary>
    ///     Checks all defined mutations against a seed to see which of them are applied.
    /// </summary>
    public void MutateSeed(EntityUid plantHolder, ref SeedData seed, float severity)
    {
        if (!seed.Unique)
        {
            Log.Error("Attempted to mutate a shared seed");
            return;
        }

        CheckRandomMutations(plantHolder, ref seed, severity);
    }

    public SeedData Cross(SeedData a, SeedData b)
    {
        var result = b.Clone();

        CrossChemicals(ref result.Chemicals, a.Chemicals);

        CrossFloat(ref result.NutrientConsumption, a.NutrientConsumption);
        CrossFloat(ref result.WaterConsumption, a.WaterConsumption);
        CrossFloat(ref result.IdealHeat, a.IdealHeat);
        CrossFloat(ref result.HeatTolerance, a.HeatTolerance);
        CrossFloat(ref result.IdealLight, a.IdealLight);
        CrossFloat(ref result.LightTolerance, a.LightTolerance);
        CrossFloat(ref result.ToxinsTolerance, a.ToxinsTolerance);
        CrossFloat(ref result.LowPressureTolerance, a.LowPressureTolerance);
        CrossFloat(ref result.HighPressureTolerance, a.HighPressureTolerance);
        CrossFloat(ref result.PestTolerance, a.PestTolerance);
        CrossFloat(ref result.WeedTolerance, a.WeedTolerance);

        CrossFloat(ref result.Endurance, a.Endurance);
        CrossInt(ref result.Yield, a.Yield);
        CrossFloat(ref result.Lifespan, a.Lifespan);
        CrossFloat(ref result.Maturation, a.Maturation);
        CrossFloat(ref result.Production, a.Production);
        CrossFloat(ref result.Potency, a.Potency);

        CrossBool(ref result.Seedless, a.Seedless);
        CrossBool(ref result.Viable, a.Viable);
        CrossBool(ref result.Slip, a.Slip);
        CrossBool(ref result.Sentient, a.Sentient);
        CrossBool(ref result.Ligneous, a.Ligneous);
        CrossBool(ref result.Teleporting, a.Teleporting);
        CrossBool(ref result.Bioluminescent, a.Bioluminescent);
        CrossBool(ref result.TurnIntoKudzu, a.TurnIntoKudzu);
        CrossBool(ref result.CanScream, a.CanScream);

        CrossGasses(ref result.ExudeGasses, a.ExudeGasses);
        CrossGasses(ref result.ConsumeGasses, a.ConsumeGasses);

        result.BioluminescentColor = Random(0.5f) ? a.BioluminescentColor : result.BioluminescentColor;
        result.Mutations = result.Mutations.Where(m => Random(0.5f))
            .Union(a.Mutations.Where(m => Random(0.5f)))
            .DistinctBy(m => m.Name)
            .ToList();

        if (a.Name != result.Name && Random(0.7f))
            result.Seedless = true;

        return result;
    }

    private void CrossChemicals(ref Dictionary<string, SeedChemQuantity> val, Dictionary<string, SeedChemQuantity> other)
    {
        foreach (var otherChem in other)
        {
            if (val.ContainsKey(otherChem.Key))
            {
                val[otherChem.Key] = Random(0.5f) ? otherChem.Value : val[otherChem.Key];
            }
            else if (Random(0.5f))
            {
                var fixedChem = otherChem.Value;
                fixedChem.Inherent = false;
                val.Add(otherChem.Key, fixedChem);
            }
        }

        foreach (var thisChem in val.ToArray())
        {
            if (!other.ContainsKey(thisChem.Key) && Random(0.5f) && val.Count > 1)
                val.Remove(thisChem.Key);
        }
    }

    private void CrossGasses(ref Dictionary<Gas, float> val, Dictionary<Gas, float> other)
    {
        foreach (var otherGas in other)
        {
            if (val.ContainsKey(otherGas.Key))
            {
                val[otherGas.Key] = Random(0.5f) ? otherGas.Value : val[otherGas.Key];
            }
            else if (Random(0.5f))
            {
                val.Add(otherGas.Key, otherGas.Value);
            }
        }

        foreach (var thisGas in val.ToArray())
        {
            if (!other.ContainsKey(thisGas.Key) && Random(0.5f))
                val.Remove(thisGas.Key);
        }
    }

    private bool Random(float p)
    {
        return _robustRandom.Prob(p);
    }

    private void CrossFloat(ref float val, float other)
    {
        val = Random(0.5f) ? val : other;
    }

    private void CrossInt(ref int val, int other)
    {
        val = Random(0.5f) ? val : other;
    }

    private void CrossBool(ref bool val, bool other)
    {
        val = Random(0.5f) ? val : other;
    }
}
