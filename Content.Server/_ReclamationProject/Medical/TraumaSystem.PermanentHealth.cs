using System.Linq;
using Content.Shared._ReclamationProject.Medical;
using Content.Shared._Shitmed.Body.Organ;
using Content.Shared._Shitmed.Targeting;
using Content.Shared.Body.Events;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Robust.Shared.Random;

namespace Content.Server._ReclamationProject.Medical;

public sealed partial class TraumaSystem
{
    private void InitializePermanentHealth()
    {
        SubscribeLocalEvent<TraumaOrganComponent, OrganEnableChangedEvent>(OnOrganState,
            before: new[] { typeof(SharedBodySystem) });
        SubscribeLocalEvent<TraumaOrganComponent, OrganRemovedEvent>(OnOrganRemoving);
        SubscribeLocalEvent<TraumaOrganComponent, OrganAddedEvent>(OnOrganInserted);
        SubscribeLocalEvent<TraumaLimbComponent, TraumaLimbStateChangeEvent>(OnLimbState);
    }

    private void OnOrganRemoving(EntityUid uid, TraumaOrganComponent comp, ref OrganRemovedEvent args)
        => comp.Detaching = true;

    private void OnOrganInserted(EntityUid uid, TraumaOrganComponent comp, ref OrganAddedEvent args)
        => comp.Detaching = false;

    private void WearOrgan(EntityUid uid, TraumaOrganComponent comp)
    {
        if (comp.FailureRecorded || comp.MaximumHealth <= 0f) return;
        comp.FailureRecorded = true;
        comp.MaximumHealth = Math.Max(0f, comp.MaximumHealth - _random.Next(25, 86));
        comp.Damage = comp.MaximumHealth;
        Dirty(uid, comp);
    }

    private void OnOrganState(EntityUid uid, TraumaOrganComponent comp, ref OrganEnableChangedEvent args)
    {
        if (comp.Detaching) return; // Ordinary removal is not another organ failure.
        if (args.Enabled && (comp.MaximumHealth <= 0f || comp.Damage >= comp.MaximumHealth ||
            IsFilterOrgan(comp) && comp.FailureRecorded && comp.Damage > 0f))
            args = new OrganEnableChangedEvent(false);
        if (!args.Enabled)
            WearOrgan(uid, comp);
        else
            comp.FailureRecorded = false;
        Dirty(uid, comp);
    }

    private void SetOrganHealthDamage(EntityUid uid, TraumaOrganComponent comp, float damage)
    {
        comp.Damage = Math.Clamp(damage, 0f, comp.MaximumHealth);
        if (comp.MaximumHealth > 0f && comp.Damage >= comp.MaximumHealth)
            WearOrgan(uid, comp);
        else if (comp.MaximumHealth > 0f && (!IsFilterOrgan(comp) || comp.Damage <= 0f))
            comp.FailureRecorded = false;

        if (TryComp<OrganComponent>(uid, out var organ) && organ.Body != null)
        {
            var enabled = comp.MaximumHealth > 0f && comp.Damage < comp.MaximumHealth &&
                (!IsFilterOrgan(comp) || !comp.FailureRecorded || comp.Damage <= 0f);
            if (organ.Enabled != enabled)
            {
                var ev = new OrganEnableChangedEvent(enabled);
                RaiseLocalEvent(uid, ref ev);
            }
        }
    }

    private static bool IsFilterOrgan(TraumaOrganComponent organ)
        => organ.Kind is TraumaKind.Liver or TraumaKind.Kidneys;

    private static bool IsLimb(BodyPartComponent part)
        => part.PartType is BodyPartType.Arm or BodyPartType.Hand or BodyPartType.Leg or BodyPartType.Foot;

    private void InitializeLimbHealth(EntityUid uid, BodyPartComponent part)
    {
        var health = EnsureComp<TraumaLimbComponent>(uid);
        if (!IsLimb(part) || health.OriginalFailureThreshold > 0f) return;
        health.OriginalFailureThreshold = part.IntegrityThresholds[TargetIntegrity.CriticallyWounded];
        Dirty(uid, health);
    }

    private void OnLimbState(EntityUid uid, TraumaLimbComponent comp, ref TraumaLimbStateChangeEvent args)
    {
        if (!TryComp<BodyPartComponent>(uid, out var part) || !IsLimb(part) || part.Body is not { } body)
            return;
        if (args.Enabled)
        {
            // A fracture repair may request enable before tissue is repaired.
            // Rearm only after some health has genuinely been restored.
            if (comp.TemporaryHealth <= 0f && GetLimbHealth(uid, comp) > 0f)
                comp.FailureRecorded = false;
            return;
        }
        if (!part.Enabled || comp.FailureRecorded || comp.MaximumHealth <= 0f || !_body.BodyHasChild(body, uid)) return;
        InitializeLimbHealth(uid, part);
        comp.FailureRecorded = true; // Latch before damage/disable callbacks can re-enter.
        comp.MaximumHealth = Math.Min(15f, Math.Max(0f, comp.MaximumHealth - _random.Next(10, 26)));
        var ratio = Math.Max(.00001f, comp.MaximumHealth + comp.TemporaryHealth) / Math.Max(.00001f, comp.AppliedMaximumHealth);
        foreach (var threshold in part.IntegrityThresholds.Keys.ToArray())
            part.IntegrityThresholds[threshold] *= ratio;
        comp.AppliedMaximumHealth = comp.MaximumHealth + comp.TemporaryHealth;
        Dirty(uid, comp);
        Dirty(uid, part);

        // A fracture can disable an otherwise undamaged limb. Its health is zero
        // until tissue healing or surgery repairs it to the reduced maximum.
        if (!TryComp<DamageableComponent>(uid, out var damage)) return;
        var required = FixedPoint2.NewCeiling(part.IntegrityThresholds[TargetIntegrity.CriticallyWounded]);
        var missing = required - damage.TotalDamage;
        if (missing <= FixedPoint2.Zero) return;
        var trauma = CompOrNull<TraumaBodyComponent>(body);
        var suppressed = trauma?.SuppressInjuryRolls ?? false;
        if (trauma != null) trauma.SuppressInjuryRolls = true;
        try
        {
            _damage.TryChangeDamage(uid, new DamageSpecifier { DamageDict = new() { ["Blunt"] = missing } },
                ignoreResistances: true, doPartDamage: false, canSever: false);
        }
        finally
        {
            if (trauma != null) trauma.SuppressInjuryRolls = suppressed;
        }
    }

    private void RestoreDisabledLimb(EntityUid part)
    {
        if (!TryComp<TraumaLimbComponent>(part, out var health) ||
            health.OriginalFailureThreshold <= 0f || !TryComp<DamageableComponent>(part, out var damage)) return;
        // Bone setting can salvage even zero-capacity limbs, but only to the
        // scarred fifteen-point limit. Ordinary healing cannot raise this cap.
        health.MaximumHealth = 15f;
        if (TryComp<BodyPartComponent>(part, out var limb))
        {
            var effectiveMaximum = health.MaximumHealth + health.TemporaryHealth;
            var ratio = Math.Max(.00001f, effectiveMaximum) / Math.Max(.00001f, health.AppliedMaximumHealth);
            foreach (var key in limb.IntegrityThresholds.Keys.ToArray()) limb.IntegrityThresholds[key] *= ratio;
            health.AppliedMaximumHealth = effectiveMaximum;
            Dirty(part, limb);
        }
        Dirty(part, health);
        var target = Math.Min(15f, health.MaximumHealth);
        var targetDamage = FixedPoint2.New((health.MaximumHealth - target) * health.OriginalFailureThreshold / 100f);
        var total = damage.TotalDamage;
        if (total <= targetDamage) return;
        var heal = new DamageSpecifier();
        foreach (var (type, amount) in damage.Damage.DamageDict)
            if (amount > 0) heal.DamageDict[type] = -FixedPoint2.NewCeiling((total - targetDamage).Float() * amount.Float() / total.Float());
        _authorizedStructuralHealingParts.Add(part);
        try
        {
            _damage.TryChangeDamage(part, heal, ignoreResistances: true, interruptsDoAfters: false, canSever: false);
        }
        finally { _authorizedStructuralHealingParts.Remove(part); }
    }

    public float GetLimbHealth(EntityUid uid, TraumaLimbComponent health)
    {
        if (health.MaximumHealth + health.TemporaryHealth <= 0f || !TryComp<DamageableComponent>(uid, out var damage)) return 0f;
        // Damage uses fixed-point units while thresholds use floats. Round away
        // sub-unit noise so a fully failed limb cannot appear barely alive.
        return MathF.Round(Math.Clamp(health.MaximumHealth + health.TemporaryHealth - damage.TotalDamage.Float() /
            Math.Max(.00001f, health.OriginalFailureThreshold) * 100f, 0f, health.MaximumHealth + health.TemporaryHealth), 2);
    }
}
