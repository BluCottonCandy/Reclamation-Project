using System.Linq;
using Robust.Shared.Random;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Shared._ReclamationProject.Medical;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;

namespace Content.Server._ReclamationProject.Medical;

public sealed partial class TraumaSystem
{
    // Unusual outcomes remain possible for every damage type. Tiny ticks cannot roll trauma;
    // a 30-point hit has a 2.2% fallback chance, capped at 5% for off-type injuries.
    private static float CrossInjuryChance(float total) => Math.Clamp((total - 8f) * .001f, 0, .05f);

    public static float FractureChance(float blunt, float piercing, float slash, float other = 0)
        => Math.Max(CrossInjuryChance(blunt + piercing + slash + other),
            Math.Clamp((blunt * 1.4f + piercing * .2f + slash * .15f - 8f) * .018f, 0, .85f));

    public static float BleedingChance(float blunt, float piercing, float slash, float other = 0)
        => Math.Max(CrossInjuryChance(blunt + piercing + slash + other),
            Math.Clamp((blunt * .15f + piercing + slash * .8f - 8f) * .018f, 0, .8f));

    public static float OrganChance(float blunt, float piercing, float slash, float other = 0)
        => Math.Max(CrossInjuryChance(blunt + piercing + slash + other),
            Math.Clamp((blunt * 1.4f + piercing * .6f + slash * .3f - 10f) * .015f, 0, .8f));

    private void RollInjuries(EntityUid part, DamageSpecifier damage)
    {
        if (!TryComp<BodyPartComponent>(part, out var component) || component.Body is not { } body ||
            !HasComp<TraumaBodyComponent>(body) || _mob.IsDead(body)) return;
        float Amount(string type) => damage.DamageDict.TryGetValue(type, out var value) ? Math.Max(0, value.Float()) : 0;
        var blunt = Amount("Blunt"); var piercing = Amount("Piercing"); var slash = Amount("Slash");
        var total = damage.GetTotal().Float();
        var other = Math.Max(0, total - blunt - piercing - slash);
        if (component.PartType is BodyPartType.Arm or BodyPartType.Hand or BodyPartType.Leg or BodyPartType.Foot &&
            !HasComp<FractureComponent>(part) && _random.Prob(FractureChance(blunt, piercing, slash, other)))
            Fracture(part);
        if (component.PartType == BodyPartType.Torso && _random.Prob(BleedingChance(blunt, piercing, slash, other)))
            AddInternalBleeding(body, Math.Clamp(total * .04f, .25f, 1.5f));
        var organs = _body.GetPartOrgans(part).Where(o => HasComp<TraumaOrganComponent>(o.Id)).ToArray();
        if (organs.Length > 0 && _random.Prob(OrganChance(blunt, piercing, slash, other)))
        {
            var organ = organs[_random.Next(organs.Length)].Id;
            SetOrganDamage(organ, Comp<TraumaOrganComponent>(organ).Damage + Math.Clamp(total * .75f, 5, 45));
            _popup.PopupEntity(Loc.GetString("trauma-deep-injury"), body, body);
        }
    }

    public void Refresh(EntityUid uid, TraumaBodyComponent medical)
    {
        // Newly transplanted organs and limbs join the same injury model.
        foreach (var (part, _) in _body.GetBodyChildren(uid)) EnsureComp<TraumaLimbComponent>(part);
        foreach (var (organ, component) in _body.GetBodyOrgans(uid))
            if (KindForSlot(component.SlotId) is { } kind && !HasComp<TraumaOrganComponent>(organ))
            {
                var added = EnsureComp<TraumaOrganComponent>(organ);
                added.Kind = kind;
                Dirty(organ, added);
            }
        var organs = _body.GetBodyOrgans(uid).Where(o => HasComp<TraumaOrganComponent>(o.Id)).ToArray();
        float Damage(TraumaKind kind)
        {
            var matches = organs.Where(o => Comp<TraumaOrganComponent>(o.Id).Kind == kind).ToArray();
            var original = medical.OriginalOrgans.GetValueOrDefault(kind, matches.Length);
            var count = Math.Max(original, matches.Length);
            return count == 0 ? 0 : (matches.Sum(o => Comp<TraumaOrganComponent>(o.Id).Damage) +
                Math.Max(0, original - matches.Length) * 100f) / count;
        }
        var lungs = Damage(TraumaKind.Lungs); var heart = Damage(TraumaKind.Heart);
        var liver = Damage(TraumaKind.Liver); var kidneys = Damage(TraumaKind.Kidneys);
        var eyes = Damage(TraumaKind.Eyes); var stomach = Damage(TraumaKind.Stomach);
        var changed = medical.LungDamage != lungs || medical.HeartDamage != heart || medical.LiverDamage != liver ||
            medical.KidneyDamage != kidneys || medical.EyeDamage != eyes || medical.StomachDamage != stomach;
        var eyeChanged = medical.EyeDamage != eyes;
        medical.LungDamage = lungs; medical.HeartDamage = heart; medical.LiverDamage = liver;
        medical.KidneyDamage = kidneys; medical.EyeDamage = eyes; medical.StomachDamage = stomach;
        if (TryComp<StaminaComponent>(uid, out var stamina))
        {
            // A nonzero numerical floor keeps other stamina systems safe at total lung failure.
            // At that point breathing fails and the remaining stamina capacity is negligible.
            var capacity = Math.Max(.0001f, 1f - lungs / 100f);
            var recovery = 1f - .5f * kidneys / 100f;
            if (capacity != medical.AppliedStamina || recovery != medical.AppliedRecovery)
            {
                stamina.CritThreshold *= capacity / medical.AppliedStamina;
                stamina.Decay *= recovery / medical.AppliedRecovery;
                medical.AppliedStamina = capacity;
                medical.AppliedRecovery = recovery;
                Dirty(uid, stamina);
            }
        }
        var heartFactor = 1f - .5f * heart / 100f;
        if (heartFactor != medical.AppliedHeart &&
            _thresholds.TryGetThresholdForState(uid, MobState.Critical, out var critical))
        {
            _thresholds.SetMobStateThreshold(uid, FixedPoint2.Max(FixedPoint2.New(1),
                critical.Value * (heartFactor / medical.AppliedHeart)), MobState.Critical);
            medical.AppliedHeart = heartFactor;
        }
        foreach (var (organ, _) in organs)
        {
            var injury = Comp<TraumaOrganComponent>(organ);
            if (injury.Kind is not (TraumaKind.Liver or TraumaKind.Kidneys)) continue;
            var intervalFactor = 1f / Math.Max(.1f, 1f - .9f * injury.Damage / 100f);
            if (intervalFactor == injury.AppliedMetabolism) continue;
            var metabolic = new ApplyMetabolicMultiplierEvent(organ, intervalFactor / injury.AppliedMetabolism, true);
            RaiseLocalEvent(organ, ref metabolic);
            injury.AppliedMetabolism = intervalFactor;
        }
        if (changed) Dirty(uid, medical);
        if (eyeChanged)
        {
            _blur.UpdateBlurMagnitude(uid);
            _blind.UpdateIsBlind(uid);
        }
    }

    public string GetReport(EntityUid uid)
    {
        if (!TryComp<TraumaBodyComponent>(uid, out var medical)) return string.Empty;
        Refresh(uid, medical);
        var lines = new List<string> { Loc.GetString("trauma-report-heading") };
        foreach (var (part, component) in _body.GetBodyChildren(uid))
        {
            if (component.PartType is not (BodyPartType.Arm or BodyPartType.Hand or BodyPartType.Leg or BodyPartType.Foot) ||
                !TryComp<DamageableComponent>(part, out var damage)) continue;
            var threshold = component.IntegrityThresholds[Content.Shared._Shitmed.Targeting.TargetIntegrity.CriticallyWounded];
            var percent = MathF.Round(damage.TotalDamage.Float() / Math.Max(1, threshold) * 100);
            var status = !component.Enabled ? "trauma-limb-disabled" : percent >= 60 ? "trauma-limb-warning" : "trauma-limb-functional";
            lines.Add(Loc.GetString("trauma-report-limb", ("part", Name(part)), ("damage", percent), ("status", Loc.GetString(status))));
        }
        if (TryComp<BodyComponent>(uid, out var body) && body.RootContainer.ContainedEntity is { } root)
            foreach (var (part, _) in _body.GetBodyPartChildren(root))
                if (TryComp<FractureComponent>(part, out var fracture))
                    lines.Add(Loc.GetString(fracture.Splinted ? "trauma-report-splinted" : "trauma-report-fracture", ("part", Name(part))));
        if (medical.InternalBleeding > 0)
            lines.Add(Loc.GetString("trauma-report-bleeding", ("rate", MathF.Round(medical.InternalBleeding, 2))));
        foreach (var (kind, value) in new[] { (TraumaKind.Lungs, medical.LungDamage), (TraumaKind.Heart, medical.HeartDamage),
                     (TraumaKind.Liver, medical.LiverDamage), (TraumaKind.Kidneys, medical.KidneyDamage),
                     (TraumaKind.Eyes, medical.EyeDamage), (TraumaKind.Stomach, medical.StomachDamage) })
            if (value > 0) lines.Add(Loc.GetString("trauma-report-organ", ("organ", Loc.GetString("trauma-organ-" + kind.ToString().ToLowerInvariant())),
                ("damage", MathF.Round(value))));
        if (lines.Count == 1) lines.Add(Loc.GetString("trauma-report-healthy"));
        return string.Join("\n", lines);
    }
}
