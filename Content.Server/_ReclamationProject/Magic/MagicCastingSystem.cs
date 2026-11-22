using Content.Server.Paper;
using Content.Shared._ReclamationProject.Magic;
using Content.Shared.Actions;
using Content.Shared.Administration.Logs;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;

namespace Content.Server._ReclamationProject.Magic;

/// <summary>
/// #Reclamation Project - Dispel, Blink, paper scrolls, and Mind Etching (writing a spell into your brain).
/// </summary>
public sealed class MagicCastingSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly MagicSystem _magic = default!;
    [Dependency] private readonly MagicWordsSystem _words = default!;
    [Dependency] private readonly ActionContainerSystem _actionContainer = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly DamageableSystem _damage = default!;
    [Dependency] private readonly MetaDataSystem _meta = default!;
    [Dependency] private readonly ISharedAdminLogManager _adminLog = default!;

    private static readonly SoundSpecifier BlinkSound = new SoundPathSpecifier("/Audio/Magic/blink.ogg");

    /// <summary>Mind Etching costs this much mana and this much internal damage.</summary>
    public const float EtchMana = 20f;
    public const float EtchDamage = 5f;
    public const float EtchSeconds = 10f;

    private readonly Dictionary<EntityUid, (ProtoId<MagicSpellPrototype> Spell, string Note)> _pendingEtch = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MagicDispelSpellEvent>(OnDispel);
        SubscribeLocalEvent<MagicBlinkSpellEvent>(OnBlink);
        SubscribeLocalEvent<PaperComponent, GetItemActionsEvent>(OnPaperActions);
        SubscribeLocalEvent<MagicGrimoireComponent, MagicEtchPageMessage>(OnEtchPage);
        SubscribeLocalEvent<MagicTornPageComponent, UseInHandEvent>(OnTornPageUse);
        SubscribeLocalEvent<MagicCasterComponent, MagicEtchDoAfterEvent>(OnEtchDone);
        SubscribeLocalEvent<MagicCasterComponent, GetVerbsEvent<Verb>>(OnForgetVerbs);
    }

    #region Dispel and Blink

    private void OnDispel(MagicDispelSpellEvent args)
    {
        if (args.Handled || !_magic.TryPayForSpell(args.Action, args.Performer))
            return;
        args.Handled = true;
        _magic.Flash(args.Target, _magic.ClassOf(args.Action), "Hit");

        if (!_magic.Dispel(args.Target, args.Cooldown))
            _magic.Notify(args.Performer, Loc.GetString("magic-dispel-nothing"));
        else
            _adminLog.Add(LogType.Action, LogImpact.Low, $"{ToPrettyString(args.Performer):player} dispelled {ToPrettyString(args.Target):player}");
    }

    private void OnBlink(MagicBlinkSpellEvent args)
    {
        if (args.Handled)
            return;

        var xform = Transform(args.Performer);
        if (xform.MapID != args.Target.GetMapId(EntityManager)
            || !_interaction.InRangeUnobstructed(args.Performer, args.Target, range: 1000f, collisionMask: CollisionGroup.Opaque, popup: true))
            return;

        if (!_magic.TryPayForSpell(args.Action, args.Performer))
            return;

        var origin = Transform(args.Performer).Coordinates;
        _transform.SetCoordinates(args.Performer, args.Target);
        _transform.AttachToGridOrMap(args.Performer);
        _magic.TeleportMishap(args.Performer, origin);
        _magic.Flash(args.Performer, _magic.ClassOf(args.Action), "Hit"); // where you land
        args.Handled = true;
    }

    #endregion

    #region Scrolls

    /// <summary>
    /// A sheet of paper with working spell words becomes a one-use scroll in the hands of someone who knows Arcane.
    /// It is not bound to anyone and doesn't use one of the 8 slots, but costs 50% more and crumbles after one cast.
    /// </summary>
    private void OnPaperActions(Entity<PaperComponent> ent, ref GetItemActionsEvent args)
    {
        if (!args.InHands || HasComp<MagicCodexLeafComponent>(ent) || !_magic.KnowsArcane(args.User))
            return;
        if (!_words.TryResolve(ent.Comp.PaperContent, out var spellId, out var empowered) || !_proto.TryIndex(spellId, out var spell))
            return;
        // Sealed schools stay sealed on paper too.
        if (!_magic.CanUseSchool(args.User, spell.Class))
            return;

        var scroll = EnsureComp<MagicScrollComponent>(ent);
        if (scroll.Spell != spellId && scroll.Action is { } old)
        {
            QueueDel(old);
            scroll.Action = null;
        }

        var action = scroll.Action;
        if (!_actionContainer.EnsureAction(ent, ref action, spell.Action))
            return;
        scroll.Action = action;
        scroll.Spell = spellId;

        var spellAction = EnsureComp<MagicSpellActionComponent>(action.Value);
        spellAction.Spell = spellId;
        spellAction.Empowered = empowered;
        spellAction.Scroll = true;
        Dirty(action.Value, spellAction);
        _meta.SetEntityName(action.Value, Loc.GetString("magic-scroll-action"));
        args.AddAction(action);
    }

    #endregion

    #region Mind Etching

    private void OnEtchPage(Entity<MagicGrimoireComponent> ent, ref MagicEtchPageMessage args)
    {
        if (ent.Comp.MagicOwner != args.Actor || args.Page < 0 || args.Page >= ent.Comp.MagicPages.Count)
            return;
        var page = ent.Comp.MagicPages[args.Page];
        if (page.Spell is not { } spell)
        {
            _popup.PopupEntity(Loc.GetString("magic-etch-nothing"), ent, args.Actor);
            return;
        }
        StartEtch(args.Actor, spell, page.Note);
    }

    private void OnTornPageUse(Entity<MagicTornPageComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled || ent.Comp.MagicOwner != args.User || ent.Comp.Page.Spell is not { } spell)
            return;
        args.Handled = true;
        StartEtch(args.User, spell, ent.Comp.Page.Note);
    }

    /// <summary>Starts the 10 s Mind Etching do-after (moving or getting hit cancels it).</summary>
    public bool StartEtch(EntityUid user, ProtoId<MagicSpellPrototype> spell, string note)
    {
        var caster = EnsureComp<MagicCasterComponent>(user);
        if (caster.MagicKnownSpells.Contains(spell))
        {
            _magic.Notify(user, Loc.GetString("magic-etch-already"));
            return false;
        }
        if (!caster.MagicIgnoreActiveLimit && _magic.CountActive(user) >= MagicCasterComponent.ActiveLimit)
        {
            _magic.Notify(user, Loc.GetString("magic-limit-reached"));
            return false;
        }

        _pendingEtch[user] = (spell, note);
        _magic.Notify(user, Loc.GetString("magic-etch-start"));
        return _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user, TimeSpan.FromSeconds(EtchSeconds), new MagicEtchDoAfterEvent(), user)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            BlockDuplicate = true,
        });
    }

    private void OnEtchDone(Entity<MagicCasterComponent> ent, ref MagicEtchDoAfterEvent args)
    {
        if (!_pendingEtch.Remove(ent, out var pending) || args.Cancelled || args.Handled)
            return;
        args.Handled = true;
        Etch(ent, pending.Spell, pending.Note);
    }

    /// <summary>Writes a spell into the caster's mind: no book needed, uses one of the 8 slots, costs mana and some brain strain.</summary>
    public bool Etch(EntityUid user, ProtoId<MagicSpellPrototype> spell, string note)
    {
        var caster = EnsureComp<MagicCasterComponent>(user);
        if (!string.IsNullOrWhiteSpace(note))
            caster.MagicKnownSpellNotes[spell] = note;
        if (!_magic.LearnSpell(user, spell))
        {
            _magic.Notify(user, Loc.GetString("magic-limit-reached"));
            return false;
        }

        _magic.PayMana((user, caster), EtchMana);
        _damage.TryChangeDamage(user, new DamageSpecifier(_proto.Index<DamageTypePrototype>("Cellular"), EtchDamage), ignoreResistances: true, interruptsDoAfters: false);
        _magic.Notify(user, Loc.GetString("magic-etch-done"), PopupType.Medium);
        _adminLog.Add(LogType.Action, LogImpact.Low, $"{ToPrettyString(user):player} etched magic spell {spell} into their mind");
        return true;
    }

    /// <summary>Right-click yourself: memorise a spell cast 10 times, or forget a spell held in your mind (frees the slot).</summary>
    private void OnForgetVerbs(Entity<MagicCasterComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (args.User != args.Target)
            return;

        var n = 1;
        foreach (var (spell, count) in ent.Comp.MagicCastCounts)
        {
            if (count < MagicCasterComponent.MemoriseCasts || ent.Comp.MagicMemorisedSpells.Contains(spell))
                continue;
            var name = ent.Comp.MagicKnownSpellNotes.TryGetValue(spell, out var note) && !string.IsNullOrWhiteSpace(note)
                ? note
                : Loc.GetString("magic-forget-unnamed", ("number", n));
            var target = spell;
            var who = ent.Owner;
            args.Verbs.Add(new Verb
            {
                Text = Loc.GetString("magic-memorise-verb", ("spell", name)),
                Act = () => _magic.Memorise(who, target),
            });
            n++;
        }

        if (ent.Comp.MagicKnownSpells.Count == 0)
            return;

        var i = 1;
        foreach (var spell in ent.Comp.MagicKnownSpells)
        {
            var name = ent.Comp.MagicKnownSpellNotes.TryGetValue(spell, out var note) && !string.IsNullOrWhiteSpace(note)
                ? note
                : Loc.GetString("magic-forget-unnamed", ("number", i));
            var target = spell;
            var user = ent.Owner;
            args.Verbs.Add(new Verb
            {
                Text = Loc.GetString("magic-forget-verb", ("spell", name)),
                ConfirmationPopup = true,
                Act = () =>
                {
                    _magic.ForgetSpell(user, target);
                    _magic.Notify(user, Loc.GetString("magic-forget-done"));
                },
            });
            i++;
        }
    }

    #endregion
}
