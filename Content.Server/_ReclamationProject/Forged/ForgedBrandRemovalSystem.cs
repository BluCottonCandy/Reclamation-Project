using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server.Kitchen.Components;
using Content.Shared._ReclamationProject.Forged;
using Content.Shared.Damage;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Verbs;
using Robust.Shared.Timing;

namespace Content.Server._ReclamationProject.Forged;

public sealed partial class ForgedBrandingSystem
{
    [Dependency] private readonly DamageableSystem _damage = default!;
    [Dependency] private readonly MobThresholdSystem _thresholds = default!;
    [Dependency] private readonly BloodstreamSystem _blood = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private sealed class Removal
    {
        public EntityUid User;
        public EntityUid Tool;
        public TimeSpan Start;
        public TimeSpan NextTick;
        public TimeSpan NextScream;
    }

    private readonly Dictionary<EntityUid, Removal> _removals = new();

    private void InitializeRemoval()
    {
        SubscribeLocalEvent<ForgedBrandProfileComponent, ExaminedEvent>(OnProfileExamined);
        SubscribeLocalEvent<ForgedBrandingPracticeComponent, ExaminedEvent>(OnPracticeExamined);
        SubscribeLocalEvent<HumanoidAppearanceComponent, GetVerbsEvent<AlternativeVerb>>(OnRemovalVerb);
        SubscribeLocalEvent<HumanoidAppearanceComponent, ForgedBrandRemovalDoAfterEvent>(OnRemovalFinished);
        SubscribeLocalEvent<HumanoidAppearanceComponent, DoAfterAttemptEvent<ForgedBrandRemovalDoAfterEvent>>(OnRemovalAttempt);
    }

    private void OnProfileExamined(EntityUid uid, ForgedBrandProfileComponent comp, ExaminedEvent args) => OnBrandExamined(uid, args);
    private void OnPracticeExamined(EntityUid uid, ForgedBrandingPracticeComponent comp, ExaminedEvent args) => OnBrandExamined(uid, args);

    private void OnBrandExamined(EntityUid ent, ExaminedEvent args)
    {
        if (HasBrand(ent, ForgedBrandingProfiles.Forged) && HasBrand(ent, ForgedBrandingProfiles.Captive))
        {
            args.PushMarkup(Loc.GetString("forged-brand-examine-covered"));
            return;
        }
        if (HasBrand(ent, ForgedBrandingProfiles.Forged))
            args.PushMarkup(Loc.GetString("forged-brand-examine-anvil"));
        if (HasBrand(ent, ForgedBrandingProfiles.Captive))
            args.PushMarkup(Loc.GetString("forged-brand-examine-captive"));
    }

    private void OnRemovalVerb(Entity<HumanoidAppearanceComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Using is not { } tool || !HasComp<SharpComponent>(tool)
            || !Eligible(ent) || !HasBrand(ent, ForgedBrandingProfiles.Captive))
            return;
        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("forged-brand-cut-verb"),
            Act = () => StartRemoval(ent, user, tool)
        });
    }

    private bool SafeToCut(EntityUid target, out DamageableComponent? damageable, out FixedPoint2 ceiling)
    {
        ceiling = FixedPoint2.Zero;
        if (!TryComp(target, out damageable)
            || !_thresholds.TryGetThresholdForState(target, MobState.Critical, out var critical)
            || !TryComp<BloodstreamComponent>(target, out var blood) || _blood.GetBloodLevelPercentage(target, blood) < 0.8f)
            return false;
        // Leave a substantial margin for native blood loss and secondary injury effects.
        ceiling = critical.Value * 0.7;
        return damageable.TotalDamage < critical.Value * 0.85;
    }

    private void StartRemoval(EntityUid target, EntityUid user, EntityUid tool)
    {
        if (!Eligible(target) || !HasBrand(target, ForgedBrandingProfiles.Captive) || _saving.Contains(target)
            || _removals.ContainsKey(target) || !HasComp<SharpComponent>(tool)
            || _hands.GetActiveItem(user) != tool || !_interaction.InRangeUnobstructed(user, target))
            return;
        if (!SafeToCut(target, out _, out _))
        {
            _popup.PopupEntity(Loc.GetString("forged-brand-cut-unsafe"), target, user);
            return;
        }
        _removals[target] = new Removal { User = user, Tool = tool, Start = _timing.CurTime,
            NextTick = _timing.CurTime + TimeSpan.FromSeconds(1), NextScream = _timing.CurTime + TimeSpan.FromSeconds(4) };
        // Same cancellable, hand-bound removal pattern as SharedProjectileSystem.OnEmbedActivate.
        // Keep the sharp tool in the hand; do not actually embed a damaging projectile in the patient.
        if (!_doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user, TimeSpan.FromSeconds(20),
                new ForgedBrandRemovalDoAfterEvent(), target, target: target, used: tool)
            {
                MultiplyDelay = false,
                NeedHand = true,
                BreakOnMove = true,
                BreakOnDamage = false, // The procedure's own cuts must not cancel it.
                DistanceThreshold = 1.5f,
                AttemptFrequency = AttemptFrequency.EveryTick,
                BlockDuplicate = true,
                CancelDuplicate = false
            }))
        {
            _removals.Remove(target);
            return;
        }
        _chat.TryEmoteWithChat(target, "Scream", ignoreActionBlocker: true, forceEmote: true);
        _popup.PopupEntity(Loc.GetString("forged-brand-cut-start"), target, user);
    }

    private void OnRemovalAttempt(Entity<HumanoidAppearanceComponent> ent,
        ref DoAfterAttemptEvent<ForgedBrandRemovalDoAfterEvent> args)
    {
        if (!_removals.TryGetValue(ent, out var removal) || removal.User != args.Event.User
            || removal.Tool != args.Event.Used || _hands.GetActiveItem(removal.User) != removal.Tool
            || !HasComp<SharpComponent>(removal.Tool) || !HasBrand(ent, ForgedBrandingProfiles.Captive)
            || !SafeToCut(ent, out var damageable, out var ceiling))
        {
            args.Cancel();
            return;
        }
        var now = _timing.CurTime;
        if (now < removal.NextTick)
            return;
        removal.NextTick = now + TimeSpan.FromSeconds(1);
        if (now >= removal.NextScream)
        {
            removal.NextScream = now + TimeSpan.FromSeconds(4);
            _chat.TryEmoteWithChat(ent, "Scream", ignoreActionBlocker: true, forceEmote: true);
        }
        ApplyCut(ent, damageable!, ceiling);
    }

    private void ApplyCut(EntityUid target, DamageableComponent damageable, FixedPoint2 ceiling)
    {
        if (!TryComp<BloodstreamComponent>(target, out var blood))
            return;
        var beforeBleed = blood.BleedAmount;
        var amount = FixedPoint2.Min(ceiling / 20, FixedPoint2.Max(FixedPoint2.Zero, ceiling - damageable.TotalDamage));
        if (amount > FixedPoint2.Zero)
        {
            // Use the embedded-wound damage pathway, with bounded damage and no limb severing.
            var cut = new DamageSpecifier();
            cut.DamageDict.Add("Slash", amount);
            _damage.TryChangeDamage(target, cut, ignoreResistances: true, interruptsDoAfters: false,
                canSever: false, doPartDamage: false);
        }
        // Native bleeding, bounded so this procedure cannot drain a healthy patient's blood supply.
        // Preserve any unrelated bleeding already present instead of treating their other wounds.
        var desired = Math.Max(beforeBleed, 1f);
        _blood.TryModifyBleedAmount(target, desired - blood.BleedAmount, blood);
    }

    private void OnRemovalFinished(Entity<HumanoidAppearanceComponent> ent, ref ForgedBrandRemovalDoAfterEvent args)
    {
        if (!_removals.Remove(ent, out var removal) || args.Handled)
            return;
        args.Handled = true;
        if (args.Cancelled || !HasBrand(ent, ForgedBrandingProfiles.Captive)
            || _hands.GetActiveItem(args.User) != removal.Tool || !HasComp<SharpComponent>(removal.Tool)
            || !SafeToCut(ent, out var damageable, out var ceiling))
            return;
        ApplyCut(ent, damageable!, ceiling);
        _ = ChangeBrand(ent, args.User, ForgedBrandingProfiles.Captive, true, captiveOnly: true);
    }
}
