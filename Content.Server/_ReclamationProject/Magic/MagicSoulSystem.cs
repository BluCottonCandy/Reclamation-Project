using Content.Server.Body.Systems;
using Content.Server.Mind;
using Content.Shared._ReclamationProject.Magic;
using Content.Shared.Actions;
using Content.Shared.Damage;
using Content.Shared.Database;
using Content.Shared.Administration.Logs;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Body.Part;
using Content.Shared._ReclamationProject.Medical;
using Robust.Shared.Timing;
using Robust.Shared.Prototypes;

namespace Content.Server._ReclamationProject.Magic;

/// <summary>
/// #Reclamation Project - spells that move the soul:
/// - Undying Will (King): cast while dead and you come back, hurt but alive. Once an hour.
/// - Astral Projection: your mind leaves your body as an unseen spirit (it floats through walls, sees but can't touch
///   or speak) and snaps back when the time runs out, when you press Return, or the moment your body is hurt.
/// </summary>
public sealed class MagicSoulSystem : EntitySystem
{
    [Dependency] private readonly MagicSystem _magic = default!;
    [Dependency] private readonly MindSystem _mind = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly MobThresholdSystem _thresholds = default!;
    [Dependency] private readonly DamageableSystem _damage = default!;
    [Dependency] private readonly BloodstreamSystem _blood = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly Content.Shared.Body.Systems.SharedBodySystem _body = default!;
    [Dependency] private readonly Content.Server._ReclamationProject.Medical.TraumaSystem _trauma = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly ISharedAdminLogManager _adminLog = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MagicSelfReviveEvent>(OnSelfRevive);
        SubscribeLocalEvent<MagicCasterComponent, MobStateChangedEvent>(OnCasterState);
        SubscribeLocalEvent<MagicUndyingChoiceComponent, MagicUndyingChoiceEvent>(OnChoice);
        SubscribeLocalEvent<Content.Server.GameTicking.GhostAttemptHandleEvent>(OnGhostAttempt);
        SubscribeLocalEvent<MagicAstralProjectionEvent>(OnAstral);
        SubscribeLocalEvent<MagicAstralFormComponent, MagicAstralReturnEvent>(OnReturn);
        SubscribeLocalEvent<MagicAstralBodyComponent, DamageChangedEvent>(OnBodyHurt);
        SubscribeLocalEvent<MagicAstralBodyComponent, MobStateChangedEvent>(OnBodyState);
        SubscribeLocalEvent<MagicAstralBodyComponent, ComponentShutdown>(OnBodyShutdown);
    }

    #region Undying Will

    private void OnSelfRevive(MagicSelfReviveEvent args)
    {
        if (args.Handled)
            return;
        var user = args.Performer;
        // Checked before paying: alive, it does nothing and costs nothing.
        if (!_mobState.IsDead(user))
        {
            _magic.Notify(user, Loc.GetString("magic-selfrevive-alive"));
            return;
        }
        if (!_magic.TryPayForSpell(args.Action, user))
            return;
        args.Handled = true;
        Revive(user, args.WoundsLeft);
        _adminLog.Add(LogType.Action, LogImpact.High, $"{ToPrettyString(user):player} brought themself back from death with Undying Will");
    }

    private static readonly ProtoId<MagicSpellPrototype> UndyingWill = "UndyingWill";

    /// <summary>
    /// Died with Undying Will ready (readied in a grimoire or known by heart): its button appears on the hotbar.
    /// Nothing happens by itself; pressing it is the choice to come back. It goes away once you're alive again.
    /// </summary>
    private void OnCasterState(Entity<MagicCasterComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
        {
            if (!EntityManager.System<MagicChatCastSystem>().TryGetSpellAction(ent, UndyingWill, out _))
                return;
            var choice = EnsureComp<MagicUndyingChoiceComponent>(ent);
            _actions.AddAction(ent, ref choice.Action, "ActionMagicUndyingChoice");
            _magic.Notify(ent, Loc.GetString("magic-undying-choice"), PopupType.Medium);
            return;
        }
        if (TryComp<MagicUndyingChoiceComponent>(ent, out var had))
        {
            _actions.RemoveAction(ent, had.Action);
            RemCompDeferred<MagicUndyingChoiceComponent>(ent);
        }
    }

    /// <summary>The player chose to come back: casts the real Undying Will (its mana, its cooldown).</summary>
    private void OnChoice(Entity<MagicUndyingChoiceComponent> ent, ref MagicUndyingChoiceEvent args)
    {
        if (args.Handled)
            return;
        args.Handled = true;
        TryCallBack(ent.Owner);
    }

    /// <summary>Undying Will through the hotbar button or dying words. True if they came back.</summary>
    public bool TryCallBack(EntityUid uid)
    {
        if (!TryComp<MagicCasterComponent>(uid, out var caster)
            || !EntityManager.System<MagicChatCastSystem>().TryGetSpellAction((uid, caster), UndyingWill, out var spell))
            return false;
        if (_actions.TryGetActionData(spell, out var data) && data.Cooldown is { } cd && cd.End > _timing.CurTime)
        {
            _magic.Notify(uid, Loc.GetString("magic-undying-cooldown", ("minutes", (int) MathF.Ceiling((float) (cd.End - _timing.CurTime).TotalMinutes))));
            return false;
        }
        if (!_magic.TryPayForSpell(spell, uid))
            return false;
        Revive(uid, 0.75f);
        _actions.StartUseDelay(spell);
        _adminLog.Add(LogType.Action, LogImpact.High, $"{ToPrettyString(uid):player} chose to come back with Undying Will");
        return true;
    }

    /// <summary>Dying words: someone in crit whispers Undying Will as their Last Words and comes back instead.</summary>
    public bool TryDyingWords(EntityUid uid)
    {
        if (!_mobState.IsCritical(uid))
            return false;
        // The words were just whispered: don't say them a second time.
        var wasTyped = _magic.TypedCastInProgress;
        _magic.TypedCastInProgress = true;
        bool back;
        try
        {
            back = TryCallBack(uid);
        }
        finally
        {
            _magic.TypedCastInProgress = wasTyped;
        }
        if (!back)
            return false;
        EnsureComp<MagicSpareGhostComponent>(uid); // Last Words ghosts you right after speaking: not this time
        return true;
    }

    private void OnGhostAttempt(Content.Server.GameTicking.GhostAttemptHandleEvent args)
    {
        if (args.Handled || args.Mind.CurrentEntity is not { } body || !RemComp<MagicSpareGhostComponent>(body))
            return;
        args.Result = false;
        args.Handled = true;
    }

    /// <summary>Brings a dead body back: wounds healed down to a share of the crit point, blood refilled, bleeding stopped.</summary>
    public void Revive(EntityUid uid, float woundsLeft)
    {
        if (TryComp<DamageableComponent>(uid, out var damageable)
            && _thresholds.TryGetThresholdForState(uid, MobState.Critical, out var crit))
        {
            var total = damageable.TotalDamage.Float();
            var keep = crit.Value.Float() * woundsLeft;
            if (total > keep && total > 0f)
            {
                var heal = damageable.Damage * -((total - keep) / total);
                _damage.TryChangeDamage(uid, heal, ignoreResistances: true, interruptsDoAfters: false, canSever: false);
            }
        }
        _blood.TryModifyBloodLevel(uid, 1000);
        _blood.TryModifyBleedAmount(uid, -1000f);

        if (_mobState.IsDead(uid))
            _mobState.ChangeMobState(uid, MobState.Alive);
        MendLimbs(uid);
        // Stuck shrapnel and bullet fragments come out too, so healing and nanites can work afterwards.
        EntityManager.System<Content.Server._ReclamationProject.Medical.BioMonitor.BioMonitorSystem>().RemoveAllFragments(uid);
        _magic.Flash(uid, MagicClass.Psychic, "Hit");
        _magic.Notify(uid, Loc.GetString("magic-selfrevive-done"), PopupType.Large);
    }

    /// <summary>
    /// Revived limbs work well enough to get away: every limb loses three quarters of its damage and gets some lost
    /// strength back, and broken legs and feet are
    /// set (like a splint), so you can stand and walk.
    /// </summary>
    public void MendLimbs(EntityUid uid, float share = 0.75f)
    {
        foreach (var (part, bodyPart) in _body.GetBodyChildren(uid))
        {
            // Limb damage only heals through the trauma system's own route (plain healing is refused on limbs).
            if (TryComp<DamageableComponent>(part, out var partDamage) && partDamage.TotalDamage > 0)
                _trauma.HealPart(part, partDamage.Damage * -share);
            // A limb worn down to nothing gets some of its strength back, so it works again.
            _trauma.RestoreLostCapacity(part, 25f);
            if (bodyPart.PartType is BodyPartType.Leg or BodyPartType.Foot && HasComp<FractureComponent>(part))
                _trauma.Repair(uid, part, TraumaKind.Fracture, splint: true);
        }
    }

    #endregion

    #region Astral Projection

    private void OnAstral(MagicAstralProjectionEvent args)
    {
        if (args.Handled)
            return;
        var user = args.Performer;
        if (HasComp<MagicAstralBodyComponent>(user) || !_mind.TryGetMind(user, out var mindId, out var mind))
            return;
        if (!_magic.TryPayForSpell(args.Action, user))
            return;
        args.Handled = true;

        var power = TryComp<MagicCasterComponent>(user, out var caster) ? caster.LastPower : 1f;
        var form = Spawn(args.Form, Transform(user).Coordinates);
        _transform.AttachToGridOrMap(form);

        var formComp = EnsureComp<MagicAstralFormComponent>(form);
        formComp.Body = user;
        _actions.AddAction(form, ref formComp.ReturnAction, "ActionMagicAstralReturn");

        var body = EnsureComp<MagicAstralBodyComponent>(user);
        body.Form = form;
        body.Mind = mindId;
        body.End = _timing.CurTime + TimeSpan.FromSeconds(args.Seconds * power);

        _magic.Flash(user, MagicClass.Psychic, "Cast");
        _mind.Visit(mindId, form, mind);
        _magic.Notify(form, Loc.GetString("magic-astral-start", ("seconds", (int) (args.Seconds * power))), PopupType.Medium);
        _adminLog.Add(LogType.Action, LogImpact.Medium, $"{ToPrettyString(user):player} left their body with Astral Projection");
    }

    private void OnReturn(Entity<MagicAstralFormComponent> ent, ref MagicAstralReturnEvent args)
    {
        if (args.Handled)
            return;
        args.Handled = true;
        if (TryComp<MagicAstralBodyComponent>(ent.Comp.Body, out var body))
            EndProjection((ent.Comp.Body, body), null);
    }

    private void OnBodyHurt(Entity<MagicAstralBodyComponent> ent, ref DamageChangedEvent args)
    {
        if (args.DamageIncreased && args.DamageDelta is { } delta && delta.GetTotal() > 0)
            EndProjection(ent, "magic-astral-pulled-back");
    }

    private void OnBodyState(Entity<MagicAstralBodyComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Alive)
            EndProjection(ent, "magic-astral-pulled-back");
    }

    private void OnBodyShutdown(Entity<MagicAstralBodyComponent> ent, ref ComponentShutdown args)
    {
        // The body is gone: the spirit has nowhere to go back to, so the mind is simply let go of the form.
        if (ent.Comp.Mind is { } mindId && TryComp<MindComponent>(mindId, out var mind) && mind.VisitingEntity == ent.Comp.Form)
            _mind.UnVisit(mindId, mind);
        if (ent.Comp.Form is { } form && !TerminatingOrDeleted(form))
            QueueDel(form);
    }

    /// <summary>The spirit snaps back into the body and the form fades.</summary>
    public void EndProjection(Entity<MagicAstralBodyComponent> body, string? why)
    {
        var form = body.Comp.Form;
        if (body.Comp.Mind is { } mindId && TryComp<MindComponent>(mindId, out var mind) && mind.VisitingEntity == form)
            _mind.UnVisit(mindId, mind);
        if (form is { } f && !TerminatingOrDeleted(f))
            QueueDel(f);
        body.Comp.Form = null;
        body.Comp.Mind = null; // already back: the shutdown below has nothing left to do
        RemComp<MagicAstralBodyComponent>(body);
        _magic.Notify(body, Loc.GetString(why ?? "magic-astral-end"), PopupType.Medium);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<MagicAstralBodyComponent>();
        var ending = new List<Entity<MagicAstralBodyComponent>>();
        while (query.MoveNext(out var uid, out var body))
        {
            if (now >= body.End || body.Form is not { } form || TerminatingOrDeleted(form))
                ending.Add((uid, body));
        }
        foreach (var body in ending)
            EndProjection(body, null);
    }

    #endregion
}

/// <summary>Dead with Undying Will ready: holds the hotbar button that brings them back.</summary>
[RegisterComponent]
public sealed partial class MagicUndyingChoiceComponent : Component
{
    [ViewVariables] public EntityUid? Action;
}

/// <summary>Just came back with dying words: the ghosting that Last Words does next is cancelled.</summary>
[RegisterComponent]
public sealed partial class MagicSpareGhostComponent : Component;

/// <summary>On a body whose spirit is out on an Astral Projection.</summary>
[RegisterComponent]
public sealed partial class MagicAstralBodyComponent : Component
{
    [ViewVariables] public EntityUid? Form;
    [ViewVariables] public EntityUid? Mind;
    [ViewVariables] public TimeSpan End;
}

/// <summary>The spirit form itself.</summary>
[RegisterComponent]
public sealed partial class MagicAstralFormComponent : Component
{
    [ViewVariables] public EntityUid Body;
    [ViewVariables] public EntityUid? ReturnAction;
}
