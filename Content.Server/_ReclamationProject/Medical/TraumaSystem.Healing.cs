using System.Linq;
using Content.Server.Bed.Components;
using Content.Shared._ReclamationProject.Medical;
using Content.Shared._Shitmed.Targeting;
using Content.Shared.Bed.Sleep;
using Content.Shared.Buckle.Components;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;

namespace Content.Server._ReclamationProject.Medical;

public sealed partial class TraumaSystem
{
    public float RecoveryPerMinute(EntityUid uid)
    {
        var rate = HasComp<TraumaRegenerationComponent>(uid) ? 3f : 1f;
        if (TryComp<BuckleComponent>(uid, out var buckle) && buckle.BuckledTo is { } bed && HasComp<HealOnBuckleComponent>(bed))
            rate = HasComp<SleepingComponent>(uid) ? 10f : 5f;
        return rate;
    }

    public void HealNaturally(EntityUid uid, TraumaBodyComponent medical, float seconds)
    {
        if (_mob.IsDead(uid) || seconds <= 0) return;
        var points = RecoveryPerMinute(uid) * seconds / 60f;
        foreach (var (organ, _) in _body.GetBodyOrgans(uid).ToArray())
            if (TryComp<TraumaOrganComponent>(organ, out var injury) && injury.Damage > 0)
                SetOrganDamage(organ, Math.Max(0, injury.Damage - points));
        if (medical.InternalBleeding > 0)
        {
            medical.InternalBleeding = Math.Max(0, medical.InternalBleeding - 4f * points / 100f);
            Dirty(uid, medical);
        }
        foreach (var (part, component) in _body.GetBodyChildren(uid).ToArray())
        {
            if (TryComp<FractureComponent>(part, out var fracture))
            {
                fracture.HealingProgress = Math.Min(100, fracture.HealingProgress + points);
                Dirty(part, fracture);
                if (fracture.HealingProgress >= 100) Repair(uid, part, TraumaKind.Fracture);
            }
            if (!TryComp<DamageableComponent>(part, out var damage) || damage.TotalDamage <= 0) continue;
            // Use the ordinary damage/healing pipeline, distributing one percentage point of
            // the limb's failure threshold across its existing damage rather than each type.
            var budget = component.IntegrityThresholds[TargetIntegrity.CriticallyWounded] * points / 100f;
            var total = damage.TotalDamage.Float();
            var heal = new DamageSpecifier();
            foreach (var (type, amount) in damage.Damage.DamageDict)
                if (amount > 0) heal.DamageDict[type] = -FixedPoint2.New(Math.Min(amount.Float(), budget * amount.Float() / total));
            _damage.TryChangeDamage(part, heal, ignoreResistances: true, interruptsDoAfters: false, canSever: false);
        }
        Refresh(uid, medical);
    }
}
