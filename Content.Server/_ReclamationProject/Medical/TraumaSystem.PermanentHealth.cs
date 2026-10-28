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
            if (GetLimbHealth(uid, comp) > 0f)
                comp.FailureRecorded = false;
            return;
        }
        if (!part.Enabled || comp.FailureRecorded || comp.MaximumHealth <= 0f || !_body.BodyHasChild(body, uid)) return;
        InitializeLimbHealth(uid, part);
        comp.FailureRecorded = true; // Latch before damage/disable callbacks can re-enter.
        comp.MaximumHealth = Math.Max(0f, comp.MaximumHealth - _random.Next(10, 26));
        var ratio = Math.Max(.00001f, comp.MaximumHealth) / Math.Max(.00001f, comp.AppliedMaximumHealth);
        foreach (var threshold in part.IntegrityThresholds.Keys.ToArray())
            part.IntegrityThresholds[threshold] *= ratio;
        comp.AppliedMaximumHealth = comp.MaximumHealth;
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

    public float GetLimbHealth(EntityUid uid, TraumaLimbComponent health)
    {
        if (health.MaximumHealth <= 0f || !TryComp<DamageableComponent>(uid, out var damage)) return 0f;
        // Damage uses fixed-point units while thresholds use floats. Round away
        // sub-unit noise so a fully failed limb cannot appear barely alive.
        return MathF.Round(Math.Clamp(health.MaximumHealth - damage.TotalDamage.Float() /
            Math.Max(.00001f, health.OriginalFailureThreshold) * 100f, 0f, health.MaximumHealth), 2);
    }
}
