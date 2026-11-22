using System.Linq;
using System.Numerics;
using Content.Shared._ReclamationProject.Magic;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Timing;

namespace Content.Client._ReclamationProject.Magic;

/// <summary>
/// #Reclamation Project - the grimoire window ("Liber Arcanum"), approved mockup v21.
/// Opens on the closed, belted book; then a slim two-page book with bookmark tabs on the right.
/// Tabs: Ready, Codex and New always; a class tab only once a page of that class glows.
/// A working page only glows in its class colour - the window never names a spell or shows its stats.
/// </summary>
public sealed class MagicGrimoireWindow : BaseWindow
{
    public static readonly Color Gold = Color.FromHex("#b08a3e");
    public static readonly Color Parchment = Color.FromHex("#ece0c2");
    public static readonly Color Cover = Color.FromHex("#1c1028");
    public static readonly Color Ink = Color.FromHex("#3a2410");
    public static readonly Color DarkRed = Color.FromHex("#7a1f1f");

    public static (Color Dark, Color Light) ClassColors(MagicClass c) => c switch
    {
        MagicClass.Elemental => (Color.FromHex("#D85A30"), Color.FromHex("#FAC775")),
        MagicClass.Psychic => (Color.FromHex("#378ADD"), Color.FromHex("#B5D4F4")),
        MagicClass.Dark => (Color.FromHex("#7F77DD"), Color.FromHex("#CECBF6")),
        MagicClass.Blood => (Color.FromHex("#E24B4A"), Color.FromHex("#F7C1C1")),
        MagicClass.Healing => (Color.FromHex("#5F5E5A"), Color.FromHex("#D3D1C7")),
        MagicClass.Conjuration => (Color.FromHex("#BA7517"), Color.FromHex("#FAC775")),
        MagicClass.Atomic => (Color.FromHex("#639922"), Color.FromHex("#C0DD97")),
        _ => (Ink, Ink),
    };

    private static Color TabColor(int chapter) => chapter switch
    {
        Readied => Color.FromHex("#c9a85a"),
        Codex => Color.FromHex("#d4b06a"),
        Words => Color.FromHex("#c7b59a"),
        Spells => Color.FromHex("#b9c4d8"),
        New => Color.FromHex("#cbb98e"),
        (int) MagicClass.Elemental => Color.FromHex("#e08a62"),
        (int) MagicClass.Atomic => Color.FromHex("#97c459"),
        (int) MagicClass.Psychic => Color.FromHex("#78aee6"),
        (int) MagicClass.Dark => Color.FromHex("#a9a2e8"),
        (int) MagicClass.Blood => Color.FromHex("#ec7f7e"),
        (int) MagicClass.Healing => Color.FromHex("#c4c2b8"),
        _ => Color.FromHex("#d9a24e"),
    };

    private static readonly MagicClass[] AllClasses =
    {
        MagicClass.Elemental, MagicClass.Atomic, MagicClass.Psychic, MagicClass.Dark,
        MagicClass.Blood, MagicClass.Healing, MagicClass.Conjuration,
    };

    private static string ClassKey(MagicClass c) => c.ToString().ToLowerInvariant();

    private static readonly string[] Roman =
    {
        "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X", "XI", "XII", "XIII", "XIV", "XV", "XVI", "XVII",
        "XVIII", "XIX", "XX",
    };

    private static string Numeral(int i) => i < Roman.Length ? Roman[i] : (i + 1).ToString();

    // Chapters: the special tabs are negative, class tabs use the MagicClass value.
    private const int Spells = -5;
    private const int Words = -4;
    private const int Readied = -3;
    private const int Codex = -2;
    private const int New = -1;

    public event Action<int, string>? OnWrite;
    public event Action<int, string>? OnNote;
    public event Action<int>? OnToggleReady;
    public event Action<int>? OnCast;
    public event Action<int>? OnTearOut;
    public event Action<int>? OnEtch;

    private readonly MagicClosedBook _closed;
    private readonly BoxContainer _open;
    private readonly BoxContainer _tabs;
    private readonly Label _header;
    private readonly MagicPageFlip _flip;
    private readonly MagicToast _toast;

    // Left page.
    private readonly MagicPageDecor _leftDecor;
    private readonly Label _leftTitle;
    private readonly BoxContainer _leftList;
    private readonly ScrollContainer _leftScroll;
    private readonly MagicWrapLabel _leftHint;
    private readonly BoxContainer _keyBox;
    private readonly Label _keyTitle;
    private readonly MagicWrapLabel _keyHint;
    private readonly GridContainer _keyGrid;
    private readonly MagicGlyphParagraph _leftWake;

    // Right page.
    private readonly MagicPageDecor _rightDecor;
    private readonly BoxContainer _folioBox;
    private readonly BoxContainer _emptyBox;
    private readonly Label _emptyTitle;
    private readonly MagicWrapLabel _emptyHint;
    private readonly MagicCodexKeyPage _codexKey = new();
    private readonly MagicCodexWordsPage _codexWords = new();
    private readonly MagicKnownWordPage _knownWord = new();
    private readonly MagicKnownSpellPage _knownSpell = new();
    private readonly Label _folio;
    private readonly MagicGlyphParagraph _wake;
    private readonly MagicGlyphControl _glyphs;
    private readonly Label _state;
    private readonly BoxContainer _writeBox;
    private readonly LineEdit _words;
    private readonly LineEdit _note;
    private readonly MagicSealButton _writeSeal;
    private readonly MagicSealButton _inscribe;
    private readonly MagicSealButton _ready;
    private readonly MagicLinkButton _back;
    private readonly MagicLinkButton _next;

    private MagicGrimoireBuiState? _last;
    private int _chapter = New;
    private int _sel;
    private bool _writing;
    private int _leaf;
    private int? _pendingPage;
    private int _shownPage = -1;
    private float _typingTimer;
    private float _noteTimer;
    private int _notePage = -1;
    private MagicClass? _growClass;
    private float _growAt;
    private float _clock;
    private bool _closing;
    private bool _chapterChosen;

    public MagicGrimoireWindow()
    {
        // No window frame or title bar: only the book itself shows. Drag it by its frame; the gold mark closes it.
        MinSize = new Vector2(700, 520);
        SetSize = new Vector2(720, 540);
        // A bare window ignores the mouse by default (DefaultWindow turns this on itself): needed for dragging.
        MouseFilter = MouseFilterMode.Stop;

        var root = new PanelContainer { PanelOverride = new StyleBoxEmpty() };
        AddChild(root);

        _closed = new MagicClosedBook();
        // Both happen inside a frame update; adding/removing the window then breaks the UI's child loop.
        _closed.OnOpened += () => UserInterfaceManager.DeferAction(ShowBook);
        _closed.OnClosed += () => UserInterfaceManager.DeferAction(() => base.Close());
        root.AddChild(_closed);

        // The open book with the tabs hanging off its right side.
        _open = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, Visible = false, Margin = new Thickness(8, 6, 0, 6) };
        root.AddChild(_open);

        var closeMark = new MagicCloseMark
        {
            HorizontalAlignment = HAlignment.Right,
            VerticalAlignment = VAlignment.Top,
            Margin = new Thickness(0, 9, 56, 0),
        };
        closeMark.OnPressed += Close;
        root.AddChild(closeMark);

        var tome = new PanelContainer { HorizontalExpand = true, PanelOverride = new StyleBoxEmpty() };
        tome.AddChild(new MagicTomeFrame());
        var frame = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, Margin = new Thickness(10, 3, 10, 3), SeparationOverride = 1 };
        tome.AddChild(frame);
        _open.AddChild(tome);

        _tabs = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 1, Margin = new Thickness(0, 46, 0, 0), MinWidth = 46 };
        _open.AddChild(_tabs);

        frame.AddChild(RuneStrip());
        _header = new Label
        {
            HorizontalAlignment = HAlignment.Center,
            FontColorOverride = Color.FromHex("#e6cf92"),
            FontOverride = MagicFonts.Gothic(15, 1f),
            Margin = new Thickness(0, 0, 0, 3),
        };
        frame.AddChild(_header);

        var bookStack = new PanelContainer { VerticalExpand = true, PanelOverride = new StyleBoxFlat { BackgroundColor = Color.FromHex("#120a1a") } };
        frame.AddChild(bookStack);
        frame.AddChild(RuneStrip(8f));

        var pages = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 0 };
        bookStack.AddChild(pages);
        _flip = new MagicPageFlip();
        bookStack.AddChild(_flip);
        _toast = new MagicToast();
        bookStack.AddChild(_toast);

        // ---- Left page ----
        var left = new PanelContainer { HorizontalExpand = true, VerticalExpand = true, PanelOverride = new StyleBoxEmpty() };
        _leftDecor = new MagicPageDecor();
        left.AddChild(_leftDecor);
        var leftBox = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, Margin = new Thickness(16, 12, 16, 10), SeparationOverride = 2 };
        left.AddChild(leftBox);
        pages.AddChild(left);

        _leftTitle = Gothic(18, DarkRed);
        leftBox.AddChild(_leftTitle);
        leftBox.AddChild(new MagicDivider());
        _leftScroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false };
        _leftList = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 1 };
        _leftScroll.AddChild(_leftList);
        leftBox.AddChild(_leftScroll);

        _keyBox = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, VerticalExpand = true, Visible = false, SeparationOverride = 3 };
        _keyTitle = Gothic(18, DarkRed);
        _keyBox.AddChild(_keyTitle);
        _keyBox.AddChild(new MagicDivider());
        _keyHint = Small(string.Empty);
        _keyBox.AddChild(_keyHint);
        _keyGrid = new GridContainer { Columns = 5, HSeparationOverride = 2, VSeparationOverride = 1, HorizontalAlignment = HAlignment.Center };
        for (var c = 'A'; c <= 'Z'; c++)
        {
            var key = new MagicKeyRune(c);
            key.OnPressed += AppendRune;
            _keyGrid.AddChild(key);
        }
        _keyBox.AddChild(_keyGrid);
        _keyBox.AddChild(Small(Loc.GetString("magic-grimoire-key-codex")));
        leftBox.AddChild(_keyBox);

        _leftHint = Small(string.Empty);
        leftBox.AddChild(_leftHint);
        _leftWake = new MagicGlyphParagraph(3, 30) { MinHeight = 44, HorizontalExpand = true, Margin = new Thickness(0, 4, 0, 0) };
        leftBox.AddChild(_leftWake);

        // ---- Spine with the red ribbon ----
        var spine = new PanelContainer { MinWidth = 6, MaxWidth = 6, PanelOverride = new StyleBoxFlat { BackgroundColor = Color.FromHex("#120a1a") } };
        spine.AddChild(new PanelContainer
        {
            MinWidth = 4, MaxWidth = 4, MinHeight = 220, HorizontalAlignment = HAlignment.Center, VerticalAlignment = VAlignment.Top,
            PanelOverride = new StyleBoxFlat { BackgroundColor = Color.FromHex("#a82020") },
        });
        pages.AddChild(spine);

        // ---- Right page ----
        var right = new PanelContainer { HorizontalExpand = true, VerticalExpand = true, PanelOverride = new StyleBoxEmpty() };
        _rightDecor = new MagicPageDecor { RightSide = true, Seed = 2 };
        right.AddChild(_rightDecor);
        var rightBox = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, Margin = new Thickness(16, 12, 16, 8) };
        right.AddChild(rightBox);
        pages.AddChild(right);

        var content = new PanelContainer { VerticalExpand = true, PanelOverride = new StyleBoxEmpty() };
        rightBox.AddChild(content);

        _folioBox = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 2 };
        content.AddChild(_folioBox);
        _emptyBox = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 4, Visible = false };
        content.AddChild(_emptyBox);
        _codexKey.Visible = false;
        content.AddChild(_codexKey);
        _codexWords.Visible = false;
        content.AddChild(_codexWords);
        _knownWord.Visible = false;
        content.AddChild(_knownWord);
        _knownSpell.Visible = false;
        content.AddChild(_knownSpell);

        _emptyTitle = Gothic(18, DarkRed);
        _emptyBox.AddChild(_emptyTitle);
        _emptyBox.AddChild(new MagicDivider());
        _emptyHint = Small(string.Empty);
        _emptyBox.AddChild(_emptyHint);
        _emptyBox.AddChild(new MagicGlyphParagraph(5, 40) { MinHeight = 80, HorizontalExpand = true });

        _folio = Gothic(16, DarkRed);
        _folioBox.AddChild(_folio);
        _folioBox.AddChild(new MagicDivider());
        _wake = new MagicGlyphParagraph(9, 16) { MinHeight = 30, HorizontalExpand = true };
        _folioBox.AddChild(_wake);

        var band = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat { BackgroundColor = Color.Transparent, BorderColor = Color.FromHex("#c4a66a"), BorderThickness = new Thickness(0, 1) },
            Margin = new Thickness(0, 2, 0, 0),
        };
        _glyphs = new MagicGlyphControl { GlyphHeight = 22f, MinHeight = 34 };
        band.AddChild(_glyphs);
        _folioBox.AddChild(band);
        _state = new Label { FontColorOverride = Color.FromHex("#6b4a24"), HorizontalAlignment = HAlignment.Center, FontOverride = MagicFonts.Serif(11, 1f) };
        _folioBox.AddChild(_state);

        // Writing tools: shown only while Write is pressed.
        _writeBox = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 2, Visible = false };
        _words = new LineEdit { PlaceHolder = Loc.GetString("magic-grimoire-inscribe"), StyleBoxOverride = InkBox() };
        _words.OnTextChanged += _ =>
        {
            _typingTimer = 1.2f;
            UpdateGlyphs();
        };
        _words.OnTextEntered += _ => Inscribe();
        _writeBox.AddChild(_words);
        var palette = new GridContainer { Columns = 13, HSeparationOverride = 1, VSeparationOverride = 1, HorizontalAlignment = HAlignment.Center };
        for (var c = 'A'; c <= 'Z'; c++)
        {
            var rune = new MagicRuneButton(c);
            rune.OnPressed += AppendRune;
            palette.AddChild(rune);
        }
        _writeBox.AddChild(palette);
        var keys = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, HorizontalAlignment = HAlignment.Center, SeparationOverride = 6 };
        var space = new Button { Text = Loc.GetString("magic-grimoire-space") };
        space.OnPressed += _ => AppendRune(' ');
        keys.AddChild(space);
        var backspace = new Button { Text = Loc.GetString("magic-grimoire-backspace") };
        backspace.OnPressed += _ =>
        {
            if (_words.Text.Length > 0)
                SetWords(_words.Text[..^1]);
        };
        keys.AddChild(backspace);
        var clear = new Button { Text = Loc.GetString("magic-grimoire-clear") };
        clear.OnPressed += _ => SetWords(string.Empty);
        keys.AddChild(clear);
        _writeBox.AddChild(keys);
        _folioBox.AddChild(_writeBox);

        var noteRow = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 6, Margin = new Thickness(0, 3, 0, 0) };
        noteRow.AddChild(new Label { Text = Loc.GetString("magic-grimoire-note"), FontColorOverride = Color.FromHex("#7a5a30"), VerticalAlignment = VAlignment.Center });
        _note = new LineEdit { PlaceHolder = Loc.GetString("magic-grimoire-note-placeholder"), HorizontalExpand = true, StyleBoxOverride = InkBox() };
        _note.OnTextChanged += _ =>
        {
            _noteTimer = 1f;
            _notePage = CurrentPage();
        };
        _note.OnTextEntered += _ => SaveNote();
        noteRow.AddChild(_note);
        _folioBox.AddChild(noteRow);

        var seals = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, HorizontalAlignment = HAlignment.Center, SeparationOverride = 5, Margin = new Thickness(0, 5, 0, 0) };
        _writeSeal = Seal(Loc.GetString("magic-grimoire-write-toggle"), Color.FromHex("#3a2a4a"));
        _writeSeal.OnPressed += ToggleWriting;
        seals.AddChild(_writeSeal);
        _inscribe = Seal(Loc.GetString("magic-grimoire-inscribe-seal"), Color.FromHex("#7a1a1a"));
        _inscribe.OnPressed += Inscribe;
        _inscribe.Visible = false;
        seals.AddChild(_inscribe);
        var cast = Seal(Loc.GetString("magic-grimoire-cast"), Color.FromHex("#7a1a1a"));
        cast.OnPressed += () => PageAction(OnCast);
        seals.AddChild(cast);
        _ready = Seal(Loc.GetString("magic-grimoire-ready"), Color.FromHex("#7a1a1a"));
        _ready.OnPressed += () => PageAction(OnToggleReady);
        seals.AddChild(_ready);
        var etch = Seal(Loc.GetString("magic-grimoire-etch"), Color.FromHex("#3a2a4a"));
        etch.OnPressed += () => PageAction(OnEtch);
        seals.AddChild(etch);
        var tear = Seal(Loc.GetString("magic-grimoire-tear"), Color.FromHex("#3e4246"));
        tear.OnPressed += () => PageAction(OnTearOut);
        seals.AddChild(tear);
        _folioBox.AddChild(seals);

        var nav = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        _back = new MagicLinkButton(Loc.GetString("magic-grimoire-back"));
        _back.OnPressed += () => Step(-1);
        nav.AddChild(_back);
        nav.AddChild(new Control { HorizontalExpand = true });
        _next = new MagicLinkButton(Loc.GetString("magic-grimoire-next"));
        _next.OnPressed += () => Step(1);
        nav.AddChild(_next);
        nav.AddChild(new Control { MinWidth = 14 }); // keep clear of the dog-ear
        rightBox.AddChild(nav);

        _closed.PlayOpen();
    }

    #region Building helpers

    public static Control RuneStrip(float height = 10f)
    {
        return new MagicGlyphControl
        {
            Text = "FUTHARK GEBO WUNJO HAGAL NAUD ISA JERA EIHWAZ PERTH",
            InkColor = Color.FromHex("#8a6a30"),
            GlyphHeight = height,
            Thickness = 1f,
            MinHeight = height + 4f,
            Bob = false,
            HorizontalExpand = true,
        };
    }

    private static Label Gothic(float size, Color color) => new()
    {
        HorizontalAlignment = HAlignment.Center,
        FontColorOverride = color,
        FontOverride = MagicFonts.Gothic(size, 1f),
    };

    // Wraps inside the page instead of pushing it wider (one long hint used to squash the other page).
    private static MagicWrapLabel Small(string text) => new()
    {
        Text = text,
        HorizontalExpand = true,
    };

    private static MagicSealButton Seal(string text, Color color) => new(text, 44f) { Fill = color };

    /// <summary>Dark "ink well" box for the text fields, with a gold edge.</summary>
    private static StyleBoxFlat InkBox() => new()
    {
        BackgroundColor = Color.FromHex("#2a1a10"),
        BorderColor = Color.FromHex("#9c7a44"),
        BorderThickness = new Thickness(1),
        ContentMarginLeftOverride = 6,
        ContentMarginRightOverride = 6,
        ContentMarginTopOverride = 2,
        ContentMarginBottomOverride = 2,
    };

    #endregion

    #region Opening and closing

    /// <summary>Any click that no page, tab or seal took drags the book around.</summary>
    protected override DragMode GetDragModeFor(Vector2 relativeMousePos) => DragMode.Move;

    private void ShowBook()
    {
        _closed.Visible = false;
        _open.Visible = true;
        _open.Modulate = new Color(1f, 1f, 1f, 0f);
        Refresh();
    }

    /// <summary>Closing plays the buckle animation backwards first.</summary>
    public override void Close()
    {
        if (_closing || !_open.Visible || Parent == null)
        {
            base.Close();
            return;
        }

        SaveNote();
        _closing = true;
        _open.Visible = false;
        _closed.Visible = true;
        _closed.PlayClose();
    }

    #endregion

    #region Chapters

    private bool HasClass(MagicClass c) => _last != null && _last.Pages.Any(p => p.Glow == c);

    private IEnumerable<int> Chapters()
    {
        yield return Readied;
        if (_last is { HasSpellIndex: true })
            yield return Spells;
        yield return Codex;
        if (_last is { KnownWords.Count: > 0 })
            yield return Words;
        yield return New;
        foreach (var c in AllClasses)
        {
            if (HasClass(c))
                yield return (int) c;
        }
    }

    /// <summary>Page indices (into the book) shown in a spell chapter.</summary>
    private List<int> Items(int chapter)
    {
        var list = new List<int>();
        if (_last == null || chapter is Codex or Words or Spells)
            return list;
        for (var i = 0; i < _last.Pages.Count; i++)
        {
            var page = _last.Pages[i];
            var fits = chapter switch
            {
                Readied => page.Readied,
                New => page.Glow == null,
                _ => page.Glow == (MagicClass) chapter,
            };
            if (fits)
                list.Add(i);
        }
        return list;
    }

    /// <summary>Codex leaves: null = Key of Runes, otherwise a class's words.</summary>
    private List<MagicClass?> Leaves()
    {
        var list = new List<MagicClass?>();
        if (_last == null)
            return list;
        if (_last.HasKey)
            list.Add(null);
        foreach (var c in _last.CodexClasses)
            list.Add(c);
        return list;
    }

    private int Count(int chapter) => chapter switch
    {
        Codex => Leaves().Count,
        Words => _last?.KnownWords.Count ?? 0,
        Spells => _last?.KnownSpells.Count ?? 0,
        _ => Items(chapter).Count,
    };

    private string ChapterName(int chapter) => chapter switch
    {
        Readied => Loc.GetString("magic-grimoire-chapter-readied"),
        Codex => Loc.GetString("magic-grimoire-chapter-codex"),
        Words => Loc.GetString("magic-grimoire-chapter-words"),
        Spells => Loc.GetString("magic-grimoire-chapter-spells"),
        New => Loc.GetString("magic-grimoire-chapter-new"),
        _ => Loc.GetString($"magic-grimoire-class-{ClassKey((MagicClass) chapter)}"),
    };

    private string TabName(int chapter) => chapter switch
    {
        Readied => Loc.GetString("magic-grimoire-tab-readied"),
        Codex => Loc.GetString("magic-grimoire-tab-codex"),
        Words => Loc.GetString("magic-grimoire-tab-words"),
        Spells => Loc.GetString("magic-grimoire-tab-spells"),
        New => Loc.GetString("magic-grimoire-tab-new"),
        _ => Loc.GetString($"magic-grimoire-tab-{ClassKey((MagicClass) chapter)}"),
    };

    private string LeafName(MagicClass? leaf) => leaf is { } c
        ? Loc.GetString("magic-grimoire-leaf-words", ("class", Loc.GetString($"magic-grimoire-class-{ClassKey(c)}")))
        : Loc.GetString("magic-grimoire-key-title");

    /// <summary>The book page index on show, or -1.</summary>
    private int CurrentPage()
    {
        if (_chapter is Codex or Words or Spells or Spells)
            return -1;
        var items = Items(_chapter);
        return _sel >= 0 && _sel < items.Count ? items[_sel] : -1;
    }

    /// <summary>Turns the page with the flip animation; the change happens under the sheet.</summary>
    private void Turn(int dir, Action change)
    {
        if (_flip.Active)
            return;
        SaveNote();
        _flip.Start(dir, () =>
        {
            change();
            _leaf++;
            Refresh();
        });
    }

    private void Step(int dir)
    {
        var count = Count(_chapter);
        var target = _sel + dir;
        if (target < 0 || target >= count)
            return;
        Turn(dir, () =>
        {
            _sel = target;
            _writing = false;
        });
    }

    private void PageAction(Action<int>? action)
    {
        var page = CurrentPage();
        if (page >= 0)
            action?.Invoke(page);
    }

    #endregion

    #region Writing

    private void ToggleWriting()
    {
        if (CurrentPage() < 0)
            return;
        _writing = !_writing;
        Refresh();
    }

    private void AppendRune(char c)
    {
        if (!_writing)
            return;
        SetWords(string.Concat(_words.Text, c.ToString()));
    }

    private void SetWords(string text)
    {
        _words.Text = text;
        _typingTimer = 1.2f;
        UpdateGlyphs();
    }

    private void Inscribe()
    {
        var page = CurrentPage();
        if (page < 0)
            return;
        _typingTimer = 0f;
        _writing = false;
        _pendingPage = page;
        OnWrite?.Invoke(page, _words.Text);
        Refresh();
    }

    private void SaveNote()
    {
        if (_notePage < 0 || _last == null || _notePage >= _last.Pages.Count)
            return;
        if (_last.Pages[_notePage].Note != _note.Text)
            OnNote?.Invoke(_notePage, _note.Text);
        _notePage = -1;
        _noteTimer = 0f;
    }

    #endregion

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _clock += args.DeltaSeconds;

        if (_open.Visible && _open.Modulate.A < 1f)
            _open.Modulate = new Color(1f, 1f, 1f, MathF.Min(1f, _open.Modulate.A + args.DeltaSeconds * 3f));

        if (_noteTimer > 0f)
        {
            _noteTimer -= args.DeltaSeconds;
            if (_noteTimer <= 0f)
                SaveNote();
        }

        if (_typingTimer <= 0f)
            return;
        _typingTimer -= args.DeltaSeconds;
        if (_typingTimer <= 0f)
            UpdateGlyphs();
    }

    public void UpdateState(MagicGrimoireBuiState state)
    {
        var prev = _last;
        _last = state;

        // First state: open on the first class that has pages, else New.
        if (!_chapterChosen)
        {
            _chapterChosen = true;
            foreach (var c in AllClasses)
            {
                if (!HasClass(c))
                    continue;
                _chapter = (int) c;
                break;
            }
        }

        // A page we just inscribed has started to glow: it files itself under its class bookmark.
        if (_pendingPage is { } pending && pending < state.Pages.Count)
        {
            _pendingPage = null;
            if (state.Pages[pending].Glow is { } cls
                && (prev == null || pending >= prev.Pages.Count || prev.Pages[pending].Glow != cls))
            {
                var known = prev != null && prev.Pages.Any(p => p.Glow == cls);
                var name = Loc.GetString($"magic-grimoire-class-{ClassKey(cls)}");
                if (!known)
                {
                    // The new bookmark slides out of the spine once the page has turned.
                    _growClass = cls;
                    _growAt = _clock + 0.3f;
                }
                _toast.Show(Loc.GetString(known ? "magic-grimoire-filed" : "magic-grimoire-new-bookmark", ("class", name)));
                if (_chapter != (int) cls)
                {
                    Refresh();
                    Turn(1, () =>
                    {
                        _chapter = (int) cls;
                        _sel = Math.Max(0, Items(_chapter).IndexOf(pending));
                    });
                    return;
                }
                _sel = Math.Max(0, Items(_chapter).IndexOf(pending));
            }
        }

        if (_open.Visible)
            Refresh();
    }

    private void Refresh()
    {
        if (_last == null)
            return;

        // A class chapter can vanish (its only page torn out): fall back to New.
        if (_chapter >= 0 && !HasClass((MagicClass) _chapter) || _chapter == Words && _last.KnownWords.Count == 0
            || _chapter == Spells && !_last.HasSpellIndex)
            _chapter = New;
        var count = Count(_chapter);
        _sel = count == 0 ? 0 : Math.Clamp(_sel, 0, count - 1);

        var header = Loc.GetString("magic-grimoire-title")
                     + "   ·   " + Loc.GetString("magic-grimoire-readied", ("count", _last.Readied), ("max", _last.ActiveLimit))
                     + "   ·   " + Loc.GetString("magic-grimoire-pages", ("count", _last.Pages.Count), ("max", _last.MaxPages));
        if (_last.MaxMana > 0)
            header += "   ·   " + Loc.GetString("magic-grimoire-mana", ("mana", (int) _last.Mana), ("max", (int) _last.MaxMana));
        _header.Text = header;

        _leftDecor.Seed = 1 + _leaf * 2;
        _rightDecor.Seed = 2 + _leaf * 2;
        _rightDecor.Ring = _leaf % 5 == 4;

        RebuildTabs();
        RebuildLeft();
        RebuildRight();
    }

    private void RebuildTabs()
    {
        _tabs.RemoveAllChildren();
        foreach (var chapter in Chapters())
        {
            var tab = new MagicBookTab(TabName(chapter), Count(chapter), TabColor(chapter), chapter == _chapter);
            if (_growClass is { } grow && chapter == (int) grow)
            {
                var since = _clock - _growAt;
                if (since < 0.9f)
                    tab.Grow = MathF.Max(0f, since);
                else
                    _growClass = null;
            }
            var target = chapter;
            tab.OnPressed += () =>
            {
                if (target == _chapter)
                    return;
                Turn(1, () =>
                {
                    _chapter = target;
                    _sel = 0;
                    _writing = false;
                });
            };
            _tabs.AddChild(tab);
        }
    }

    private void RebuildLeft()
    {
        _leftList.RemoveAllChildren();

        // While writing, the left page becomes the Key of Runes.
        _keyBox.Visible = _writing;
        _leftScroll.Visible = !_writing;
        _leftTitle.Visible = !_writing;
        _leftWake.Visible = !_writing;
        if (_writing)
        {
            var hasKey = _last!.HasKey;
            _keyTitle.Text = Loc.GetString(hasKey ? "magic-grimoire-key-title" : "magic-grimoire-no-key");
            _keyHint.Text = Loc.GetString(hasKey ? "magic-grimoire-key-touch" : "magic-grimoire-no-key-hint");
            _keyGrid.Visible = hasKey;
            _leftHint.Text = string.Empty;
            _leftHint.Visible = false;
            return;
        }

        _leftTitle.Text = ChapterName(_chapter);
        _leftTitle.FontColorOverride = _chapter >= 0 ? ClassColors((MagicClass) _chapter).Dark : DarkRed;
        _leftHint.Visible = true;

        if (_chapter == Codex)
        {
            var leaves = Leaves();
            for (var i = 0; i < leaves.Count; i++)
            {
                var color = leaves[i] is { } c ? ClassColors(c).Dark : Gold;
                AddRow(i, new MagicIndexRow(string.Concat(Numeral(i), "."), LeafName(leaves[i]), color, true, false, i == _sel));
            }
            _leftHint.Text = Loc.GetString("magic-grimoire-codex-empty");
            return;
        }

        if (_chapter == Spells)
        {
            var spells = _last!.KnownSpells;
            for (var i = 0; i < spells.Count; i++)
                AddRow(i, new MagicIndexRow(string.Concat(Numeral(i), "."), spells[i].Name, ClassColors(spells[i].Class).Dark, true, false, i == _sel));
            _leftHint.Text = Loc.GetString("magic-grimoire-spells-hint");
            return;
        }

        if (_chapter == Words)
        {
            var words = _last!.KnownWords;
            for (var i = 0; i < words.Count; i++)
                AddRow(i, new MagicIndexRow(string.Concat(Numeral(i), "."), words[i].Name, ClassColors(words[i].Class).Dark, true, false, i == _sel));
            _leftHint.Text = Loc.GetString("magic-grimoire-words-hint");
            return;
        }

        var items = Items(_chapter);
        for (var i = 0; i < items.Count; i++)
        {
            var page = _last!.Pages[items[i]];
            var written = !string.IsNullOrWhiteSpace(page.Text);
            var color = page.Glow is { } g ? ClassColors(g).Dark
                : page.Sealed is { } sg ? ClassColors(sg).Dark.WithAlpha(0.45f)
                : Color.FromHex("#b89a68");
            var text = !string.IsNullOrWhiteSpace(page.Note) ? page.Note
                : written ? "· · ·" : Loc.GetString("magic-grimoire-unwritten");
            AddRow(i, new MagicIndexRow(string.Concat(Numeral(i), "."), text, color, page.Glow != null, page.Readied, i == _sel));
        }

        _leftHint.Text = _chapter == New
            ? Loc.GetString(items.Count == 0 ? "magic-grimoire-no-blanks" : "magic-grimoire-stitch-more")
            : items.Count == 0 ? Loc.GetString("magic-grimoire-no-pages") : string.Empty;
    }

    private void AddRow(int index, MagicIndexRow row)
    {
        row.OnPressed += () =>
        {
            if (index == _sel)
                return;
            Turn(index > _sel ? 1 : -1, () =>
            {
                _sel = index;
                _writing = false;
            });
        };
        _leftList.AddChild(row);
    }

    private void RebuildRight()
    {
        var count = Count(_chapter);
        _back.Visible = _sel > 0;
        _next.Visible = _sel < count - 1;

        _folioBox.Visible = false;
        _emptyBox.Visible = false;
        _codexKey.Visible = false;
        _codexWords.Visible = false;
        _knownWord.Visible = false;
        _knownSpell.Visible = false;

        if (_chapter == Spells)
        {
            if (_last!.KnownSpells.Count == 0)
            {
                ShowEmpty(Loc.GetString("magic-grimoire-spells-hint"));
                return;
            }
            _knownSpell.SetSpell(_last.KnownSpells[_sel]);
            _knownSpell.Visible = true;
            return;
        }

        if (_chapter == Words)
        {
            _knownWord.SetWord(_last!.KnownWords.Count > 0 ? _last.KnownWords[_sel] : null);
            _knownWord.Visible = true;
            return;
        }

        if (_chapter == Codex)
        {
            var leaves = Leaves();
            if (leaves.Count == 0)
            {
                ShowEmpty(Loc.GetString("magic-grimoire-codex-empty"));
                return;
            }
            if (leaves[_sel] is { } cls)
            {
                _codexWords.SetEntries(_last!.Codex.Where(e => e.Class == cls).ToList());
                _codexWords.Visible = true;
            }
            else
                _codexKey.Visible = true;
            return;
        }

        var items = Items(_chapter);
        if (items.Count == 0)
        {
            ShowEmpty(Loc.GetString("magic-grimoire-empty-hint"));
            return;
        }

        _folioBox.Visible = true;
        var index = items[_sel];
        var page = _last!.Pages[index];
        _folio.Text = Loc.GetString("magic-grimoire-folio-of", ("number", Numeral(_sel)), ("total", items.Count));

        // Only reload the text boxes when a different page comes up (don't wipe what's being typed).
        if (index != _shownPage)
        {
            _shownPage = index;
            _words.Text = page.Text;
            _note.Text = page.Note;
            _notePage = -1;
        }
        else if (!_writing && _typingTimer <= 0f && _pendingPage == null) // keep typed runes until the server answers an Inscribe
            _words.Text = page.Text;

        _writeBox.Visible = _writing;
        _wake.Visible = !_writing;
        _inscribe.Visible = _writing;
        _writeSeal.Text = Loc.GetString(_writing ? "magic-grimoire-done" : "magic-grimoire-write-toggle");
        _ready.Text = Loc.GetString(page.Readied ? "magic-grimoire-unready" : "magic-grimoire-ready");
        UpdateGlyphs();
    }

    private void ShowEmpty(string hint)
    {
        _emptyBox.Visible = true;
        _emptyTitle.Text = Loc.GetString("magic-grimoire-empty");
        _emptyHint.Text = hint;
        _back.Visible = false;
        _next.Visible = false;
    }

    private void UpdateGlyphs()
    {
        var text = _words.Text.ToUpperInvariant();
        _glyphs.Text = text;

        var index = CurrentPage();
        if (_last == null || index < 0)
            return;
        var page = _last.Pages[index];

        if (_typingTimer > 0f || !string.Equals(text, page.Text.ToUpperInvariant(), StringComparison.Ordinal))
        {
            // Still writing (or not inscribed yet): the glyphs ripple through every colour.
            _glyphs.Mode = string.IsNullOrEmpty(text) ? MagicGlyphControl.GlyphMode.Ink : MagicGlyphControl.GlyphMode.Rainbow;
            _wake.Wake = null;
            _state.Text = string.IsNullOrEmpty(text) ? Loc.GetString("magic-grimoire-press-write") : Loc.GetString("magic-grimoire-shifting");
            return;
        }

        if (page.Glow is { } glow)
        {
            var (dark, light) = ClassColors(glow);
            _glyphs.Mode = MagicGlyphControl.GlyphMode.Glow;
            _glyphs.GlowDark = dark;
            _glyphs.GlowLight = light;
            _state.Text = Loc.GetString("magic-grimoire-awaken");
            _wake.Wake = dark;
            _wake.WakeLight = light;
        }
        else if (page.Sealed is { } sealedClass)
        {
            // Real words of a school sealed to you: a faint, half-asleep glow in that class colour.
            var (dark, light) = ClassColors(sealedClass);
            _glyphs.Mode = MagicGlyphControl.GlyphMode.Glow;
            _glyphs.GlowDark = dark.WithAlpha(0.45f);
            _glyphs.GlowLight = light.WithAlpha(0.45f);
            _state.Text = Loc.GetString("magic-grimoire-sealed");
            _wake.Wake = null;
        }
        else
        {
            _glyphs.Mode = MagicGlyphControl.GlyphMode.Ink;
            _wake.Wake = null;
            _state.Text = string.IsNullOrWhiteSpace(page.Text)
                ? Loc.GetString("magic-grimoire-press-write")
                : Loc.GetString("magic-grimoire-dark");
        }
    }
}
