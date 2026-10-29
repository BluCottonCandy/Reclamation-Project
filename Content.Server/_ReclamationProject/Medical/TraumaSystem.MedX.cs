using System.Linq;
using Content.Server.Body.Components;
using Content.Server.Chemistry.Containers.EntitySystems;
using Content.Shared._ReclamationProject.Medical;
using Content.Shared._Shitmed.Body.Events;
using Content.Shared._Shitmed.Body.Organ;
using Content.Shared._Shitmed.Targeting;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.EntitySystems;

namespace Content.Server._ReclamationProject.Medical;

public sealed partial class TraumaSystem
{
    [Dependency] private readonly SolutionContainerSystem _medxSolutions = default!;

    private void InitializeMedX()
    {
        SubscribeLocalEvent<TraumaBodyComponent, SolutionContainerChangedEvent>(OnMedXSolution);
    }

    private void OnMedXSolution(EntityUid uid, TraumaBodyComponent medical, ref SolutionContainerChangedEvent args)
    {
        if (!TryComp<BloodstreamComponent>(uid, out var blood) || args.SolutionId != blood.ChemicalSolutionName)
            return;
        var amount = args.Solution.GetTotalPrototypeQuantity("DamageResistMixture").Float();
        // A new addition is a dose; metabolism and repeated updates never charge another dose.
        if (amount > medical.LastMedXAmount) medical.PendingMedXDoses++;
        medical.LastMedXAmount = amount;
        UpdateMedX(uid, medical);
    }

    private void UpdateMedX(EntityUid uid, TraumaBodyComponent medical)
    {
        if (medical.UpdatingMedX) return;
        medical.UpdatingMedX = true;
        try
        {
            var amount = 0f;
            if (TryComp<BloodstreamComponent>(uid, out var blood) &&
                _medxSolutions.TryGetSolution(uid, blood.ChemicalSolutionName, out _, out var solution))
                amount = solution.GetTotalPrototypeQuantity("DamageResistMixture").Float();
            var attempt = new MedicalTreatmentAttemptEvent(IsMetabolism: true);
            RaiseLocalEvent(uid, ref attempt);
            var active = amount > 0f && !attempt.Cancelled;
            if (active && medical.PendingMedXDoses > 0)
            {
                var doses = medical.PendingMedXDoses;
                medical.PendingMedXDoses = 0;
                foreach (var (organ, _) in _body.GetBodyOrgans(uid).ToArray())
                {
                    if (!TryComp<TraumaOrganComponent>(organ, out var injury) || !IsFilterOrgan(injury)) continue;
                    injury.MaximumHealth = Math.Max(0f, injury.MaximumHealth - 15f * doses);
                    injury.Damage = Math.Min(injury.Damage, injury.MaximumHealth);
                    // Drug wear is exactly 15 points per dose, not an additional random failure roll.
                    if (injury.CurrentHealth <= 0f) injury.FailureRecorded = true;
                    SetOrganHealthDamage(organ, injury, injury.Damage);
                    Dirty(organ, injury);
                }
                active = GetFailedFilterCount(uid) == 0;
            }
            if (amount <= 0f) medical.PendingMedXDoses = 0;
            foreach (var (part, component) in _body.GetBodyChildren(uid).ToArray())
            {
                if (!IsLimb(component) || !TryComp<TraumaLimbComponent>(part, out var health)) continue;
                var temporary = active ? 15f : 0f;
                if (health.TemporaryHealth == temporary) continue;
                health.TemporaryHealth = temporary;
                var effectiveMaximum = health.MaximumHealth + temporary;
                var ratio = Math.Max(.00001f, effectiveMaximum) / Math.Max(.00001f, health.AppliedMaximumHealth);
                foreach (var key in component.IntegrityThresholds.Keys.ToArray()) component.IntegrityThresholds[key] *= ratio;
                health.AppliedMaximumHealth = effectiveMaximum;
                Dirty(part, health);
                Dirty(part, component);
                var enabled = temporary > 0f || GetLimbHealth(part, health) > 0f && !HasComp<FractureComponent>(part);
                if (component.Enabled != enabled)
                {
                    var state = new BodyPartEnableChangedEvent(enabled);
                    RaiseLocalEvent(part, ref state);
                }
                _body.CheckBodyPart((part, component), _body.GetTargetBodyPart(component), false);
            }
            Refresh(uid, medical);
            _movement.RefreshMovementSpeedModifiers(uid);
        }
        finally { medical.UpdatingMedX = false; }
    }
}
