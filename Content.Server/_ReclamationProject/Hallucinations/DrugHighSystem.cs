using System.Linq;
using Content.Server.Body.Components;
using Content.Server._Misfits.EntityEffects.Effects.DrugAmbience;
using Content.Server.EntityEffects.Effects.StatusEffects;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.Random;
using Content.Shared._ReclamationProject.Hallucinations;
using Content.Shared._ReclamationProject.Symptoms;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._ReclamationProject.Hallucinations;

/// <summary>Relays existing high effects to the owning client's visual system, independently of Outspoken.</summary>
public sealed class DrugHighSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    private HashSet<string>? _highMessages;
    private HashSet<string>? _highReagents;

    public override void Initialize()
    {
        SubscribeLocalEvent<BloodstreamComponent, SolutionContainerChangedEvent>(OnChemicalChange);
    }

    private void OnChemicalChange(EntityUid uid, BloodstreamComponent blood, ref SolutionContainerChangedEvent args)
    {
        if (args.SolutionId == blood.ChemicalSolutionName)
            RecordDose(uid, args.Solution);
    }

    /// <summary>One roll for a new dose, never for metabolism ticks or unchanged solution updates.</summary>
    public bool RecordDose(EntityUid user, Solution solution)
    {
        _highMessages ??= _prototypes.Index<SymptomSpeechPrototype>("SymptomDrugHigh").SourceMessages.ToHashSet();
        _highReagents ??= _prototypes.EnumeratePrototypes<ReagentPrototype>()
            .Where(reagent => reagent.Metabolisms != null && reagent.Metabolisms.Values.SelectMany(entry => entry.Effects).Any(effect =>
                effect is AmbientDrugFlavorText flavor && flavor.MessageKeys.Any(_highMessages.Contains) ||
                effect is GenericStatusEffect status && status.Key == "SeeingRainbows" && status.Type == StatusEffectMetabolismType.Add))
            .Select(reagent => reagent.ID).ToHashSet();
        var current = new Dictionary<string, FixedPoint2>();
        foreach (var quantity in solution.Contents)
        {
            var id = quantity.Reagent.Prototype;
            if (_highReagents.Contains(id))
                current[id] = current.GetValueOrDefault(id) + quantity.Quantity;
        }
        if (current.Count == 0 && !HasComp<DrugDoseTrackerComponent>(user))
            return false;
        var tracker = EnsureComp<DrugDoseTrackerComponent>(user);
        var dose = current.Any(entry => entry.Value > tracker.Amounts.GetValueOrDefault(entry.Key));
        tracker.Amounts = current;
        if (!dose)
            return false;
        tracker.Doses++;
        // Apply to this body only. Never alter character trait selections or award trait points.
        if (!HasComp<UnstableComponent>(user) && _random.Prob(0.05f))
            EnsureComp<UnstableComponent>(user);
        return true;
    }

    public void Refresh(EntityUid user, IEnumerable<string> messageKeys)
    {
        _highMessages ??= _prototypes.Index<SymptomSpeechPrototype>("SymptomDrugHigh").SourceMessages.ToHashSet();
        if (!messageKeys.Any(_highMessages.Contains))
            return;
        // Refreshed on metabolism, before the much longer ambient speech cooldown is checked.
        // A short tail bridges metabolism ticks without treating each dose as a new high.
        EnsureComp<DrugHighComponent>(user).Expires = _timing.CurTime + TimeSpan.FromSeconds(5);
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<DrugHighComponent>();
        while (query.MoveNext(out var uid, out var high))
        {
            if (!Paused(uid) && _timing.CurTime >= high.Expires)
                RemCompDeferred<DrugHighComponent>(uid);
        }
    }
}

/// <summary>Runtime dose accounting belongs to this body, not the saved character profile.</summary>
[RegisterComponent]
public sealed partial class DrugDoseTrackerComponent : Component
{
    public Dictionary<string, FixedPoint2> Amounts = new();
    public int Doses;
}
