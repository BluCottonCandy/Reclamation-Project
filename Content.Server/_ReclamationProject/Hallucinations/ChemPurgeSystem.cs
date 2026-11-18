using Content.Server.Body.Components;
using Content.Server.Medical;
using Content.Server.Nutrition;
using Content.Shared._Misfits.SpecialStats;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Movement.Systems;
using Content.Shared.StatusEffect;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;
using Robust.Shared.Timing;

namespace Content.Server._ReclamationProject.Hallucinations;

[RegisterComponent]
public sealed partial class StillmindPacketComponent : Component;

[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class StillmindPurgeComponent : Component
{
    [DataField]
    public int BoutsRemaining;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextBout;
}

public sealed class ChemPurgeSystem : EntitySystem
{
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private readonly StatusEffectsSystem _status = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly VomitSystem _vomit = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;

    private const string Stillmind = "ReclamationStillmindPowder";
    private const string Neurostatic = "MisfitsNeurostatic";
    private static readonly string[] Remedies = { Stillmind, Neurostatic };
    private static readonly TimeSpan BoutInterval = TimeSpan.FromSeconds(3);

    public override void Initialize()
    {
        SubscribeLocalEvent<StillmindPacketComponent, FoodSwallowedEvent>(OnPowderSwallowed);
    }

    private void OnPowderSwallowed(EntityUid uid, StillmindPacketComponent packet, ref FoodSwallowedEvent args)
    {
        if (args.Dose.GetTotalPrototypeQuantity(Stillmind) <= 0)
            return;

        Purge(args.Consumer);
        // The dose is still in the stomach. Bridge digestion so the patient gets relief during the purge.
        _status.TryAddStatusEffect(args.Consumer, "HallucinationSuppression", TimeSpan.FromSeconds(25),
            true, "HallucinationSuppression");
        var purge = EnsureComp<StillmindPurgeComponent>(args.Consumer);
        if (purge.BoutsRemaining == 0)
            purge.NextBout = _timing.CurTime + BoutInterval;
        purge.BoutsRemaining += 3;
    }

    public void Purge(EntityUid uid)
    {
        if (TryComp<BloodstreamComponent>(uid, out var blood) &&
            _solutions.ResolveSolution(uid, blood.ChemicalSolutionName, ref blood.ChemicalSolution, out var solution))
        {
            var changed = false;
            for (var i = solution.Contents.Count - 1; i >= 0; i--)
            {
                var reagent = solution.Contents[i];
                if (reagent.Reagent.Prototype.Id is Stillmind or Neurostatic)
                    continue;
                solution.RemoveReagent(reagent);
                changed = true;
            }
            if (changed)
                _solutions.UpdateChemicals(blood.ChemicalSolution.Value, false);
        }

        // These are lasting bonuses granted by chems. Other injuries and physical statuses have separate causes.
        _status.TryRemoveStatusEffect(uid, "MedXProtection");
        _status.TryRemoveStatusEffect(uid, "RecoveryDrugActive");
        _status.TryRemoveStatusEffect(uid, "RadiationProtection");
        var hadSpeed = RemComp<DrugSpecialBoostComponent>(uid);
        if (RemComp<MovespeedModifierMetabolismComponent>(uid) || hadSpeed)
            _movement.RefreshMovementSpeedModifiers(uid);
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<StillmindPurgeComponent>();
        while (query.MoveNext(out var uid, out var purge))
        {
            if (_timing.CurTime < purge.NextBout)
                continue;
            _vomit.Vomit(uid, -8f, -8f, Remedies);
            if (--purge.BoutsRemaining == 0)
                RemComp<StillmindPurgeComponent>(uid);
            else
                purge.NextBout = _timing.CurTime + BoutInterval;
        }
    }
}
