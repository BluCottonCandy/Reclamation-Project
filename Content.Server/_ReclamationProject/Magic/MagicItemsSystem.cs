using System.Linq;
using System.Text;
using Content.Server.Administration.Managers;
using Content.Server.Chat.Managers;
using Content.Server.DoAfter;
using Content.Server.Paper;
using Content.Shared._Misfits.Scavenging;
using Content.Shared._Misfits.Special;
using Content.Shared._ReclamationProject.Magic;
using Content.Shared.Administration;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Utility;

namespace Content.Server._ReclamationProject.Magic;

/// <summary>
/// #Reclamation Project - the Arcane Primer, junk-pile finds, Wild Wasteland's admin marker and the admin Magic verbs.
/// </summary>
public sealed class MagicItemsSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IAdminManager _admins = default!;
    [Dependency] private readonly Content.Server.Administration.Systems.AdminSystem _adminSystem = default!;
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly DoAfterSystem _doAfter = default!;
    [Dependency] private readonly Robust.Server.GameObjects.UserInterfaceSystem _ui = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly MagicSystem _magic = default!;
    [Dependency] private readonly MagicWordsSystem _words = default!;
    [Dependency] private readonly MagicGrimoireSystem _grimoire = default!;
    [Dependency] private readonly ISharedAdminLogManager _adminLog = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedSpecialSystem _special = default!;

    /// <summary>Normal chance a finished junk-pile search drops a magic book.</summary>
    public const float JunkBookChance = 1f / 3500f;
    /// <summary>Chance with Luck 10.</summary>
    public const float JunkBookChanceLuck10 = 1f / 1000f;
    private static readonly string[] JunkBooks = { "MagicGrimoire", "MagicCodexKey", "MagicCodexLeaf", "MagicArcanePrimer", "MagicSpellIndex", "MagicSilentCastingTreatise" };
    // Only the open schools' leaves turn up in junk: the sealed schools' leaves are admin-only.
    private static readonly string[] ClassLeaves =
    {
        "MagicCodexLeafPsychic", "MagicCodexLeafDark",
    };

    /// <summary>Blank vellum is far more common than the books: 1 in 400 per search (1 in 150 with Luck 10).</summary>
    public const float JunkVellumChance = 1f / 400f;
    public const float JunkVellumChanceLuck10 = 1f / 150f;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MagicPrimerComponent, UseInHandEvent>(OnPrimerUse);
        SubscribeLocalEvent<MagicPrimerComponent, MagicPrimerDoAfterEvent>(OnPrimerDone);
        SubscribeLocalEvent<MagicChantlessBookComponent, UseInHandEvent>(OnTreatiseUse);
        SubscribeLocalEvent<MagicUnsealingBookComponent, UseInHandEvent>(OnUnsealingUse);
        SubscribeLocalEvent<MagicUnsealingBookComponent, MagicUnsealDoAfterEvent>(OnUnsealingDone);
        SubscribeLocalEvent<MagicChantlessBookComponent, MagicChantlessDoAfterEvent>(OnTreatiseDone);
        SubscribeLocalEvent<MagicWildWastelandComponent, ExaminedEvent>(OnWildWastelandExamined);
        SubscribeLocalEvent<MagicWildWastelandComponent, ComponentStartup>(OnWildWastelandStartup);
        SubscribeLocalEvent<MagicFEVAwakenedComponent, ExaminedEvent>(OnFevExamined);
        SubscribeLocalEvent<GetVerbsEvent<Verb>>(OnGetAdminVerbs);
        SubscribeLocalEvent<JunkPileSearchableComponent, MagicJunkPileSearchedEvent>(OnJunkSearched);
    }

    #region Primer

    private void OnPrimerUse(Entity<MagicPrimerComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;
        args.Handled = true;

        if (!HasComp<MagicWildWastelandComponent>(args.User))
        {
            _popup.PopupEntity(Loc.GetString("magic-primer-gibberish"), ent, args.User);
            return;
        }

        if (_magic.KnowsArcane(args.User))
        {
            _popup.PopupEntity(Loc.GetString("magic-primer-already"), ent, args.User);
            return;
        }

        _popup.PopupEntity(Loc.GetString("magic-primer-start"), ent, args.User);
        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, TimeSpan.FromSeconds(ent.Comp.ReadSeconds),
            new MagicPrimerDoAfterEvent(), ent, used: ent)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        });
    }

    private void OnPrimerDone(Entity<MagicPrimerComponent> ent, ref MagicPrimerDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;
        args.Handled = true;
        _magic.TeachArcane(args.User);
        _magic.Notify(args.User, Loc.GetString("magic-primer-learned"), PopupType.Medium);
    }

    /// <summary>Admin book: reading it (needs Arcane) unseals every school of magic for the reader. Not used up.</summary>
    private void OnUnsealingUse(Entity<MagicUnsealingBookComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;
        args.Handled = true;

        if (!_magic.KnowsArcane(args.User))
        {
            _popup.PopupEntity(Loc.GetString("magic-primer-gibberish"), ent, args.User);
            return;
        }
        if (TryComp<MagicCasterComponent>(args.User, out var caster) && caster.MagicAllSchoolsUnlocked)
        {
            _magic.Notify(args.User, Loc.GetString("magic-unsealing-already"));
            return;
        }

        _magic.Notify(args.User, Loc.GetString("magic-unsealing-start"));
        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, TimeSpan.FromSeconds(ent.Comp.ReadSeconds),
            new MagicUnsealDoAfterEvent(), ent, used: ent)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        });
    }

    private void OnUnsealingDone(Entity<MagicUnsealingBookComponent> ent, ref MagicUnsealDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;
        args.Handled = true;
        _magic.Notify(args.User, Loc.GetString("magic-unsealing-done"), PopupType.Medium);
        _magic.SetAllSchoolsUnlocked(args.User, true);
    }

    /// <summary>Treatise on Silent Casting: study once (needs Arcane), then practise to cast without charging.</summary>
    private void OnTreatiseUse(Entity<MagicChantlessBookComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;
        args.Handled = true;

        if (!_magic.KnowsArcane(args.User) || !TryComp<MagicCasterComponent>(args.User, out var caster))
        {
            _popup.PopupEntity(Loc.GetString("magic-primer-gibberish"), ent, args.User);
            return;
        }
        if (caster.MagicChantless || caster.MagicChantlessStudied)
        {
            _popup.PopupEntity(Loc.GetString("magic-treatise-already",
                ("left", Math.Max(0, MagicCasterComponent.ChantlessPracticeNeeded - caster.MagicChantlessPractice))), ent, args.User);
            return;
        }

        _popup.PopupEntity(Loc.GetString("magic-treatise-start"), ent, args.User);
        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, TimeSpan.FromSeconds(ent.Comp.ReadSeconds),
            new MagicChantlessDoAfterEvent(), ent, used: ent)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        });
    }

    private void OnTreatiseDone(Entity<MagicChantlessBookComponent> ent, ref MagicChantlessDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || !TryComp<MagicCasterComponent>(args.User, out var caster))
            return;
        args.Handled = true;
        caster.MagicChantlessStudied = true;
        _magic.Notify(args.User, Loc.GetString("magic-treatise-learned", ("casts", MagicCasterComponent.ChantlessPracticeNeeded)), PopupType.Medium);
    }

    #endregion

    #region Codex

    /// <summary>The Codex entries for one reader (see <see cref="MagicWordsSystem.CodexEntries"/>).</summary>
    public List<MagicCodexEntry> CodexEntries(EntityUid? reader) => _words.CodexEntries(reader);

    #endregion

    #region Wild Wasteland

    private void OnWildWastelandStartup(Entity<MagicWildWastelandComponent> ent, ref ComponentStartup args)
    {
        _adminLog.Add(LogType.Action, LogImpact.Medium, $"{ToPrettyString(ent.Owner):player} has the Wild Wasteland trait");
        // Admins' Players list and name overlay pick up the "WW" tag (also when an admin adds it mid-round).
        if (TryComp<ActorComponent>(ent.Owner, out var actor))
            _adminSystem.UpdatePlayerList(actor.PlayerSession);
    }

    private void OnWildWastelandExamined(Entity<MagicWildWastelandComponent> ent, ref ExaminedEvent args)
    {
        if (TryComp<ActorComponent>(args.Examiner, out var actor) && _admins.IsAdmin(actor.PlayerSession))
            args.PushMarkup(Loc.GetString("magic-wild-wasteland-admin-examine"));
    }

    #endregion

    #region Junk piles

    /// <summary>1 in 3500 per finished search, 1 in 1000 with Luck 10 (current Luck, chem boosts count).</summary>
    private void OnJunkSearched(Entity<JunkPileSearchableComponent> ent, ref MagicJunkPileSearchedEvent args)
    {
        var luck = _special.GetEffective(args.User, SpecialStat.Luck);
        if (_random.Prob(luck >= 10 ? JunkVellumChanceLuck10 : JunkVellumChance))
        {
            Spawn("MagicVellum", Transform(ent).Coordinates);
            _popup.PopupEntity(Loc.GetString("magic-junk-vellum"), ent, args.User);
        }

        var chance = luck >= 10 ? JunkBookChanceLuck10 : JunkBookChance;
        if (!_random.Prob(chance))
            return;

        var pick = _random.Pick(JunkBooks);
        if (pick == "MagicCodexLeaf")
            pick = _random.Pick(ClassLeaves);
        var book = Spawn(pick, Transform(ent).Coordinates);
        if (HasComp<MagicGrimoireComponent>(book))
            Spawn("MagicQuill", Transform(ent).Coordinates); // a grimoire is useless without its quill
        _popup.PopupEntity(Loc.GetString("magic-junk-found"), ent, args.User, PopupType.Medium);
        _adminLog.Add(LogType.Action, LogImpact.Medium, $"{ToPrettyString(args.User):player} found magic book {ToPrettyString(book):entity} in a junk pile (Luck {luck})");
    }

    private void OnFevExamined(Entity<MagicFEVAwakenedComponent> ent, ref ExaminedEvent args)
    {
        if (TryComp<ActorComponent>(args.Examiner, out var actor) && _admins.IsAdmin(actor.PlayerSession))
            args.PushMarkup(Loc.GetString("magic-fev-admin-examine"));
    }

    #endregion

    #region Admin verbs

    /// <summary>All magic admin buttons sit in their own "Magic" sub-menu instead of crowding the Admin one.</summary>
    private static VerbCategory? _magicCategory;
    private static VerbCategory MagicCategory => _magicCategory ??=
        new("magic-verb-category", new SpriteSpecifier.Rsi(new ResPath("/Textures/_ReclamationProject/Magic/spells.rsi"), "fireball"));

    private void OnGetAdminVerbs(GetVerbsEvent<Verb> args)
    {
        if (!TryComp<ActorComponent>(args.User, out var actor) || !_admins.HasAdminFlag(actor.PlayerSession, AdminFlags.Admin))
            return;

        var target = args.Target;
        if (!HasComp<Shared.Mobs.Components.MobStateComponent>(target))
            return;

        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("magic-verb-grant-all"),
            Category = MagicCategory,
            Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/plus.svg.192dpi.png")),
            Act = () => _magic.GrantAll(target),
        });

        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("magic-verb-grant-open"),
            Category = MagicCategory,
            Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/plus.svg.192dpi.png")),
            Act = () => _magic.GrantOpenSchools(target),
        });

        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("magic-verb-remove-all"),
            Category = MagicCategory,
            Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/delete_transparent.svg.192dpi.png")),
            ConfirmationPopup = true,
            Act = () => _magic.RemoveAll(target),
        });

        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("magic-verb-give-grimoire"),
            Category = MagicCategory,
            Act = () => _grimoire.GiveBoundGrimoire(target),
        });

        // Every spell written into every grimoire they own (one is given first if they have none).
        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("magic-verb-fill-grimoire"),
            Category = MagicCategory,
            Act = () =>
            {
                var owns = false;
                var books = EntityQueryEnumerator<MagicGrimoireComponent>();
                while (books.MoveNext(out _, out var book))
                    owns |= book.MagicOwner == target;
                if (!owns)
                    _grimoire.GiveBoundGrimoire(target);
                var fill = new MagicFillOwnedBooksEvent(target);
                RaiseLocalEvent(ref fill);
            },
        });

        // Max mana: their mana cap goes to the top and the bar is filled.
        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("magic-verb-max-mana"),
            Category = MagicCategory,
            Act = () => _magic.MaxOutMana(target),
        });

        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("magic-verb-teach-words"),
            Category = MagicCategory,
            Act = () => _grimoire.TeachAllWords(target),
        });

        var unlocked = TryComp<MagicCasterComponent>(target, out var schoolCaster) && schoolCaster.MagicAllSchoolsUnlocked;
        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString(unlocked ? "magic-verb-unseal-off" : "magic-verb-unseal-on"),
            Category = MagicCategory,
            Act = () => _magic.SetAllSchoolsUnlocked(target, !unlocked),
        });

        if (TryComp<MagicCasterComponent>(target, out var targetCaster))
        {
            args.Verbs.Add(new Verb
            {
                Text = Loc.GetString(targetCaster.MagicChantless ? "magic-verb-chantless-off" : "magic-verb-chantless-on"),
                Category = MagicCategory,
                Act = () => _magic.SetChantless((target, targetCaster), !targetCaster.MagicChantless),
            });
        }

        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("magic-verb-show-words"),
            Category = MagicCategory,
            Act = () => _chat.DispatchServerMessage(actor.PlayerSession, WordsReport()),
        });
    }

    /// <summary>This round's base words and every spell's true (Empowered) order. Admin only.</summary>
    public string WordsReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Magic words this round:");
        foreach (var word in _proto.EnumeratePrototypes<MagicWordPrototype>().OrderBy(w => w.ID))
            sb.AppendLine($"  {Loc.GetString(word.Name)} = {_words.GetWordText(word.ID)}");
        sb.AppendLine("Spells (true order):");
        foreach (var spell in _proto.EnumeratePrototypes<MagicSpellPrototype>().OrderBy(s => s.Words.Count).ThenBy(s => s.ID))
            sb.AppendLine($"  {Loc.GetString(spell.Name)} [{spell.ID}] = {_words.GetTrueText(spell)}");
        return sb.ToString();
    }

    #endregion
}

/// <summary>Raised on a junk pile after someone finishes searching it.</summary>
[ByRefEvent]
public record struct MagicJunkPileSearchedEvent(EntityUid User);
