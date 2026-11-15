using Content.Shared._ReclamationProject.Hallucinations;
using Content.Shared._ReclamationProject.Endurance;
using Content.Shared.Damage;
using Content.Server.GameTicking;
using Content.Shared.Drugs;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._ReclamationProject.Hallucinations;

/// <summary>Reversible perceived damage. Never calls Damageable, blood, organ or limb injury APIs.</summary>
public sealed class HallucinationPainSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MobStateSystem _mob = default!;
    [Dependency] private readonly MobThresholdSystem _thresholds = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<GhostAttemptHandleEvent>(OnGhostAttempt);
        SubscribeNetworkEvent<HallucinationEncounterStartedEvent>((msg, args) =>
        {
            if (args.SenderSession.AttachedEntity is { } user) Begin(user, msg.Encounter);
        });
        SubscribeNetworkEvent<HallucinationHitEvent>((msg, args) =>
        {
            if (args.SenderSession.AttachedEntity is { } user) Hit(user, msg.Encounter, msg.Ranged);
        });
        SubscribeNetworkEvent<HallucinationEncounterEndedEvent>((msg, args) =>
        {
            if (args.SenderSession.AttachedEntity is { } user) Finish(user, msg.Encounter);
        });
        SubscribeLocalEvent<HallucinationPainComponent, UpdateMobStateEvent>(OnUpdateState,
            after: new[] { typeof(MobThresholdSystem) });
    }

    private void OnGhostAttempt(GhostAttemptHandleEvent args)
    {
        if (args.Mind.CurrentEntity is not { } user ||
            !TryComp<HallucinationPainComponent>(user, out var pain) || !pain.Collapsing)
            return;
        args.Result = false;
        args.Handled = true;
    }

    public bool Begin(EntityUid user, Guid encounter)
    {
        var drugHallucinations = !HasComp<DrugHallucinationResistanceComponent>(user) &&
            (HasComp<DrugHighComponent>(user) || HasComp<SeeingRainbowsComponent>(user));
        if (encounter == Guid.Empty || !_mob.IsAlive(user) ||
            !(HasComp<UnstableComponent>(user) || drugHallucinations))
            return false;
        var pain = EnsureComp<HallucinationPainComponent>(user);
        if (pain.Collapsing || _timing.CurTime < pain.NextBeginAt) return false;
        pain.Encounter = encounter;
        pain.Amount = 0;
        pain.Expires = _timing.CurTime + TimeSpan.FromSeconds(22);
        pain.NextBeginAt = _timing.CurTime + TimeSpan.FromSeconds(1);
        pain.NextHitAt = TimeSpan.Zero;
        Dirty(user, pain);
        return true;
    }

    public bool Hit(EntityUid user, Guid encounter, bool ranged)
    {
        if (!TryComp<HallucinationPainComponent>(user, out var pain) || encounter == Guid.Empty ||
            pain.Encounter != encounter || pain.Collapsing || _timing.CurTime >= pain.Expires ||
            _timing.CurTime < pain.NextHitAt || !_mob.IsAlive(user)) return false;
        // An active invulnerability ability must remain authoritative.
        if (TryComp<EnduranceEnragedComponent>(user, out var rage) && rage.Active) return false;
        if (!TryComp<DamageableComponent>(user, out var realDamage) ||
            !_thresholds.TryGetThresholdForState(user, MobState.Critical, out var critical)) return false;
        pain.NextHitAt = _timing.CurTime + TimeSpan.FromSeconds(0.15);
        pain.Amount = Math.Min(pain.Amount + (ranged ? 10f : 25f), 1000f);
        Dirty(user, pain);
        if (realDamage.TotalDamage.Float() + pain.Amount < critical.Value.Float())
        {
            _thresholds.VerifyThresholds(user); // Refresh the health indicator after every apparent hit.
            return true;
        }
        // Only a perceived contribution can cause this collapse; real wounds are never reversed.
        if (realDamage.TotalDamage >= critical.Value) { Finish(user, encounter); return false; }
        pain.Amount = 0;
        pain.Collapsing = true;
        pain.CollapseUntil = _timing.CurTime + TimeSpan.FromSeconds(1);
        pain.InducingCollapse = true;
        try { _mob.ChangeMobState(user, MobState.Critical); }
        finally { pain.InducingCollapse = false; }
        if (!_mob.IsCritical(user)) pain.Collapsing = false;
        Dirty(user, pain);
        return true;
    }

    public void Finish(EntityUid user, Guid encounter)
    {
        if (!TryComp<HallucinationPainComponent>(user, out var pain) || pain.Encounter != encounter) return;
        pain.Amount = 0;
        pain.Encounter = Guid.Empty;
        Dirty(user, pain);
        if (!pain.Collapsing) _thresholds.VerifyThresholds(user);
        // Keep the brief real collapse visible even if the client removes its enemies immediately.
    }

    private void OnUpdateState(EntityUid user, HallucinationPainComponent pain, ref UpdateMobStateEvent args)
    {
        if (pain.Collapsing && _timing.CurTime < pain.CollapseUntil && args.State == MobState.Alive)
            args.State = MobState.Critical;
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<HallucinationPainComponent>();
        while (query.MoveNext(out var user, out var pain))
        {
            if (Paused(user)) continue;
            if (pain.Collapsing && _timing.CurTime >= pain.CollapseUntil)
            {
                pain.Collapsing = false;
                pain.Amount = 0;
                pain.Encounter = Guid.Empty;
                Dirty(user, pain);
                // Re-evaluate current real damage and other state rules; never force Alive or revive death.
                if (!_mob.IsDead(user))
                {
                    _thresholds.VerifyThresholds(user);
                    EntityManager.System<MobStateActionsSystem>().RefreshActions(user);
                    // A real injury may have reached crit while the mob was already in fake crit.
                    // In that case the threshold system can early-return without replacing its cached state.
                    if (TryComp<DamageableComponent>(user, out var actual) &&
                        _thresholds.TryGetThresholdForState(user, MobState.Critical, out var realCritical) &&
                        actual.TotalDamage >= realCritical.Value)
                        continue;
                    _mob.UpdateMobState(user);
                }
            }
            if (pain.Encounter != Guid.Empty && _timing.CurTime >= pain.Expires) Finish(user, pain.Encounter);
        }
    }
}
