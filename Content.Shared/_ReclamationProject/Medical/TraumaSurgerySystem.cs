using System.Linq;
using Content.Shared._Shitmed.Body.Events;
using Content.Shared._Shitmed.Medical.Surgery;
using Content.Shared._Shitmed.Medical.Surgery.Conditions;
using Content.Shared._Shitmed.Medical.Surgery.Steps;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Eye.Blinding.Components;
using Content.Shared.Eye.Blinding.Systems;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Systems;
using Content.Shared.Body.Components;
using Content.Shared.Damage;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.Network;

namespace Content.Shared._ReclamationProject.Medical;

/// <summary>Integrates injury repair with Shitmed's tool checks, prerequisites and do-afters.</summary>
public sealed class TraumaSurgerySystem : EntitySystem
{
    [Dependency] private readonly SharedBodySystem _body = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<TraumaRepairComponent, SurgeryValidEvent>(OnValid);
        SubscribeLocalEvent<TraumaRepairComponent, SurgeryStepCompleteCheckEvent>(OnCompleteCheck);
        SubscribeLocalEvent<TraumaRepairComponent, SurgeryStepEvent>(OnStep);
        SubscribeLocalEvent<TraumaLimbComponent, BodyPartEnableChangedEvent>(OnEnable,
            before: new[] { typeof(SharedBodySystem) });
        SubscribeLocalEvent<TraumaBodyComponent, GetBlurEvent>(OnBlur);
        SubscribeLocalEvent<TraumaBodyComponent, CanSeeAttemptEvent>(OnSee);
        SubscribeLocalEvent<TraumaBodyComponent, RefreshMovementSpeedModifiersEvent>(OnMovement);
        SubscribeLocalEvent<TraumaBodyComponent, GetBodyStandingSupportEvent>(OnStandingSupport);
    }

    public bool NeedsRepair(EntityUid body, EntityUid part, TraumaKind kind)
    {
        if (!HasComp<TraumaBodyComponent>(body) || !TryComp<BodyPartComponent>(part, out var bodyPart) || bodyPart.Body != body)
            return false;
        if (kind == TraumaKind.Fracture)
            return HasComp<FractureComponent>(part);
        if (kind == TraumaKind.LimbDamage)
            return bodyPart.PartType is BodyPartType.Arm or BodyPartType.Hand or BodyPartType.Leg or BodyPartType.Foot &&
                TryComp<DamageableComponent>(part, out var damage) && damage.TotalDamage > 0;
        if (kind == TraumaKind.InternalBleeding)
            return bodyPart.PartType == BodyPartType.Torso && Comp<TraumaBodyComponent>(body).InternalBleeding > 0;
        return _body.GetPartOrgans(part).Any(o => TryComp<TraumaOrganComponent>(o.Id, out var injury) &&
            injury.Kind == kind && injury.Damage > 0);
    }

    private void OnValid(EntityUid uid, TraumaRepairComponent comp, ref SurgeryValidEvent args)
    {
        if (!NeedsRepair(args.Body, args.Part, comp.Kind)) args.Cancelled = true;
    }

    private void OnCompleteCheck(EntityUid uid, TraumaRepairComponent comp, ref SurgeryStepCompleteCheckEvent args)
    {
        if (NeedsRepair(args.Body, args.Part, comp.Kind)) args.Cancelled = true;
    }

    private void OnStep(EntityUid uid, TraumaRepairComponent comp, ref SurgeryStepEvent args)
    {
        if (!_net.IsServer || !NeedsRepair(args.Body, args.Part, comp.Kind)) return;
        var repair = new TraumaRepairEvent(args.Part, comp.Kind);
        RaiseLocalEvent(args.Body, ref repair);
    }

    private void OnEnable(EntityUid uid, TraumaLimbComponent marker, ref BodyPartEnableChangedEvent args)
    {
        // Ordinary medicine and passive part healing must not re-enable a broken limb.
        if (!args.Enabled || !TryComp<BodyPartComponent>(uid, out var comp)) return;
        var blocked = HasComp<FractureComponent>(uid);
        if (comp.Body is { } body)
        {
            var related = comp.PartType switch
            {
                BodyPartType.Leg => BodyPartType.Foot,
                BodyPartType.Arm => BodyPartType.Hand,
                BodyPartType.Hand => BodyPartType.Arm,
                _ => BodyPartType.Other,
            };
            if (related != BodyPartType.Other)
                blocked |= _body.GetBodyChildrenOfType(body, related, symmetry: comp.Symmetry)
                    .Any(p => HasComp<FractureComponent>(p.Id));
        }
        if (blocked) args = new BodyPartEnableChangedEvent(false);
    }

    private void OnBlur(EntityUid uid, TraumaBodyComponent comp, GetBlurEvent args)
        => args.Blur += comp.EyeDamage / 100f * BlurryVisionComponent.MaxMagnitude;

    private void OnSee(EntityUid uid, TraumaBodyComponent comp, CanSeeAttemptEvent args)
    {
        if (comp.EyeDamage >= 100f) args.Cancel();
    }

    private void OnMovement(EntityUid uid, TraumaBodyComponent comp, RefreshMovementSpeedModifiersEvent args)
    {
        if (HasWalkingSupport(uid))
        {
            args.ModifySpeed(.5f, .5f);
            return;
        }
        var fractures = _body.GetBodyChildrenOfType(uid, BodyPartType.Leg)
            .Concat(_body.GetBodyChildrenOfType(uid, BodyPartType.Foot))
            .Where(p => HasComp<FractureComponent>(p.Id)).ToArray();
        if (fractures.Length == 0) return;
        // A splint supports transport, but never restores use of the fractured limb.
        var factor = fractures.All(p => Comp<FractureComponent>(p.Id).Splinted) ? .85f : .7f;
        args.ModifySpeed(factor, factor);
    }

    public bool HasWalkingSupport(EntityUid uid, EntityUid? excluding = null)
    {
        if (!TryComp<BodyComponent>(uid, out var body) || body.RequiredLegs != 2 || body.LegEntities.Count != 1)
            return false;
        // A cane replaces one support point, never a second healthy leg or two missing legs.
        return _hands.EnumerateHeld(uid).Any(item => item != excluding && HasComp<TraumaWalkingSupportComponent>(item));
    }

    private void OnStandingSupport(EntityUid uid, TraumaBodyComponent comp, ref GetBodyStandingSupportEvent args)
    {
        if (HasWalkingSupport(uid)) args.AdditionalLegs = 1;
    }
}
