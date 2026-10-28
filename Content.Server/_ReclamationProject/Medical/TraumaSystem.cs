using Content.Shared.Atmos.Rotting;
using System.Linq;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server.Medical;
using Content.Shared._ReclamationProject.Medical;
using Content.Shared._Shitmed.Body.Events;
using Content.Shared.Body.Components;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Eye.Blinding.Systems;
using Content.Shared.Humanoid;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Popups;
using Content.Shared.Rejuvenate;
using Robust.Shared.Random;
using Content.Shared.Hands;
using Content.Shared.Standing;
using Robust.Shared.Player;
using Content.Server.GameTicking;

namespace Content.Server._ReclamationProject.Medical;

/// <summary>Structural damage for organic humanoids. Simple NPCs keep their existing medical model.</summary>
public sealed partial class TraumaSystem : EntitySystem
{
    [Dependency] private readonly SharedRottingSystem _rotting = default!;
    [Dependency] private readonly SharedBodySystem _body = default!;
    [Dependency] private readonly DamageableSystem _damage = default!;
    [Dependency] private readonly MobThresholdSystem _thresholds = default!;
    [Dependency] private readonly MobStateSystem _mob = default!;
    [Dependency] private readonly BloodstreamSystem _blood = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;
    [Dependency] private readonly BlurryVisionSystem _blur = default!;
    [Dependency] private readonly BlindableSystem _blind = default!;
    [Dependency] private readonly HungerSystem _hunger = default!;
    [Dependency] private readonly ThirstSystem _thirst = default!;
    [Dependency] private readonly VomitSystem _vomit = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    // Accumulate a burst's part damage before rolling injuries. Pellets do not each roll a wound.
    private readonly Dictionary<EntityUid, (DamageSpecifier Damage, float Age)> _pending = new();
    private bool _rejuvenating;
    [Dependency] private readonly TraumaSurgerySystem _traumaSurgery = default!;
    [Dependency] private readonly StandingStateSystem _standing = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawn);
        SubscribeLocalEvent<PlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<TraumaLimbComponent, BeforeDamageChangedEvent>(OnStructuralHealing);
        SubscribeLocalEvent<TraumaLimbComponent, DamageChangedEvent>(OnPartDamaged, after: new[] { typeof(SharedBodySystem) });
        SubscribeLocalEvent<BrainComponent, BeforeDamageChangedEvent>(OnBrainDamage);
        SubscribeLocalEvent<BrainComponent, DamageChangedEvent>(OnBrainChanged, after: new[] { typeof(SharedBodySystem) });
        SubscribeLocalEvent<TraumaBodyComponent, DamageModifyEvent>(OnPoison);
        SubscribeLocalEvent<TraumaBodyComponent, NutritionAbsorptionEvent>(OnNutrition);
        SubscribeLocalEvent<TraumaBodyComponent, TraumaRepairEvent>(OnRepair);
        SubscribeLocalEvent<TraumaBodyComponent, RejuvenateEvent>(OnRejuvenate);
        InitializeSplints();
        InitializePermanentHealth();
        InitializeFilterFailure();
        InitializeMedX();
        SubscribeLocalEvent<TraumaBodyComponent, DidEquipHandEvent>(OnSupportEquipped);
        SubscribeLocalEvent<TraumaBodyComponent, DidUnequipHandEvent>(OnSupportRemoved);
    }

    public static TraumaKind? KindForSlot(string slot) => slot switch
    {
        "lungs" => TraumaKind.Lungs, "heart" => TraumaKind.Heart, "liver" => TraumaKind.Liver,
        "kidneys" => TraumaKind.Kidneys, "eyes" => TraumaKind.Eyes, "stomach" => TraumaKind.Stomach,
        _ => null,
    };

    private void OnPlayerSpawn(PlayerSpawnCompleteEvent args) => InitializeBody(args.Mob);
    private void OnPlayerAttached(PlayerAttachedEvent args) => InitializeBody(args.Entity);

    public void InitializeBody(EntityUid uid)
    {
        if (!HasComp<HumanoidAppearanceComponent>(uid) || !HasComp<BodyComponent>(uid) || _body.GetBodyOrganComponents<LungComponent>(uid).Count == 0) return;
        _rotting.SetRotDelay(uid, TimeSpan.FromHours(1));
        var medical = EnsureComp<TraumaBodyComponent>(uid);
        foreach (var (part, component) in _body.GetBodyChildren(uid)) InitializeLimbHealth(part, component);
        foreach (var (organ, component) in _body.GetBodyOrgans(uid))
        {
            if (KindForSlot(component.SlotId) is not { } kind || HasComp<BrainComponent>(organ)) continue;
            var injury = EnsureComp<TraumaOrganComponent>(organ);
            injury.Kind = kind;
            Dirty(organ, injury);
        }
        if (medical.OriginalOrgans.Count == 0)
            foreach (var group in _body.GetBodyOrgans(uid).Where(o => HasComp<TraumaOrganComponent>(o.Id))
                         .GroupBy(o => Comp<TraumaOrganComponent>(o.Id).Kind))
                medical.OriginalOrgans[group.Key] = group.Count();
        Refresh(uid, medical);
    }

    private void OnBrainDamage(EntityUid uid, BrainComponent comp, ref BeforeDamageChangedEvent args)
    {
        if (!TryComp<OrganComponent>(uid, out var organ) || organ.Body is not { } body || !HasComp<TraumaBodyComponent>(body)) return;
        if (args.Damage.DamageDict.Values.Any(value => value > 0)) args.Cancelled = true;
    }

    private void OnBrainChanged(EntityUid uid, BrainComponent comp, DamageChangedEvent args)
    {
        if (!TryComp<OrganComponent>(uid, out var organ) || organ.Body is not { } body || !HasComp<TraumaBodyComponent>(body)) return;
        if (args.Damageable.TotalDamage > 0) _damage.SetAllDamage(uid, args.Damageable, Content.Shared.FixedPoint.FixedPoint2.Zero);
    }

    private void OnPartDamaged(EntityUid uid, TraumaLimbComponent marker, DamageChangedEvent args)
    {
        if (!TryComp<BodyPartComponent>(uid, out var part)) return;
        if (_rejuvenating || !args.DamageIncreased || args.DamageDelta == null ||
            part.Body is not { } body || (!TryComp<TraumaBodyComponent>(body, out var trauma) || trauma.SuppressInjuryRolls)) return;
        var injuryDamage = new DamageSpecifier();
        foreach (var (type, value) in args.DamageDelta.DamageDict)
            if (value > 0) injuryDamage.DamageDict[type] = value;
        if (injuryDamage.Empty) return;
        if (_pending.TryGetValue(uid, out var pending)) injuryDamage += pending.Damage;
        _pending[uid] = (injuryDamage, _pending.TryGetValue(uid, out pending) ? pending.Age : 0f);
    }

    public override void Update(float frameTime)
    {
        foreach (var part in _pending.Keys.ToArray())
        {
            var entry = _pending[part];
            if (TerminatingOrDeleted(part)) { _pending.Remove(part); continue; }
            if (MetaData(part).EntityPaused) continue;
            entry.Age += frameTime;
            if (entry.Age < .15f) { _pending[part] = entry; continue; }
            _pending.Remove(part);
            RollInjuries(part, entry.Damage);
        }

        var query = EntityQueryEnumerator<TraumaBodyComponent>();
        while (query.MoveNext(out var uid, out var medical))
        {
            if (MetaData(uid).EntityPaused) continue;
            medical.TickAccumulator += frameTime;
            if (medical.TickAccumulator < 1f) continue;
            var seconds = medical.TickAccumulator;
            medical.TickAccumulator = 0;
            Refresh(uid, medical);
            UpdateMedX(uid, medical);
            if (_mob.IsDead(uid)) continue;
            HealNaturally(uid, medical, seconds);
            if (medical.InternalBleeding > 0)
                _blood.TryModifyBloodLevel(uid, -Content.Shared.FixedPoint.FixedPoint2.New(medical.InternalBleeding * seconds), createPuddle: false);
            if (medical.KidneyDamage > 0 && TryComp<ThirstComponent>(uid, out var thirst))
                _thirst.ModifyThirst(uid, thirst, -.015f * medical.KidneyDamage / 100f * seconds);
            ApplyFilterFailurePoison(uid, seconds);
            medical.SymptomAccumulator += seconds;
            if (medical.SymptomAccumulator < 30f) continue;
            medical.SymptomAccumulator = 0;
            if (medical.StomachDamage >= 50 && _random.Prob(medical.StomachDamage / 200f))
                _vomit.Vomit(uid, thirstAdded: -5, hungerAdded: -5);
            if (medical.InternalBleeding > 0)
                _popup.PopupEntity(Loc.GetString("trauma-internal-pain"), uid, uid);
            if (medical.LungDamage > 0)
                _popup.PopupEntity(Loc.GetString("trauma-breathless"), uid, uid);
        }
    }

    private void ApplySystemicDamage(EntityUid uid, string type, float amount)
        => _damage.TryChangeDamage(uid, new DamageSpecifier { DamageDict = new() { [type] = Content.Shared.FixedPoint.FixedPoint2.New(amount) } },
            interruptsDoAfters: false, doPartDamage: false, canSever: false);

    private void OnPoison(EntityUid uid, TraumaBodyComponent comp, DamageModifyEvent args)
    {
        if (args.Damage.DamageDict.TryGetValue("Poison", out var poison) && poison > 0)
        {
            args.Damage = new DamageSpecifier(args.Damage);
            args.Damage.DamageDict["Poison"] = poison * (1f + comp.LiverDamage / 100f);
        }
    }

    private void OnNutrition(EntityUid uid, TraumaBodyComponent comp, ref NutritionAbsorptionEvent args)
    {
        if (args.Amount > 0) args.Amount *= 1f - .8f * comp.StomachDamage / 100f;
    }

    private void OnRepair(EntityUid uid, TraumaBodyComponent comp, ref TraumaRepairEvent args)
        => Repair(uid, args.Part, args.Kind, surgical: true);

    private void OnSupportEquipped(EntityUid uid, TraumaBodyComponent comp, DidEquipHandEvent args)
    {
        if (HasComp<TraumaWalkingSupportComponent>(args.Equipped)) _body.UpdateMovementSpeed(uid);
        if (HasComp<TraumaWalkingSupportComponent>(args.Equipped) && _traumaSurgery.HasWalkingSupport(uid))
            _standing.Stand(uid);
    }

    private void OnSupportRemoved(EntityUid uid, TraumaBodyComponent comp, DidUnequipHandEvent args)
    {
        if (HasComp<TraumaWalkingSupportComponent>(args.Unequipped)) _body.UpdateMovementSpeed(uid);
        if (HasComp<TraumaWalkingSupportComponent>(args.Unequipped) && TryComp<BodyComponent>(uid, out var body) &&
            body.LegEntities.Count < body.RequiredLegs && !_traumaSurgery.HasWalkingSupport(uid, args.Unequipped))
            _standing.Down(uid);
    }

    public void Repair(EntityUid uid, EntityUid part, TraumaKind kind, bool surgical = false)
    {
        if (!TryComp<TraumaBodyComponent>(uid, out var medical) ||
            !TryComp<BodyPartComponent>(part, out var bodyPart) || bodyPart.Body != uid) return;
        var treatment = new Content.Shared.Chemistry.MedicalTreatmentAttemptEvent();
        RaiseLocalEvent(uid, ref treatment);
        if (treatment.Cancelled) return;
        if (kind == TraumaKind.LimbDamage && bodyPart.PartType is BodyPartType.Arm or BodyPartType.Hand or BodyPartType.Leg or BodyPartType.Foot)
        {
            if (TryComp<DamageableComponent>(part, out var damage))
                _damage.SetAllDamage(part, damage, Content.Shared.FixedPoint.FixedPoint2.Zero);
            _body.CheckBodyPart((part, bodyPart), _body.GetTargetBodyPart(bodyPart), false);
        }
        else if (kind == TraumaKind.Fracture)
        {
            if (surgical) RestoreDisabledLimb(part);
            RemComp<FractureComponent>(part);
            var enabled = new BodyPartEnableChangedEvent(true);
            RaiseLocalEvent(part, ref enabled);
            _body.CheckBodyPart((part, bodyPart), _body.GetTargetBodyPart(bodyPart), false);
            // Re-check the paired hand/arm or foot/leg after repairing either part.
            // A remaining fracture must keep the limb unusable, regardless of repair order.
            foreach (var (related, relatedPart) in _body.GetBodyChildren(uid))
                if (relatedPart.Symmetry == bodyPart.Symmetry && related != part)
                    _body.CheckBodyPart((related, relatedPart), _body.GetTargetBodyPart(relatedPart), false);
            _movement.RefreshMovementSpeedModifiers(uid);
        }
        else if (kind == TraumaKind.InternalBleeding && bodyPart.PartType == BodyPartType.Torso)
        {
            medical.InternalBleeding = 0;
            Dirty(uid, medical);
        }
        else
        {
            foreach (var (organ, _) in _body.GetPartOrgans(part))
                if (TryComp<TraumaOrganComponent>(organ, out var injury) && injury.Kind == kind)
                    SetOrganDamage(organ, 0);
        }
        Refresh(uid, medical);
    }

    public void SetOrganDamage(EntityUid organ, float damage)
    {
        if (HasComp<BrainComponent>(organ) || !TryComp<TraumaOrganComponent>(organ, out var injury)) return;
        SetOrganHealthDamage(organ, injury, damage);
        Dirty(organ, injury);
        if (TryComp<OrganComponent>(organ, out var component) && component.Body is { } body &&
            TryComp<TraumaBodyComponent>(body, out var medical)) Refresh(body, medical);
    }

    public void Fracture(EntityUid part)
    {
        if (!TryComp<BodyPartComponent>(part, out var component) || component.Body is not { } body ||
            component.PartType is not (BodyPartType.Arm or BodyPartType.Hand or BodyPartType.Leg or BodyPartType.Foot)) return;
        EnsureComp<FractureComponent>(part);
        var disabled = new BodyPartEnableChangedEvent(false);
        RaiseLocalEvent(part, ref disabled);
        if (component.PartType == BodyPartType.Foot)
            foreach (var (leg, _) in _body.GetBodyChildrenOfType(body, BodyPartType.Leg, symmetry: component.Symmetry))
            {
                var disableLeg = new BodyPartEnableChangedEvent(false);
                RaiseLocalEvent(leg, ref disableLeg);
            }
        _body.CheckBodyPart((part, component), _body.GetTargetBodyPart(component), false);
        _movement.RefreshMovementSpeedModifiers(body);
        _popup.PopupEntity(Loc.GetString("trauma-fracture", ("part", Name(part))), body, body);
    }

    public void AddInternalBleeding(EntityUid uid, float rate)
    {
        if (!TryComp<TraumaBodyComponent>(uid, out var comp)) return;
        var wasBleeding = comp.InternalBleeding > 0;
        comp.InternalBleeding = Math.Clamp(comp.InternalBleeding + rate, 0, 4);
        Dirty(uid, comp);
        if (!wasBleeding && comp.InternalBleeding > 0)
            _popup.PopupEntity(Loc.GetString("trauma-internal-pain"), uid, uid);
    }

    private void OnRejuvenate(EntityUid uid, TraumaBodyComponent comp, RejuvenateEvent args)
    {
        _rejuvenating = true;
        foreach (var (organ, _) in _body.GetBodyOrgans(uid)) SetOrganDamage(organ, 0);
        if (TryComp<BodyComponent>(uid, out var body) && body.RootContainer.ContainedEntity is { } root)
            foreach (var (part, _) in _body.GetBodyPartChildren(root))
                if (HasComp<FractureComponent>(part)) Repair(uid, part, TraumaKind.Fracture);
        comp.InternalBleeding = 0;
        Refresh(uid, comp);
        Dirty(uid, comp);
        _rejuvenating = false;
    }
}
