using Robust.Shared.GameStates;
using System.Linq;
using Content.Shared._ReclamationProject.Magic;
using Content.Shared.Actions;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Tag;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._ReclamationProject.Magic;

/// <summary>
/// #Reclamation Project - the grimoire: writing pages, binding to its owner, readying spells to the hotbar.
/// The book never tells anyone what a spell is called or what it does. A working page only glows.
/// </summary>
public sealed class MagicGrimoireSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MagicSystem _magic = default!;
    [Dependency] private readonly MagicWordsSystem _words = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly ActionContainerSystem _actionContainer = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly TagSystem _tag = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly MetaDataSystem _meta = default!;
    [Dependency] private readonly ISharedAdminLogManager _adminLog = default!;

    private static readonly ProtoId<TagPrototype> QuillTag = "MagicQuill";
    private const int MaxTextLength = 60;
    private const int MaxNoteLength = 32;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MagicGrimoireComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<MagicGrimoireComponent, ActivatableUIOpenAttemptEvent>(OnOpenAttempt);
        SubscribeLocalEvent<MagicGrimoireComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<MagicGrimoireComponent, MagicWritePageMessage>(OnWrite);
        SubscribeLocalEvent<MagicGrimoireComponent, MagicSetNoteMessage>(OnNote);
        SubscribeLocalEvent<MagicGrimoireComponent, MagicToggleReadyMessage>(OnToggleReady);
        SubscribeLocalEvent<MagicGrimoireComponent, MagicCastPageMessage>(OnCast);
        SubscribeLocalEvent<MagicGrimoireComponent, GetItemActionsEvent>(OnGetActions);
        SubscribeLocalEvent<MagicGrimoireComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<MagicGrimoireComponent, MagicAdminFillGrimoireEvent>(OnAdminFill);
        // The torn-page system already owns InteractUsing on the book, so vellum and leaves hook in from the item side.
        SubscribeLocalEvent<MagicVellumComponent, AfterInteractEvent>(OnVellumUsed);
        SubscribeLocalEvent<MagicCodexLeafComponent, AfterInteractEvent>(OnLeafUsed);
        SubscribeLocalEvent<MagicSpellIndexLeafComponent, AfterInteractEvent>(OnSpellIndexUsed);
        SubscribeLocalEvent<MagicUnreadyAllEvent>(OnUnreadyAll);
        // Secret words never go to anyone but the owner (a cheat client could otherwise read any book nearby).
        SubscribeLocalEvent<MagicGrimoireComponent, ComponentGetStateAttemptEvent>(OnBookStateAttempt);
        SubscribeLocalEvent<MagicTornPageComponent, ComponentGetStateAttemptEvent>(OnTornStateAttempt);
        SubscribeLocalEvent<MagicFillOwnedBooksEvent>(OnFillOwnedBooks);
        SubscribeLocalEvent<MagicSchoolsChangedEvent>(OnSchoolsChanged);
    }

    private void OnBookStateAttempt(Entity<MagicGrimoireComponent> ent, ref ComponentGetStateAttemptEvent args)
    {
        args.Cancelled = args.Player != null && args.Player.AttachedEntity != ent.Comp.MagicOwner;
    }

    private void OnTornStateAttempt(Entity<MagicTornPageComponent> ent, ref ComponentGetStateAttemptEvent args)
    {
        args.Cancelled = args.Player != null && args.Player.AttachedEntity != ent.Comp.MagicOwner;
    }

    private void OnMapInit(Entity<MagicGrimoireComponent> ent, ref MapInitEvent args)
    {
        while (ent.Comp.MagicPages.Count < ent.Comp.PageCount)
            ent.Comp.MagicPages.Add(new MagicPage());
        Dirty(ent);
    }

    private void OnOpenAttempt(Entity<MagicGrimoireComponent> ent, ref ActivatableUIOpenAttemptEvent args)
    {
        if (ent.Comp.MagicOwner is { } owner && owner != args.User)
        {
            args.Cancel();
            _popup.PopupEntity(Loc.GetString("magic-grimoire-wont-open"), ent, args.User);
        }
    }

    private void OnUiOpened(Entity<MagicGrimoireComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateUi(ent, args.Actor);
    }

    private void OnExamined(Entity<MagicGrimoireComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.MagicOwner == null)
            return;
        args.PushMarkup(Loc.GetString(ent.Comp.MagicOwner == args.Examiner ? "magic-grimoire-examine-yours" : "magic-grimoire-examine-bound"));
    }

    #region UI

    private bool HasPen(EntityUid user)
    {
        return _hands.EnumerateHeld(user).Any(held => _tag.HasTag(held, QuillTag));
    }

    public void UpdateUi(Entity<MagicGrimoireComponent> ent, EntityUid? user = null)
    {
        _ui.SetUiState(ent.Owner, MagicGrimoireUiKey.Key, BuildState(ent, user));
    }

    /// <summary>What the owner's window shows: pages, counts, and the Codex leaves bound in (for this owner).</summary>
    public MagicGrimoireBuiState BuildState(Entity<MagicGrimoireComponent> ent, EntityUid? user = null)
    {
        var owner = ent.Comp.MagicOwner ?? user;
        var state = new MagicGrimoireBuiState
        {
            Pages = ent.Comp.MagicPages,
            ActiveLimit = MagicCasterComponent.ActiveLimit,
            MaxPages = ent.Comp.MagicMaxPages,
            HasKey = ent.Comp.MagicHasKey,
            CodexClasses = ent.Comp.MagicCodexClasses.OrderBy(c => c).ToList(),
        };
        var entries = _words.CodexEntries(owner);
        state.Codex = entries.Where(e => ent.Comp.MagicCodexClasses.Contains(e.Class)).ToList();
        state.KnownWords = entries.Where(e => e.Solved).OrderBy(e => e.Class).ThenBy(e => e.Name).ToList();

        if (owner is { } o)
        {
            state.Readied = _magic.CountActive(o);
            state.HasSpellIndex = ent.Comp.MagicHasSpellIndex;
            if (ent.Comp.MagicHasSpellIndex)
                state.KnownSpells = KnownSpells(o, ent.Comp);
            state.HasPen = HasPen(o);
            if (TryComp<MagicCasterComponent>(o, out var caster))
            {
                state.Mana = caster.MagicMana;
                state.MaxMana = caster.MagicMaxMana;
            }
        }

        return state;
    }

    /// <summary>Every spell this person knows, with what it does and where they hold it (for the Spells tab).</summary>
    private List<MagicKnownSpellEntry> KnownSpells(EntityUid owner, MagicGrimoireComponent book)
    {
        var where = new Dictionary<string, List<string>>();
        void Add(string spell, string place)
        {
            if (!where.TryGetValue(spell, out var places))
                where[spell] = places = new List<string>();
            if (!places.Contains(place))
                places.Add(place);
        }

        if (TryComp<MagicCasterComponent>(owner, out var caster))
        {
            foreach (var id in caster.MagicKnownSpells)
                Add(id, Loc.GetString(caster.MagicMemorisedSpells.Contains(id) ? "magic-spells-where-memorised" : "magic-spells-where-mind"));
            foreach (var id in caster.MagicMemorisedSpells)
                Add(id, Loc.GetString("magic-spells-where-memorised"));
        }
        for (var i = 0; i < book.MagicPages.Count; i++)
        {
            var page = book.MagicPages[i];
            if (page.Glow != null && page.Spell is { } id)
                Add(id, Loc.GetString(page.Readied ? "magic-spells-where-page-readied" : "magic-spells-where-page", ("page", i + 1)));
        }

        var list = new List<MagicKnownSpellEntry>();
        foreach (var (id, places) in where)
        {
            if (!_proto.TryIndex<MagicSpellPrototype>(id, out var spell))
                continue;
            var words = spell.Words.Select(w => _proto.TryIndex(w, out var word) ? Loc.GetString(word.Name) : w.Id);
            list.Add(new MagicKnownSpellEntry
            {
                Name = Loc.GetString(spell.Name),
                Class = spell.Class,
                Words = string.Join(" + ", words),
                Incantation = _words.GetTrueText(spell),
                ManaCost = (int) MathF.Round(spell.ManaCost),
                Summary = spell.Summary ?? string.Empty,
                Where = string.Join(", ", places.Take(3)) + (places.Count > 3 ? ", ..." : string.Empty),
            });
        }
        return list.OrderBy(e => e.Class).ThenBy(e => e.Name).ToList();
    }

    private bool ValidPage(Entity<MagicGrimoireComponent> ent, int page) => page >= 0 && page < ent.Comp.MagicPages.Count;

    private void OnWrite(Entity<MagicGrimoireComponent> ent, ref MagicWritePageMessage args)
    {
        // A tampered client could send no text at all: treat it as empty rather than crash.
        WritePage(ent, args.Actor, args.Page, args.Text ?? string.Empty);
    }

    /// <summary>Writes words on a page (needs a pen in hand). Returns false if the write was refused.</summary>
    public bool WritePage(Entity<MagicGrimoireComponent> ent, EntityUid user, int index, string text)
    {
        if (!ValidPage(ent, index) || (ent.Comp.MagicOwner is { } owner && owner != user))
            return false;

        if (!HasPen(user))
        {
            _popup.PopupEntity(Loc.GetString("magic-grimoire-need-pen"), ent, user);
            return false;
        }

        var page = ent.Comp.MagicPages[index];
        if (page.Readied)
            SetReadied(ent, index, false);

        page.Text = text.Length > MaxTextLength ? text[..MaxTextLength] : text;
        ResolvePage(ent, user, page);

        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"{ToPrettyString(user):player} wrote \"{page.Text}\" in grimoire {ToPrettyString(ent.Owner):entity} page {index + 1} (spell: {page.Spell?.Id ?? "none"}{(page.Empowered ? ", empowered" : "")}{(page.Sealed != null ? ", sealed" : "")})");

        Dirty(ent);
        UpdateUi(ent, user);
        return true;
    }

    /// <summary>
    /// Reads a page's words: it wakes (a working spell), stays sealed (a real spell of a school sealed to the
    /// writer), or stays dark (wrong or misspelled words, which all look the same).
    /// </summary>
    private void ResolvePage(Entity<MagicGrimoireComponent> ent, EntityUid user, MagicPage page, bool quiet = false)
    {
        page.Spell = null;
        page.Empowered = false;
        page.Glow = null;
        page.Sealed = null;

        // The page only wakes for someone who knows Arcane. Wrong and misspelled words look the same.
        if (!_magic.KnowsArcane(user) || !_words.TryResolve(page.Text, out var spellId, out var empowered)
            || !_proto.TryIndex(spellId, out var spell))
            return;

        // Only Psychic and Dark are open until an admin unseals the other schools.
        if (!_magic.CanUseSchool(user, spell.Class))
        {
            page.Sealed = spell.Class;
            if (!quiet)
                _magic.Notify(user, Loc.GetString("magic-page-sealed"), PopupType.Medium);
            return;
        }

        page.Spell = spellId;
        page.Empowered = empowered;
        page.Glow = spell.Class;

        // The Codex now names and spells out every word of this spell for them.
        var solver = EnsureComp<MagicCasterComponent>(user);
        foreach (var word in spell.Words)
            solver.MagicSolvedWords.Add(word);
        _magic.GrantPsychicTelepathy(user, spell.Class);

        if (ent.Comp.MagicOwner == null)
        {
            ent.Comp.MagicOwner = user;
            EnsureComp<MagicCasterComponent>(user);
            _popup.PopupEntity(Loc.GetString("magic-grimoire-binds"), ent, user, PopupType.Medium);
            _adminLog.Add(LogType.Action, LogImpact.Medium, $"{ToPrettyString(user):player} bound grimoire {ToPrettyString(ent.Owner):entity}");
        }
    }

    /// <summary>Schools unsealed (or sealed again): every page in the owner's books is read again.</summary>
    private void OnSchoolsChanged(ref MagicSchoolsChangedEvent args)
    {
        var query = EntityQueryEnumerator<MagicGrimoireComponent>();
        while (query.MoveNext(out var uid, out var book))
        {
            if (book.MagicOwner != args.Owner)
                continue;
            for (var i = 0; i < book.MagicPages.Count; i++)
            {
                var page = book.MagicPages[i];
                if (string.IsNullOrWhiteSpace(page.Text))
                    continue;
                var wasReadied = page.Readied;
                ResolvePage((uid, book), args.Owner, page, quiet: true);
                // A page that went quiet can't stay on the hotbar.
                if (wasReadied && page.Spell == null)
                    SetReadied((uid, book), i, false);
            }
            Dirty(uid, book);
            UpdateUi((uid, book));
        }
    }

    private void OnNote(Entity<MagicGrimoireComponent> ent, ref MagicSetNoteMessage args)
    {
        if (!ValidPage(ent, args.Page) || (ent.Comp.MagicOwner is { } owner && owner != args.Actor))
            return;

        var page = ent.Comp.MagicPages[args.Page];
        var note = args.Note ?? string.Empty;
        page.Note = note.Length > MaxNoteLength ? note[..MaxNoteLength] : note;
        if (ent.Comp.PageActions.TryGetValue(args.Page, out var action))
            _meta.SetEntityName(action, ActionName(page));
        Dirty(ent);
        UpdateUi(ent, args.Actor);
    }

    private void OnToggleReady(Entity<MagicGrimoireComponent> ent, ref MagicToggleReadyMessage args)
    {
        TryToggleReady(ent, args.Actor, args.Page);
    }

    /// <summary>Readies or un-readies a page for its owner, respecting the 8 active spell limit.</summary>
    public bool TryToggleReady(Entity<MagicGrimoireComponent> ent, EntityUid user, int index)
    {
        if (!ValidPage(ent, index) || ent.Comp.MagicOwner != user)
            return false;

        var page = ent.Comp.MagicPages[index];
        if (!page.Readied)
        {
            if (page.Spell == null)
                return false;
            var limitFree = TryComp<MagicCasterComponent>(user, out var caster) && caster.MagicIgnoreActiveLimit;
            if (!limitFree && _magic.CountActive(user) >= MagicCasterComponent.ActiveLimit)
            {
                _popup.PopupEntity(Loc.GetString("magic-limit-reached"), ent, user);
                return false;
            }
        }

        SetReadied(ent, index, !page.Readied);
        UpdateUi(ent, user);
        return true;
    }

    private void OnCast(Entity<MagicGrimoireComponent> ent, ref MagicCastPageMessage args)
    {
        if (!ValidPage(ent, args.Page) || ent.Comp.MagicOwner != args.Actor)
            return;

        if (!ent.Comp.PageActions.TryGetValue(args.Page, out var action))
        {
            _popup.PopupEntity(Loc.GetString("magic-grimoire-ready-first"), ent, args.Actor);
            return;
        }

        if (!TryComp<InstantActionComponent>(action, out var instant) || instant.Event == null)
        {
            _popup.PopupEntity(Loc.GetString("magic-grimoire-aim-hotbar"), ent, args.Actor);
            return;
        }

        if (!TryComp<ActionsComponent>(args.Actor, out var actions) || instant.AttachedEntity != args.Actor)
            return;
        if (instant.Cooldown is { } cd && cd.End > _timing.CurTime)
            return;

        _actions.PerformAction(args.Actor, actions, action, instant, instant.Event, _timing.CurTime, predicted: false);
    }

    #endregion

    #region Vellum and Codex leaves

    private bool CanBindInto(EntityUid? target, EntityUid user, bool canReach, out Entity<MagicGrimoireComponent> book)
    {
        book = default;
        if (!canReach || target is not { } t || !TryComp<MagicGrimoireComponent>(t, out var comp))
            return false;
        book = (t, comp);
        if (comp.MagicOwner is { } owner && owner != user)
        {
            _popup.PopupEntity(Loc.GetString("magic-grimoire-wont-open"), t, user);
            return false;
        }
        return true;
    }

    private void OnVellumUsed(Entity<MagicVellumComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !CanBindInto(args.Target, args.User, args.CanReach, out var book))
            return;
        args.Handled = true;
        AddPages(book, args.User, ent, ent.Comp.Pages);
    }

    private void OnLeafUsed(Entity<MagicCodexLeafComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !CanBindInto(args.Target, args.User, args.CanReach, out var book))
            return;
        args.Handled = true;
        BindLeaf(book, args.User, ent, ent.Comp.Class);
    }

    private void OnSpellIndexUsed(Entity<MagicSpellIndexLeafComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !CanBindInto(args.Target, args.User, args.CanReach, out var book))
            return;
        args.Handled = true;
        BindSpellIndex(book, args.User, ent);
    }

    /// <summary>Binds the Spell Index into the book (used up): adds the Spells tab.</summary>
    public bool BindSpellIndex(Entity<MagicGrimoireComponent> ent, EntityUid user, EntityUid used)
    {
        if (ent.Comp.MagicHasSpellIndex)
        {
            _popup.PopupEntity(Loc.GetString("magic-leaf-already"), ent, user);
            return false;
        }
        ent.Comp.MagicHasSpellIndex = true;
        QueueDel(used);
        Dirty(ent);
        _popup.PopupEntity(Loc.GetString("magic-spell-index-bound"), ent, user);
        UpdateUi(ent, user);
        return true;
    }

    /// <summary>Stitches blank pages into the book (used up), up to its page limit.</summary>
    public bool AddPages(Entity<MagicGrimoireComponent> ent, EntityUid user, EntityUid used, int pages)
    {
        if (ent.Comp.MagicPages.Count >= ent.Comp.MagicMaxPages)
        {
            _popup.PopupEntity(Loc.GetString("magic-vellum-full"), ent, user);
            return false;
        }

        var added = 0;
        while (added < pages && ent.Comp.MagicPages.Count < ent.Comp.MagicMaxPages)
        {
            ent.Comp.MagicPages.Add(new MagicPage());
            added++;
        }

        QueueDel(used);
        Dirty(ent);
        _popup.PopupEntity(Loc.GetString("magic-vellum-stitched"), ent, user);
        UpdateUi(ent, user);
        return true;
    }

    /// <summary>Binds a Codex leaf into the book's Codex tab (used up). Null class = the Key of Runes.</summary>
    public bool BindLeaf(Entity<MagicGrimoireComponent> ent, EntityUid user, EntityUid used, MagicClass? leafClass)
    {
        var already = leafClass is { } c ? !ent.Comp.MagicCodexClasses.Add(c) : ent.Comp.MagicHasKey;
        if (already)
        {
            _popup.PopupEntity(Loc.GetString("magic-leaf-already"), ent, user);
            return false;
        }

        if (leafClass == null)
            ent.Comp.MagicHasKey = true;

        QueueDel(used);
        Dirty(ent);
        _popup.PopupEntity(Loc.GetString("magic-leaf-bound"), ent, user);
        UpdateUi(ent, user);
        return true;
    }

    #endregion

    #region Readying and actions

    private string ActionName(MagicPage page)
    {
        if (!string.IsNullOrWhiteSpace(page.Note))
            return page.Note;
        if (page.Glow != null && page.Spell is { } id && _proto.TryIndex(id, out var spell))
            return Loc.GetString(spell.Name);
        return "· · ·";
    }

    /// <summary>True if the book is in the owner's hands or in one of their worn slots (belt, pocket...).</summary>
    private bool IsOnOwner(Entity<MagicGrimoireComponent> ent, out EntityUid owner)
    {
        owner = ent.Comp.MagicOwner ?? EntityUid.Invalid;
        return ent.Comp.MagicOwner != null
               && _container.TryGetContainingContainer((ent.Owner, null, null), out var container)
               && container.Owner == owner;
    }

    public void SetReadied(Entity<MagicGrimoireComponent> ent, int index, bool readied)
    {
        var page = ent.Comp.MagicPages[index];
        if (readied && page.Spell is { } spellId && _proto.TryIndex(spellId, out var spell))
        {
            var action = _actionContainer.AddAction(ent, spell.Action);
            if (action == null)
                return;

            var spellAction = EnsureComp<MagicSpellActionComponent>(action.Value);
            spellAction.Spell = spellId;
            spellAction.Empowered = page.Empowered;
            Dirty(action.Value, spellAction);
            _meta.SetEntityName(action.Value, ActionName(page));

            ent.Comp.PageActions[index] = action.Value;
            page.Readied = true;

            if (IsOnOwner(ent, out var owner))
                _actions.GrantContainedAction(owner, ent.Owner, action.Value);
        }
        else
        {
            page.Readied = false;
            if (ent.Comp.PageActions.Remove(index, out var action))
            {
                _actions.RemoveAction(action);
                QueueDel(action);
            }
        }

        Dirty(ent);
    }

    private void OnGetActions(Entity<MagicGrimoireComponent> ent, ref GetItemActionsEvent args)
    {
        if (ent.Comp.MagicOwner != args.User)
            return;
        foreach (var action in ent.Comp.PageActions.Values)
            args.AddAction(action);
    }

    private void OnUnreadyAll(ref MagicUnreadyAllEvent args)
    {
        var query = EntityQueryEnumerator<MagicGrimoireComponent>();
        while (query.MoveNext(out var uid, out var book))
        {
            if (book.MagicOwner != args.Owner)
                continue;
            for (var i = 0; i < book.MagicPages.Count; i++)
            {
                if (book.MagicPages[i].Readied)
                    SetReadied((uid, book), i, false);
            }
        }
    }

    #endregion

    #region Admin

    private void OnAdminFill(Entity<MagicGrimoireComponent> ent, ref MagicAdminFillGrimoireEvent args)
    {
        if (ent.Comp.MagicOwner == null && _container.TryGetContainingContainer((ent.Owner, null, null), out var container))
            ent.Comp.MagicOwner = container.Owner;
        // An admin filling a book means every school: unseal its owner so the pages really work.
        if (ent.Comp.MagicOwner is { } fillOwner)
            _magic.SetAllSchoolsUnlocked(fillOwner, true);
        FillAll(ent);
    }

    private void OnFillOwnedBooks(ref MagicFillOwnedBooksEvent args)
    {
        var query = EntityQueryEnumerator<MagicGrimoireComponent>();
        while (query.MoveNext(out var uid, out var book))
        {
            if (book.MagicOwner == args.Owner)
                FillAll((uid, book));
        }
        TeachAllWords(args.Owner);
    }

    /// <summary>Admin: the person knows every word (Codex and Words tab), and their books get every Codex leaf.</summary>
    public void TeachAllWords(EntityUid user)
    {
        var caster = EnsureComp<MagicCasterComponent>(user);
        foreach (var word in _proto.EnumeratePrototypes<MagicWordPrototype>())
            caster.MagicSolvedWords.Add(word.ID);
        Dirty(user, caster);

        var query = EntityQueryEnumerator<MagicGrimoireComponent>();
        while (query.MoveNext(out var uid, out var book))
        {
            if (book.MagicOwner != user)
                continue;
            foreach (var cls in Enum.GetValues<MagicClass>())
                book.MagicCodexClasses.Add(cls);
            book.MagicHasKey = true;
            book.MagicHasSpellIndex = true;
            Dirty(uid, book);
            UpdateUi((uid, book));
        }
    }

    /// <summary>Writes every spell into the book (true order, so all pages are Empowered).</summary>
    public void FillAll(Entity<MagicGrimoireComponent> ent)
    {
        var spells = _proto.EnumeratePrototypes<MagicSpellPrototype>().OrderBy(s => s.Words.Count).ThenBy(s => s.ID).ToList();
        while (ent.Comp.MagicPages.Count < spells.Count)
            ent.Comp.MagicPages.Add(new MagicPage());

        for (var i = 0; i < spells.Count; i++)
        {
            var page = ent.Comp.MagicPages[i];
            if (page.Readied)
                SetReadied(ent, i, false);
            page.Text = _words.GetTrueText(spells[i]);
            page.Spell = spells[i].ID;
            page.Empowered = spells[i].Words.Count > 1;
            page.Glow = spells[i].Class;
        }

        // The owner now knows every word too (Codex and Words tab).
        if (ent.Comp.MagicOwner is { } owner)
        {
            var solver = EnsureComp<MagicCasterComponent>(owner);
            foreach (var spell in spells)
            {
                foreach (var word in spell.Words)
                    solver.MagicSolvedWords.Add(word);
            }
        }

        Dirty(ent);
        UpdateUi(ent);
    }

    /// <summary>Spawns a blank grimoire already bound to someone, in their hands if possible.</summary>
    public EntityUid GiveBoundGrimoire(EntityUid user)
    {
        var book = Spawn("MagicGrimoire", Transform(user).Coordinates);
        var comp = EnsureComp<MagicGrimoireComponent>(book);
        comp.MagicOwner = user;
        Dirty(book, comp);
        EnsureComp<MagicCasterComponent>(user);
        _hands.TryPickupAnyHand(user, book);
        var quill = Spawn("MagicQuill", Transform(user).Coordinates);
        _hands.TryPickupAnyHand(user, quill);
        return book;
    }

    #endregion
}
