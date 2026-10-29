using System.Linq;
using Content.Shared._ReclamationProject.Medical;
using Content.Shared.Chemistry;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;

namespace Content.Server._ReclamationProject.Medical;

public sealed partial class TraumaSystem
{
    private void InitializeFilterFailure()
    {
        SubscribeLocalEvent<TraumaBodyComponent, MedicalTreatmentAttemptEvent>(OnFilterTreatment);
    }

    public int GetFailedFilterCount(EntityUid uid)
    {
        if (!TryComp<TraumaBodyComponent>(uid, out var medical)) return 0;
        var organs = _body.GetBodyOrgans(uid).ToArray();
        var failed = 0;
        foreach (var kind in new[] { TraumaKind.Liver, TraumaKind.Kidneys })
        {
            var matches = organs.Where(o => TryComp<TraumaOrganComponent>(o.Id, out var injury)
                ? injury.Kind == kind : KindForSlot(o.Component.SlotId) == kind).ToArray();
            if (medical.OriginalOrgans.GetValueOrDefault(kind) == 0 && matches.Length == 0) continue;
            if (!matches.Any(o => o.Component.Enabled &&
                (!TryComp<TraumaOrganComponent>(o.Id, out var injury) || injury.CurrentHealth > 0f)))
                failed++;
        }
        return failed;
    }

    private void OnFilterTreatment(EntityUid uid, TraumaBodyComponent comp, ref MedicalTreatmentAttemptEvent args)
    {
        if (args.IsMetabolism && GetFailedFilterCount(uid) > 0)
            args.Cancelled = true;
    }

    private void ApplyFilterFailurePoison(EntityUid uid, float seconds)
    {
        var failed = GetFailedFilterCount(uid);
        if (failed == 0 || seconds <= 0f) return;
        // The liver's usual poison multiplier must not double this fixed rate.
        _damage.TryChangeDamage(uid, new DamageSpecifier
        {
            DamageDict = new() { ["Poison"] = FixedPoint2.New(failed * seconds) },
        }, ignoreResistances: true, interruptsDoAfters: false, doPartDamage: false, canSever: false);
    }
}
