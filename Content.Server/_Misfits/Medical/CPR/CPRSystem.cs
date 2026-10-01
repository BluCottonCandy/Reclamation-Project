// #Misfits Change — Misfits left-click CPR system commented out.
// Reverted to the upstream verb-based system (Content.Server/Medical/CPR/CPRSystem.cs).
// Shared support files (CPRDoAfterEvent, CPRCooldownComponent) are also commented out.
/*
// #Misfits Add - Server-side CPR system.
// Inspired by CPR concepts in stalker-14-EN, designed and built independently for N14.
// Allows a player to perform CPR on a critically-injured target to stabilise them and
// partially reverse damage, giving medics a meaningful emergency tool.
// #Misfits Fix - extended to support CPR on dead targets (requires N14CPRTraining trait).
using Content.Server.Medical.CPR; // for CPRTrainingComponent (N14CPRTraining trait gate)
using Content.Shared._Misfits.Medical.CPR;
using Content.Shared.Damage;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Server.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Timing;

namespace Content.Server._Misfits.Medical.CPR;

/// <summary>
/// CPR lets any humanoid (empty-handed) perform chest compressions on a critically injured player.
/// - Performer must have empty active hand (no item) or have hands at all.
/// - Target must be in <see cref="MobState.Critical"/> or <see cref="MobState.Dead"/>.
/// - 8-second do-after; cancels if the performer moves or takes damage.
/// - On dead targets (requires N14CPRTraining): heals 40 Asphyxiation to attempt revival,
///   matching smelling salts / defibrillator parity. No brute healing is applied.
/// - 20-second cooldown per performer to prevent spam.
/// </summary>
public sealed class CPRSystem : EntitySystem
{
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly MobThresholdSystem _mobThreshold = default!; // #Misfits Fix - needed for dead-threshold check
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    // How many Asphyxiation damage points to heal when reviving a dead target.
    // Matches smelling salts and defibrillator revival heal for parity.
    // No brute healing is applied — CPR is purely an oxygen-restoration tool.
    private const float CPRAsphyxiationHeal = 40f;

    // Sound to play when CPR completes (compressions).
    private static readonly SoundPathSpecifier CPRSound =
        new("/Audio/Effects/hit_kick.ogg");

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MobStateComponent, InteractHandEvent>(OnInteractHand);
        SubscribeLocalEvent<CPRDoAfterEvent>(OnCPRDoAfter);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        // Clean up expired cooldown components.
        var query = EntityQueryEnumerator<CPRCooldownComponent>();
        while (query.MoveNext(out var uid, out var cooldown))
        {
            if (_timing.CurTime >= cooldown.ExpireTime)
                RemComp<CPRCooldownComponent>(uid);
        }
    }

    private void OnInteractHand(EntityUid uid, MobStateComponent component, InteractHandEvent args)
    {
        if (args.Handled)
            return;

        var user = args.User;
        var target = args.Target;

        // No self-CPR.
        if (user == target)
            return;

        // #Misfits Fix - accept both critical and dead targets.
        var isTargetCritical = _mobState.IsCritical(target);
        var isTargetDead = _mobState.IsDead(target);

        if (!isTargetCritical && !isTargetDead)
            return;

        // CPR on a dead target is gated behind the N14CPRTraining trait.
        if (isTargetDead && !HasComp<CPRTrainingComponent>(user))
        {
            _popup.PopupEntity(Loc.GetString("cpr-no-training-for-dead"), user, user, PopupType.Small);
            return;
        }

        // Performer must not be on cooldown.
        if (HasComp<CPRCooldownComponent>(user))
        {
            _popup.PopupEntity(Loc.GetString("cpr-on-cooldown"), user, user, PopupType.Small);
            return;
        }

        args.Handled = true;

        if (isTargetDead)
        {
            _popup.PopupEntity(
                Loc.GetString("cpr-start-performer-dead", ("target", target)),
                user, user, PopupType.Medium);
            _popup.PopupEntity(
                Loc.GetString("cpr-start-target-dead", ("user", user)),
                target, target, PopupType.Medium);
        }
        else
        {
            _popup.PopupEntity(
                Loc.GetString("cpr-start-performer", ("target", target)),
                user, user, PopupType.Medium);
            _popup.PopupEntity(
                Loc.GetString("cpr-start-target", ("user", user)),
                target, target, PopupType.Medium);
        }

        var doAfterArgs = new DoAfterArgs(EntityManager, user, TimeSpan.FromSeconds(8f), new CPRDoAfterEvent(), user, target)
        {
            NeedHand = true,
            BreakOnMove = true,
            BreakOnDamage = true,
            Hidden = false,
        };
        _doAfter.TryStartDoAfter(doAfterArgs);
    }

    private void OnCPRDoAfter(CPRDoAfterEvent args)
    {
        if (args.Cancelled || args.Target == null)
            return;

        var user = args.User;
        var target = args.Target.Value;

        // #Misfits Fix - re-validate for both critical and dead states.
        var isTargetCritical = _mobState.IsCritical(target);
        var isTargetDead = _mobState.IsDead(target);

        if (!isTargetCritical && !isTargetDead)
        {
            _popup.PopupEntity(Loc.GetString("cpr-target-no-longer-critical", ("target", target)), user, user, PopupType.Small);
            return;
        }

        _audio.PlayPvs(CPRSound, target, AudioParams.Default.WithVolume(-2f));

        if (isTargetDead)
        {
            // #Misfits Tweak - CPR revival parity with smelling salts / defibrillator:
            // Only heal Asphyxiation (no brute), 40 points to match those tools.
            // Apply heal first, then check if total damage dropped below the death threshold.
            var healSpec = new DamageSpecifier();
            healSpec.DamageDict.Add("Asphyxiation", -CPRAsphyxiationHeal);
            _damageable.TryChangeDamage(target, healSpec, true, origin: user);

            if (TryComp<MobStateComponent>(target, out var mobState) &&
                _mobThreshold.TryGetThresholdForState(target, MobState.Dead, out var threshold) &&
                TryComp<DamageableComponent>(target, out var damageable) &&
                damageable.TotalDamage < threshold)
            {
                _mobState.ChangeMobState(target, MobState.Critical, mobState, user);
                _popup.PopupEntity(
                    Loc.GetString("cpr-revive-performer", ("target", target)),
                    user, user, PopupType.Large);
                _popup.PopupEntity(
                    Loc.GetString("cpr-revive-target", ("user", user)),
                    target, target, PopupType.Large);
            }
            else
            {
                // Damage still above death threshold — CPR wasn't enough to revive.
                _popup.PopupEntity(
                    Loc.GetString("cpr-failed-revive-performer", ("target", target)),
                    user, user, PopupType.Medium);
            }
        }
        else
        {
            _popup.PopupEntity(
                Loc.GetString("cpr-success-performer", ("target", target)),
                user, user, PopupType.Medium);
            _popup.PopupEntity(
                Loc.GetString("cpr-success-target", ("user", user)),
                target, target, PopupType.Medium);
        }

        // Apply cooldown: performer cannot immediately repeat.
        var cooldown = EnsureComp<CPRCooldownComponent>(user);
        cooldown.ExpireTime = _timing.CurTime + TimeSpan.FromSeconds(cooldown.CooldownDuration);
    }
}
*/
