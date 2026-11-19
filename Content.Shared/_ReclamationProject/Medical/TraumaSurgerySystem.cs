using Robust.Shared.GameStates;
using System.Linq;
using Content.Shared._Shitmed.Body.Events;
using Content.Shared._Shitmed.Targeting;
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
using Content.Shared.Inventory;
using Content.Shared.Standing;
using Content.Shared._ReclamationProject.PowerArmor;
using Robust.Shared.Network;

namespace Content.Shared._ReclamationProject.Medical;

/// <summary>Integrates injury repair with Shitmed's tool checks, prerequisites and do-afters.</summary>
public sealed partial class TraumaSurgerySystem : EntitySystem
{
    [Dependency] private readonly SharedBodySystem _body = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;
    [Dependency] private readonly StandingStateSystem _standing = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<TraumaRepairComponent, SurgeryValidEvent>(OnValid);
        SubscribeLocalEvent<TraumaRepairComponent, SurgeryStepCompleteCheckEvent>(OnCompleteCheck);
        SubscribeLocalEvent<TraumaRepairComponent, SurgeryStepEvent>(OnStep);
        SubscribeLocalEvent<TraumaLimbComponent, BodyPartEnableChangedEvent>(OnEnable,
            before: new[] { typeof(SharedBodySystem) });
        SubscribeLocalEvent<TraumaLimbComponent, AfterAutoHandleStateEvent>(OnLimbHealthState);
        SubscribeLocalEvent<TraumaBodyComponent, GetBlurEvent>(OnBlur);
        SubscribeLocalEvent<TraumaBodyComponent, CanSeeAttemptEvent>(OnSee);
        SubscribeLocalEvent<TraumaBodyComponent, RefreshMovementSpeedModifiersEvent>(OnMovement);
        SubscribeLocalEvent<BodyComponent, GetBodyStandingSupportEvent>(OnStandingSupport);
    }

    public static bool SupportsTissueRepair(BodyPartType type)
        => type is BodyPartType.Head or BodyPartType.Torso or BodyPartType.Arm or BodyPartType.Hand or BodyPartType.Leg or BodyPartType.Foot;

    public bool NeedsRepair(EntityUid body, EntityUid part, TraumaKind kind)
    {
        if (!HasComp<TraumaBodyComponent>(body) || !TryComp<BodyPartComponent>(part, out var bodyPart) || bodyPart.Body != body)
            return false;
        if (kind == TraumaKind.LimbDamage &&
            TryComp<TraumaLimbComponent>(part, out var limb) && limb.MaximumHealth <= 0f)
            return false;
        if (kind == TraumaKind.Fracture)
            return HasComp<FractureComponent>(part) ||
                bodyPart.PartType is (BodyPartType.Arm or BodyPartType.Hand or BodyPartType.Leg or BodyPartType.Foot) &&
                TryComp<TraumaLimbComponent>(part, out var health) && health.OriginalFailureThreshold > 0f &&
                TryComp<DamageableComponent>(part, out var tissue) &&
                tissue.TotalDamage.Float() >= health.MaximumHealth * health.OriginalFailureThreshold / 100f;
        if (kind == TraumaKind.LimbDamage)
            return SupportsTissueRepair(bodyPart.PartType) &&
                TryComp<DamageableComponent>(part, out var damage) && damage.TotalDamage > 0;
        if (kind == TraumaKind.InternalBleeding)
            return bodyPart.PartType == BodyPartType.Torso && Comp<TraumaBodyComponent>(body).InternalBleeding > 0;
        return _body.GetPartOrgans(part).Any(o => TryComp<TraumaOrganComponent>(o.Id, out var injury) &&
            injury.Kind == kind && injury.MaximumHealth > 0f && injury.Damage > 0);
    }

    private void OnValid(EntityUid uid, TraumaRepairComponent comp, ref SurgeryValidEvent args)
    {
        // Keep tissue repair in the procedure list even when damage changes after
        // opening surgery. Completion checks decide whether treatment is needed.
        if (comp.Kind == TraumaKind.LimbDamage)
        {
            if (!HasComp<TraumaBodyComponent>(args.Body) ||
                !TryComp<BodyPartComponent>(args.Part, out var part) || part.Body != args.Body ||
                !SupportsTissueRepair(part.PartType)) args.Cancelled = true;
            return;
        }
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
        if (!TryComp<BodyPartComponent>(uid, out var comp)) return;
        var protectedLimb = HasLimbDisableProtection(uid);
        if (protectedLimb) args = new BodyPartEnableChangedEvent(true);
        if (args.Enabled && !protectedLimb)
        {
            var blocked = marker.MaximumHealth + marker.TemporaryHealth <= 0f ||
                marker.TemporaryHealth <= 0f && HasComp<FractureComponent>(uid);
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
                        .Any(p => HasComp<FractureComponent>(p.Id) &&
                            (!TryComp<TraumaLimbComponent>(p.Id, out var health) || health.TemporaryHealth <= 0f));
            }
            if (blocked) args = new BodyPartEnableChangedEvent(false);
        }

        // Server-side permanent wear observes the final requested state, before
        // SharedBodySystem applies it. This also covers fracture-driven failure.
        var changed = new TraumaLimbStateChangeEvent(args.Enabled);
        RaiseLocalEvent(uid, ref changed);
    }

    private void OnLimbHealthState(EntityUid uid, TraumaLimbComponent comp, ref AfterAutoHandleStateEvent args)
    {
        if (_net.IsServer || !TryComp<BodyPartComponent>(uid, out var part)) return;
        var ratio = Math.Max(.00001f, comp.MaximumHealth + comp.TemporaryHealth) / Math.Max(.00001f, comp.AppliedMaximumHealth);
        foreach (var key in part.IntegrityThresholds.Keys.ToArray())
            part.IntegrityThresholds[key] *= ratio;
        comp.AppliedMaximumHealth = comp.MaximumHealth + comp.TemporaryHealth;
    }

    private void OnBlur(EntityUid uid, TraumaBodyComponent comp, GetBlurEvent args)
        => args.Blur += comp.EyeDamage / 100f * BlurryVisionComponent.MaxMagnitude;

    private void OnSee(EntityUid uid, TraumaBodyComponent comp, CanSeeAttemptEvent args)
    {
        if (comp.EyeDamage >= 100f) args.Cancel();
    }

    private void OnMovement(EntityUid uid, TraumaBodyComponent comp, RefreshMovementSpeedModifiersEvent args)
    {
        if (GetWalkingSupport(uid) == WalkingSupport.Cane)
        {
            args.ModifySpeed(.5f, .5f);
            return;
        }
        var fractures = _body.GetBodyChildrenOfType(uid, BodyPartType.Leg)
            .Concat(_body.GetBodyChildrenOfType(uid, BodyPartType.Foot))
            .Where(p => HasComp<FractureComponent>(p.Id) &&
                (!TryComp<TraumaLimbComponent>(p.Id, out var health) || health.TemporaryHealth <= 0f)).ToArray();
        if (fractures.Length == 0) return;
        // A splint supports transport, but never restores use of the fractured limb.
        var factor = fractures.All(p => Comp<FractureComponent>(p.Id).Splinted) ? .85f : .7f;
        args.ModifySpeed(factor, factor);
    }

    private enum WalkingSupport { None, Cane, Frame }

    private WalkingSupport GetWalkingSupport(EntityUid uid, EntityUid? excluding = null)
    {
        if (!TryComp<BodyComponent>(uid, out var body) || body.RequiredLegs != 2 || body.LegEntities.Count >= 2)
            return WalkingSupport.None;
        if (_inventory.TryGetSlotEntity(uid, "outerClothing", out var suit) &&
            suit != excluding && HasComp<ModularPowerArmorComponent>(suit))
            return WalkingSupport.Frame;
        if (body.LegEntities.Count == 1 && HasComp<TraumaBodyComponent>(uid) &&
            _hands.EnumerateHeld(uid).Any(item => item != excluding && HasComp<TraumaWalkingSupportComponent>(item)))
            return WalkingSupport.Cane;
        return WalkingSupport.None;
    }

    private void OnStandingSupport(Entity<BodyComponent> body, ref GetBodyStandingSupportEvent args)
    {
        if (GetWalkingSupport(body) != WalkingSupport.None)
            args.AdditionalLegs = body.Comp.RequiredLegs - body.Comp.LegEntities.Count;
    }

    public void RefreshWalkingSupport(EntityUid wearer, EntityUid? removed = null)
    {
        if (!TryComp<BodyComponent>(wearer, out var body) || body.RequiredLegs != 2 || body.LegEntities.Count >= 2)
            return;

        _body.UpdateMovementSpeed(wearer, body);
        _movement.RefreshMovementSpeedModifiers(wearer);
        var supported = GetWalkingSupport(wearer, removed) != WalkingSupport.None;
        if (removed == null && supported)
            _standing.Stand(wearer);
        else if (removed != null && !supported)
            _standing.Down(wearer);
    }
}
