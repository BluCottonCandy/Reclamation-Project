using System.Globalization;
using System.Linq;
using Content.Server.Botany.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Examine;
using Content.Shared.Verbs;
using Robust.Shared.Utility;

namespace Content.Server.Botany.Systems;

public sealed partial class BotanySystem
{
    [Dependency] private readonly ExamineSystemShared _farmingExamine = default!;

    private void InitializeAdvancedFarming()
    {
        SubscribeLocalEvent<PlantHolderComponent, GetVerbsEvent<ExamineVerb>>(OnAnalyzeHolder);
        SubscribeLocalEvent<SeedComponent, GetVerbsEvent<ExamineVerb>>(OnAnalyzeSeed);
        SubscribeLocalEvent<ProduceComponent, GetVerbsEvent<ExamineVerb>>(OnAnalyzeProduce);
    }

    private bool CanAnalyze(GetVerbsEvent<ExamineVerb> args)
    {
        return args.CanAccess && args.CanInteract && HasComp<AdvancedFarmingComponent>(args.User);
    }

    private void OnAnalyzeHolder(EntityUid uid, PlantHolderComponent holder, GetVerbsEvent<ExamineVerb> args)
    {
        if (!CanAnalyze(args)) return;
        var message = DescribeFarmingPlant(holder.Seed, holder);
        _farmingExamine.AddDetailedExamineVerb(args, holder, message, Loc.GetString("advanced-farming-analyze"));
    }

    private void OnAnalyzeSeed(EntityUid uid, SeedComponent component, GetVerbsEvent<ExamineVerb> args)
    {
        if (!CanAnalyze(args) || !TryGetSeed(component, out var seed)) return;
        _farmingExamine.AddDetailedExamineVerb(args, component, DescribeFarmingPlant(seed), Loc.GetString("advanced-farming-analyze"));
    }

    private void OnAnalyzeProduce(EntityUid uid, ProduceComponent component, GetVerbsEvent<ExamineVerb> args)
    {
        if (!CanAnalyze(args) || !TryGetSeed(component, out var seed)) return;
        _farmingExamine.AddDetailedExamineVerb(args, component, DescribeFarmingPlant(seed), Loc.GetString("advanced-farming-analyze"));
    }

    /// <summary>
    /// Reads the live seed, including inherited and mutated values, without modifying it.
    /// </summary>
    public FormattedMessage DescribeFarmingPlant(SeedData? seed, PlantHolderComponent? holder = null)
    {
        var message = new FormattedMessage();
        Header("advanced-farming-heading");
        if (seed != null)
            Line("advanced-farming-plant", Loc.GetString(seed.DisplayName));
        if (holder != null)
        {
            Header("advanced-farming-condition");
            Line("advanced-farming-water", Number(holder.WaterLevel));
            Line("advanced-farming-nutrients", Number(holder.NutritionLevel));
            Line("advanced-farming-weeds", Number(holder.WeedLevel));
            Line("advanced-farming-pests", Number(holder.PestLevel));
            Line("advanced-farming-toxins", Number(holder.Toxins));
        }

        if (seed == null)
        {
            Line("advanced-farming-status", Loc.GetString("advanced-farming-empty"));
            return message;
        }

        if (holder != null)
        {
            Line("advanced-farming-status", Loc.GetString(holder.Dead ? "advanced-farming-dead" : "advanced-farming-alive"));
            Line("advanced-farming-health", $"{Number(holder.Health)} / {Number(seed.Endurance)}");
            Line("advanced-farming-age", holder.Age.ToString(CultureInfo.InvariantCulture));
            Line("advanced-farming-harvest", YesNo(holder.Harvest));
            Line("advanced-farming-mutation-level", Number(holder.MutationLevel));
            Line("advanced-farming-mutation-mod", Number(holder.MutationMod));
        }

        Header("advanced-farming-genetics");
        Line("advanced-farming-yield", Number(seed.Yield * (holder?.YieldMod ?? 1)));
        Line("advanced-farming-potency", Number(seed.Potency));
        Line("advanced-farming-endurance", Number(seed.Endurance));
        Line("advanced-farming-seedless", YesNo(seed.Seedless));
        Line("advanced-farming-repeat", YesNo(seed.HarvestRepeat == HarvestType.Repeat));
        Line("advanced-farming-glowing", YesNo(seed.Bioluminescent));

        Header("advanced-farming-chemicals");
        if (seed.Chemicals.Count == 0)
            Line("advanced-farming-contents", Loc.GetString("advanced-farming-none"));
        foreach (var (id, chemical) in seed.Chemicals.OrderBy(entry => entry.Key))
        {
            if (!_prototypeManager.TryIndex<ReagentPrototype>(id, out var reagent)) continue;
            var amount = chemical.PotencyDivisor > 0
                ? Math.Clamp(chemical.Min + seed.Potency / chemical.PotencyDivisor, chemical.Min, chemical.Max)
                : chemical.Min;
            ValueLine(reagent.LocalizedName, Number(amount));
        }

        var describedMutations = seed.Mutations.Where(mutation => mutation.Description != null).ToArray();
        if (describedMutations.Length > 0)
            Header("advanced-farming-mutations");
        foreach (var mutation in describedMutations)
        {
            if (mutation.Description != null)
            {
                message.PushNewline();
                message.AddMarkupOrThrow(Loc.GetString(mutation.Description));
            }
        }
        return message;

        string Number(float number) => number.ToString("0.##", CultureInfo.InvariantCulture);
        string YesNo(bool value) => Loc.GetString(value ? "advanced-farming-yes" : "advanced-farming-no");
        void Header(string key)
        {
            if (!message.IsEmpty)
            {
                message.PushNewline();
                message.PushNewline();
            }
            message.PushColor(Color.Gold);
            message.PushTag(new MarkupNode("bold", null, null));
            message.AddText(Loc.GetString(key));
            message.Pop();
            message.Pop();
        }
        void Line(string key, string value) => ValueLine(Loc.GetString(key), value);
        void ValueLine(string label, string value)
        {
            message.PushNewline();
            message.PushColor(Color.LightBlue);
            message.AddText(label + ": ");
            message.Pop();
            message.PushColor(Color.White);
            message.AddText(value);
            message.Pop();
        }
    }
}
