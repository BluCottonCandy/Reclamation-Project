using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Timing;

namespace Content.Client._ReclamationProject.Magic;

// #Reclamation Project - drawn decorations for the grimoire window (approved mockup v21):
// thin gold frame, aged parchment pages, bookmark tabs, index rows, dividers, page-turn sheet,
// round wax seals and faint glyph text that wakes up in the class colour.

/// <summary>Fonts used by the hand-drawn grimoire parts (cached per pixel size).</summary>
public static class MagicFonts
{
    private static readonly Dictionary<(string, int), Font> Cache = new();

    public static Font Gothic(float size, float uiScale) => Get("/Fonts/Grenze_Gotisch/GrenzeGotisch.ttf", size, uiScale);
    public static Font Serif(float size, float uiScale) => Get("/Fonts/Cambria.ttf", size, uiScale);

    private static Font Get(string path, float size, float uiScale)
    {
        var px = Math.Max(6, (int) MathF.Round(size * uiScale));
        if (!Cache.TryGetValue((path, px), out var font))
        {
            var res = IoCManager.Resolve<IResourceCache>().GetResource<FontResource>(path);
            font = new VectorFont(res, px);
            Cache[(path, px)] = font;
        }
        return font;
    }

    /// <summary>Width of a string (drawn invisibly far away to measure it).</summary>
    public static float Measure(DrawingHandleScreen h, Font font, string text)
        => h.DrawString(font, new Vector2(-10000, -10000), text, Color.Transparent).X;

    // Reused buffer: the client sandbox doesn't allow stackalloc. UI drawing is single-threaded.
    private static readonly Vector2[] QuadPoints = new Vector2[4];

    /// <summary>Filled quad from four points (used for rotated or slanted shapes).</summary>
    public static void Quad(DrawingHandleScreen h, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
    {
        QuadPoints[0] = a;
        QuadPoints[1] = b;
        QuadPoints[2] = c;
        QuadPoints[3] = d;
        h.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, QuadPoints, color);
    }

    public static void Diamond(DrawingHandleScreen h, Vector2 c, float r, Color color, bool filled)
    {
        if (filled)
        {
            Quad(h, c + new Vector2(0, -r), c + new Vector2(r, 0), c + new Vector2(0, r), c + new Vector2(-r, 0), color);
            return;
        }
        h.DrawLine(c + new Vector2(0, -r), c + new Vector2(r, 0), color);
        h.DrawLine(c + new Vector2(r, 0), c + new Vector2(0, r), color);
        h.DrawLine(c + new Vector2(0, r), c + new Vector2(-r, 0), color);
        h.DrawLine(c + new Vector2(-r, 0), c + new Vector2(0, -r), color);
    }
}

/// <summary>The open tome: dark purple with one thin gold line around it.</summary>
public sealed class MagicTomeFrame : Control
{
    protected override void Draw(DrawingHandleScreen handle)
    {
        var s = UIScale;
        var w = PixelSize.X;
        var h = PixelSize.Y;
        if (w < 20f * s || h < 20f * s)
            return;

        handle.DrawRect(new UIBox2(0, 0, w, h), MagicGrimoireWindow.Cover);
        handle.DrawRect(new UIBox2(0, 0, w, h), Color.FromHex("#8a6a30"), false);
    }
}

/// <summary>
/// One parchment page: a single hairline border with small corners, an inner shadow toward the spine,
/// and random little messes (foxing, an ink blot, a smudge). A faint coffee ring only now and then.
/// </summary>
public sealed class MagicPageDecor : Control
{
    /// <summary>Changes the messes. The window bumps it on every page turn.</summary>
    public int Seed;
    public bool Ring;
    /// <summary>Right-hand page: shadow on its left edge and a dog-ear curl in the bottom corner.</summary>
    public bool RightSide;

    protected override void Draw(DrawingHandleScreen handle)
    {
        var s = UIScale;
        var w = PixelSize.X;
        var h = PixelSize.Y;
        if (w <= 0 || h <= 0)
            return;
        handle.DrawRect(new UIBox2(0, 0, w, h), MagicGrimoireWindow.Parchment);
        // Still being laid out: parchment only (inner boxes would be inside-out).
        if (w < 60f * s || h < 60f * s)
            return;

        // Shadow toward the spine, and a soft darker rim.
        for (var i = 0; i < 12; i++)
        {
            var a = 0.16f * (1f - i / 12f);
            var x = RightSide ? i * s : w - (i + 1) * s;
            handle.DrawRect(new UIBox2(x, 0, x + 1f * s, h), new Color(0.35f, 0.23f, 0.1f, a));
        }
        for (var i = 0; i < 6; i++)
        {
            var a = 0.06f * (1f - i / 6f);
            handle.DrawRect(new UIBox2(i * s, i * s, w - i * s, h - i * s), new Color(0.54f, 0.42f, 0.23f, a), false);
        }

        // Messes, different on every page turn.
        var rng = new System.Random(Seed * 7919 + (RightSide ? 104729 : 0));
        for (var i = 0; i < 6; i++)
        {
            var r = (1f + (float) rng.NextDouble() * 1.8f) * s;
            var p = new Vector2((0.08f + (float) rng.NextDouble() * 0.84f) * w, (0.06f + (float) rng.NextDouble() * 0.86f) * h);
            handle.DrawCircle(p, r, new Color(0.6f, 0.45f, 0.25f, 0.16f + i % 3 * 0.04f));
        }
        if (rng.NextDouble() < 0.5)
        {
            var p = new Vector2((0.15f + (float) rng.NextDouble() * 0.65f) * w, (0.45f + (float) rng.NextDouble() * 0.4f) * h);
            var ink = new Color(0.16f, 0.1f, 0.06f, 0.42f);
            handle.DrawCircle(p, 4.5f * s, ink);
            handle.DrawCircle(p + new Vector2(3, -2) * s, 3f * s, ink);
            handle.DrawCircle(p + new Vector2(-2, 2) * s, 2.5f * s, ink);
            handle.DrawCircle(p + new Vector2(11, -6) * s, 1.3f * s, ink);
            handle.DrawCircle(p + new Vector2(-9, 5) * s, 1f * s, ink);
            handle.DrawCircle(p + new Vector2(12, 5) * s, 0.8f * s, ink);
        }
        if (rng.NextDouble() < 0.6)
        {
            var p = new Vector2((0.1f + (float) rng.NextDouble() * 0.6f) * w, (0.1f + (float) rng.NextDouble() * 0.6f) * h);
            var dir = new Vector2(1f, ((float) rng.NextDouble() - 0.5f) * 0.6f);
            var len = (40f + (float) rng.NextDouble() * 30f) * s;
            for (var t = 0f; t <= 1f; t += 0.1f)
                handle.DrawCircle(p + dir * len * t, 6f * s * (1f - MathF.Abs(t - 0.5f)), new Color(0.42f, 0.29f, 0.14f, 0.035f));
        }
        if (Ring)
        {
            var c = new Vector2(w - 54f * s, h - 92f * s);
            for (var i = 0; i < 3; i++)
                handle.DrawCircle(c, (30f + i) * s, new Color(0.66f, 0.5f, 0.29f, 0.12f), false);
            handle.DrawCircle(c, 33f * s, new Color(0.66f, 0.5f, 0.29f, 0.06f), false);
        }

        // One hairline border and small corners.
        var rule = new Color(0.72f, 0.6f, 0.41f, 0.7f);
        handle.DrawRect(new UIBox2(5 * s, 5 * s, w - 5 * s, h - 5 * s), rule, false);
        Corner(handle, new Vector2(5 * s, 5 * s), 1, 1);
        Corner(handle, new Vector2(w - 5 * s, 5 * s), -1, 1);
        Corner(handle, new Vector2(5 * s, h - 5 * s), 1, -1);
        Corner(handle, new Vector2(w - 5 * s, h - 5 * s), -1, -1);

        if (RightSide)
        {
            // Dog-ear curl.
            var k = 18f * s;
            MagicFonts.Quad(handle, new Vector2(w, h - k), new Vector2(w, h), new Vector2(w - k, h), new Vector2(w - k, h), Color.FromHex("#cbb68a"));
            handle.DrawLine(new Vector2(w, h - k), new Vector2(w - k, h), Color.FromHex("#a88c5c"));
        }

        void Corner(DrawingHandleScreen hd, Vector2 o, float sx, float sy)
        {
            var k = new Vector2(sx, sy) * s;
            Vector2 P(float x, float y) => o + new Vector2(x, y) * k;
            var line = Color.FromHex("#a8864e");
            hd.DrawLine(P(0, 0), P(18, 0), line);
            hd.DrawLine(P(0, 0), P(0, 18), line);
            var red = Color.FromHex("#7a1f1f");
            for (var t = 0; t < 5; t++)
            {
                var a = t / 5f;
                var b = (t + 1) / 5f;
                hd.DrawLine(P(10 * a, 6 * a * a + 1.5f * a), P(10 * b, 6 * b * b + 1.5f * b), red);
                hd.DrawLine(P(6 * a * a + 1.5f * a, 10 * a), P(6 * b * b + 1.5f * b, 10 * b), red);
            }
            hd.DrawCircle(o, 1.8f * s, MagicGrimoireWindow.Gold);
        }
    }
}

/// <summary>A thin divider: line, small red diamond, line.</summary>
public sealed class MagicDivider : Control
{
    public MagicDivider()
    {
        MinHeight = 10;
        HorizontalExpand = true;
    }

    protected override void Draw(DrawingHandleScreen h)
    {
        var s = UIScale;
        var w = PixelSize.X;
        if (w < 30 * s)
            return;
        var y = PixelSize.Y / 2f;
        var mid = w / 2f;
        var gold = Color.FromHex("#c4a66a");
        h.DrawLine(new Vector2(w * 0.2f, y), new Vector2(mid - 6 * s, y), gold);
        h.DrawLine(new Vector2(mid + 6 * s, y), new Vector2(w * 0.8f, y), gold);
        MagicFonts.Diamond(h, new Vector2(mid, y), 2.5f * s, Color.FromHex("#7a1f1f"), true);
    }
}

/// <summary>A bookmark tab sticking out of the right side of the book.</summary>
public sealed class MagicBookTab : Control
{
    public event Action? OnPressed;
    private readonly string _label;
    private readonly string _count;
    private readonly Color _color;
    private readonly bool _selected;
    /// <summary>Seconds since this tab first grew in (negative = not growing).</summary>
    public float Grow = -1f;

    public MagicBookTab(string label, int count, Color color, bool selected)
    {
        _label = label;
        _count = count > 0 ? count.ToString() : string.Empty;
        _color = color;
        _selected = selected;
        MouseFilter = MouseFilterMode.Stop;
        MinSize = new Vector2(46, 26);
        MaxSize = new Vector2(46, 26);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (Grow >= 0f)
            Grow = Grow > 1f ? -1f : Grow + args.DeltaSeconds;
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if (args.Function != EngineKeyFunctions.UIClick)
            return;
        OnPressed?.Invoke();
        args.Handle();
    }

    protected override void Draw(DrawingHandleScreen h)
    {
        var s = UIScale;
        var w = PixelSize.X;
        var hgt = PixelSize.Y;
        if (w < 10 * s || hgt < 10 * s)
            return;

        var hovered = UserInterfaceManager.CurrentlyHovered == this;
        // Selected tabs tuck in toward the book, others stick out; a growing tab slides out of the spine.
        var x0 = _selected ? 0f : 4f * s;
        if (hovered && !_selected)
            x0 += 2f * s;
        var color = _color;
        if (Grow >= 0f)
        {
            var t = MathF.Min(1f, Grow / 0.9f);
            x0 -= (1f - t) * 40f * s;
            color = Color.InterpolateBetween(Color.White, _color, t);
        }
        var x1 = MathF.Min(w, x0 + 42f * s);
        if (x1 <= MathF.Max(0f, x0) + 4f * s)
            return;
        var box = new UIBox2(MathF.Max(0f, x0), 1f * s, x1, hgt - 1f * s);
        h.DrawRect(box, color);
        h.DrawRect(new UIBox2(box.Left, box.Top, box.Left + 3f * s, box.Bottom), new Color(0f, 0f, 0f, 0.18f));
        if (_selected)
            h.DrawRect(new UIBox2(box.Left, box.Top, box.Left + 2f * s, box.Bottom), Color.FromHex("#2a1808"));

        var font = MagicFonts.Gothic(11, s);
        h.DrawString(font, new Vector2(box.Left + 6f * s, box.Top + 4f * s), _label, Color.FromHex("#2a1808"));
        if (_count.Length > 0)
        {
            var small = MagicFonts.Serif(8, s);
            var cw = MagicFonts.Measure(h, small, _count);
            h.DrawString(small, new Vector2(box.Right - cw - 3f * s, box.Top + 7f * s), _count, new Color(0.16f, 0.09f, 0.03f, 0.75f));
        }
    }
}

/// <summary>One line of the chapter list: class diamond, roman numeral, the note, a ribbon if readied.</summary>
public sealed class MagicIndexRow : Control
{
    public event Action? OnPressed;
    private readonly string _number;
    private readonly string _text;
    private readonly Color _color;
    private readonly bool _filled;
    private readonly bool _readied;
    private readonly bool _selected;

    public MagicIndexRow(string number, string text, Color color, bool filled, bool readied, bool selected)
    {
        _number = number;
        _text = text;
        _color = color;
        _filled = filled;
        _readied = readied;
        _selected = selected;
        MouseFilter = MouseFilterMode.Stop;
        MinHeight = 22;
        HorizontalExpand = true;
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if (args.Function != EngineKeyFunctions.UIClick)
            return;
        OnPressed?.Invoke();
        args.Handle();
    }

    protected override void Draw(DrawingHandleScreen h)
    {
        var s = UIScale;
        var w = PixelSize.X;
        var hgt = PixelSize.Y;
        if (w < 40 * s || hgt < 8 * s)
            return;
        if (_selected || UserInterfaceManager.CurrentlyHovered == this)
            h.DrawRect(new UIBox2(0, 0, w, hgt), new Color(0.88f, 0.82f, 0.65f, _selected ? 0.7f : 0.35f));

        var mid = hgt / 2f;
        MagicFonts.Diamond(h, new Vector2(8 * s, mid), 4f * s, _color, _filled);
        var serif = MagicFonts.Serif(12, s);
        h.DrawString(serif, new Vector2(18 * s, mid - 8 * s), _number, Color.FromHex("#7a1f1f"));
        h.DrawString(serif, new Vector2(48 * s, mid - 8 * s), _text, MagicGrimoireWindow.Ink);
        if (_readied)
        {
            var x = w - 10 * s;
            MagicFonts.Quad(h, new Vector2(x, mid - 7 * s), new Vector2(x + 6 * s, mid - 7 * s), new Vector2(x + 6 * s, mid + 7 * s), new Vector2(x, mid + 7 * s), Color.FromHex("#a82020"));
        }
    }
}

/// <summary>A rune with its letter beside it, on the Key of Runes page. Clicking writes it.</summary>
public sealed class MagicKeyRune : Control
{
    public event Action<char>? OnPressed;
    private readonly char _letter;

    public MagicKeyRune(char letter)
    {
        _letter = letter;
        MouseFilter = MouseFilterMode.Stop;
        MinSize = new Vector2(50, 30);
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if (args.Function != EngineKeyFunctions.UIClick)
            return;
        OnPressed?.Invoke(_letter);
        args.Handle();
    }

    protected override void Draw(DrawingHandleScreen h)
    {
        var s = UIScale;
        if (PixelSize.X < 20 * s || PixelSize.Y < 10 * s)
            return;
        if (UserInterfaceManager.CurrentlyHovered == this)
            h.DrawRect(new UIBox2(0, 0, PixelSize.X, PixelSize.Y), Color.FromHex("#e0d0a6"));
        for (var x = 2 * s; x < PixelSize.X - 2 * s; x += 4 * s)
            h.DrawLine(new Vector2(x, PixelSize.Y - 1 * s), new Vector2(x + 1.5f * s, PixelSize.Y - 1 * s), Color.FromHex("#d2bd90"));

        // Big, dark runes so the shapes are easy to match.
        var runeH = 24f * s;
        var o = new Vector2(3 * s, (PixelSize.Y - runeH) / 2f);
        MagicGlyphControl.DrawRune(h, _letter, o, runeH, Color.FromHex("#1e1208"));
        h.DrawString(MagicFonts.Serif(13, s), new Vector2(3 * s + runeH * 12f / 18f + 7 * s, PixelSize.Y / 2f - 9 * s), _letter.ToString(), Color.FromHex("#7a1f1f"));
    }
}

/// <summary>Small gold ring with an X: closes the book (top-right corner).</summary>
public sealed class MagicCloseMark : Control
{
    public event Action? OnPressed;

    public MagicCloseMark()
    {
        MouseFilter = MouseFilterMode.Stop;
        MinSize = new Vector2(18, 18);
        MaxSize = new Vector2(18, 18);
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if (args.Function != EngineKeyFunctions.UIClick)
            return;
        OnPressed?.Invoke();
        args.Handle();
    }

    protected override void Draw(DrawingHandleScreen h)
    {
        var s = UIScale;
        var c = PixelSize / 2f;
        var r = MathF.Min(c.X, c.Y) - 1f * s;
        if (r < 4f * s)
            return;
        var hovered = UserInterfaceManager.CurrentlyHovered == this;
        h.DrawCircle(c, r, Color.FromHex("#2a1838"));
        h.DrawCircle(c, r, MagicGrimoireWindow.Gold, false);
        var x = r * 0.45f;
        var ink = hovered ? Color.FromHex("#f0c97a") : Color.FromHex("#c9a85a");
        for (var t = -0.6f; t <= 0.6f; t += 0.6f)
        {
            var o = new Vector2(t, 0) * s;
            h.DrawLine(c + new Vector2(-x, -x) + o, c + new Vector2(x, x) + o, ink);
            h.DrawLine(c + new Vector2(x, -x) + o, c + new Vector2(-x, x) + o, ink);
        }
    }
}

/// <summary>Small red text link ("‹ back" / "next ›").</summary>
public sealed class MagicLinkButton : Control
{
    public event Action? OnPressed;
    private readonly string _text;

    public MagicLinkButton(string text)
    {
        _text = text;
        MouseFilter = MouseFilterMode.Stop;
        MinSize = new Vector2(52, 18);
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if (args.Function != EngineKeyFunctions.UIClick)
            return;
        OnPressed?.Invoke();
        args.Handle();
    }

    protected override void Draw(DrawingHandleScreen h)
    {
        var s = UIScale;
        if (PixelSize.X < 10 * s || PixelSize.Y < 8 * s)
            return;
        var font = MagicFonts.Gothic(12, s);
        var color = UserInterfaceManager.CurrentlyHovered == this ? Color.FromHex("#b02a2a") : Color.FromHex("#7a1f1f");
        var tw = MagicFonts.Measure(h, font, _text);
        h.DrawString(font, new Vector2((PixelSize.X - tw) / 2f, 0), _text, color);
    }
}

/// <summary>
/// The page-turn sheet. Drawn over both pages: it folds from one page to the spine and opens onto
/// the other. The change it was started with happens at the halfway point, under the sheet.
/// </summary>
public sealed class MagicPageFlip : Control
{
    private const float Duration = 0.55f;
    private float _t = -1f;
    private int _dir;
    private Action? _midpoint;

    public bool Active => _t >= 0f;

    public MagicPageFlip()
    {
        MouseFilter = MouseFilterMode.Ignore;
    }

    public void Start(int dir, Action midpoint)
    {
        _dir = dir >= 0 ? 1 : -1;
        _midpoint = midpoint;
        _t = 0f;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (_t < 0f)
            return;
        var before = _t;
        _t += args.DeltaSeconds / Duration;
        if (before < 0.5f && _t >= 0.5f)
        {
            var mid = _midpoint;
            _midpoint = null;
            // Rebuilding pages mid-frame-update can clash with the UI walking its children: run it right after.
            if (mid != null)
                UserInterfaceManager.DeferAction(mid);
        }
        if (_t >= 1f)
            _t = -1f;
    }

    protected override void Draw(DrawingHandleScreen h)
    {
        if (_t < 0f)
            return;
        var s = UIScale;
        var w = PixelSize.X;
        var hgt = PixelSize.Y;
        if (w < 40 * s || hgt < 20 * s)
            return;

        var spine = w / 2f;
        var half = w / 2f;
        var c = MathF.Cos(MathF.PI * MathF.Min(_t, 1f));
        // Forward: starts over the right page and lands on the left. Back: the other way round.
        float x0, x1;
        if (c >= 0f)
        {
            var reach = half * c;
            (x0, x1) = _dir > 0 ? (spine, spine + reach) : (spine - reach, spine);
        }
        else
        {
            var reach = half * -c;
            (x0, x1) = _dir > 0 ? (spine - reach, spine) : (spine, spine + reach);
        }
        if (x1 - x0 < 1f)
            return;

        h.DrawRect(new UIBox2(x0, 0, x1, hgt), Color.FromHex("#e6d8b6"));
        // Shade: darker the more edge-on the sheet is.
        var shade = 0.28f * (1f - MathF.Abs(c));
        h.DrawRect(new UIBox2(x0, 0, x1, hgt), new Color(0.2f, 0.12f, 0.05f, shade));
        h.DrawRect(new UIBox2(x0, 0, x1, hgt), Color.FromHex("#b89a68"), false);
    }
}

/// <summary>A short message in a small dark box over the book, fading out after a few seconds.</summary>
public sealed class MagicToast : PanelContainer
{
    private readonly Label _label;
    private float _left;

    public MagicToast()
    {
        Visible = false;
        MouseFilter = MouseFilterMode.Ignore;
        HorizontalAlignment = HAlignment.Center;
        VerticalAlignment = VAlignment.Center;
        PanelOverride = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex("#2a1838"),
            BorderColor = Color.FromHex("#b08a3e"),
            BorderThickness = new Thickness(1),
            ContentMarginLeftOverride = 12,
            ContentMarginRightOverride = 12,
            ContentMarginTopOverride = 5,
            ContentMarginBottomOverride = 5,
        };
        _label = new Label { FontColorOverride = Color.FromHex("#e6cf92") };
        AddChild(_label);
    }

    public void Show(string text)
    {
        _label.Text = text;
        _left = 2.6f;
        Visible = true;
        Modulate = Color.White;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (!Visible)
            return;
        _left -= args.DeltaSeconds;
        if (_left <= 0f)
        {
            Visible = false;
            return;
        }
        Modulate = new Color(1f, 1f, 1f, MathF.Min(1f, _left / 0.4f));
    }
}

/// <summary>A round wax-seal button: dark red disc, gold rim, light text. Brightens on hover.</summary>
public sealed class MagicSealButton : Control
{
    public event Action? OnPressed;
    private readonly Label _label;
    public Color Fill = Color.FromHex("#7a1a1a");

    public string Text
    {
        get => _label.Text ?? string.Empty;
        set => _label.Text = value;
    }

    public MagicSealButton(string text, float diameter = 46f)
    {
        MouseFilter = MouseFilterMode.Stop;
        MinSize = new Vector2(diameter, diameter);
        MaxSize = new Vector2(diameter, diameter);
        _label = new Label
        {
            Text = text,
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Center,
            FontColorOverride = Color.FromHex("#fbeccb"),
        };
        AddChild(_label);
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if (args.Function != EngineKeyFunctions.UIClick)
            return;
        OnPressed?.Invoke();
        args.Handle();
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        var s = UIScale;
        var c = PixelSize / 2f;
        var r = MathF.Min(c.X, c.Y) - 1f * s;
        if (r < 5f * s)
            return;
        var fill = UserInterfaceManager.CurrentlyHovered == this ? Color.InterpolateBetween(Fill, Color.White, 0.15f) : Fill;
        handle.DrawCircle(c, r, fill);
        handle.DrawCircle(c, r - 2.5f * s, Color.FromHex("#f0c97a"), false);
        handle.DrawCircle(c, r - 3f * s, fill);
    }
}

/// <summary>
/// Faint lines of glyph text that fill the page (so it's never blank). When the open page's words work,
/// about one glyph in eight lights up and twinkles in the class colour.
/// </summary>
public sealed class MagicGlyphParagraph : Control
{
    private readonly string _text;
    private float _time;
    public Color? Wake;
    public Color WakeLight = Color.White;
    public float GlyphHeight = 10f;

    public MagicGlyphParagraph(int seed, int words)
    {
        var rng = new System.Random(seed);
        var sb = new System.Text.StringBuilder();
        for (var w = 0; w < words; w++)
        {
            var n = rng.Next(2, 7);
            for (var k = 0; k < n; k++)
                sb.Append((char) ('A' + rng.Next(26)));
            sb.Append(' ');
        }
        _text = sb.ToString();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _time += args.DeltaSeconds;
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        return new Vector2(0, MinHeight);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        var s = UIScale;
        var scale = GlyphHeight / 18f * s;
        var gw = 12f * scale;
        var lineH = GlyphHeight * 1.35f * s;
        var ink = Color.FromHex("#ab9570");
        var x = 0f;
        var y = 0f;
        var index = 0;

        foreach (var word in _text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var width = word.Length * (gw + 1f * s);
            if (x + width > PixelSize.X)
            {
                x = 0f;
                y += lineH;
            }
            if (y + GlyphHeight * s > PixelSize.Y)
                break;

            foreach (var c in word)
            {
                var color = ink;
                if (Wake is { } wake && (index * 7919) % 8 == 0)
                    color = Color.InterpolateBetween(wake, WakeLight, (MathF.Sin(_time * 2.4f + index) + 1f) / 2f);

                var o = new Vector2(x, y);
                MagicGlyphControl.DrawRune(handle, c, o, 18f * scale, color);
                x += gw + 1f * s;
                index++;
            }
            x += 6f * s;
        }
    }
}

/// <summary>Small centred serif text that wraps onto more lines instead of stretching the page wider.</summary>
public sealed class MagicWrapLabel : Control
{
    private string _text = string.Empty;
    private readonly List<string> _lines = new();
    private float _wrappedFor = -1f;

    public Color FontColor = Color.FromHex("#6b4a24");
    public float FontSize = 11f;

    public string Text
    {
        get => _text;
        set
        {
            if (_text == value)
                return;
            _text = value ?? string.Empty;
            _wrappedFor = -1f;
            InvalidateMeasure();
        }
    }

    private Font CurrentFont => MagicFonts.Serif(FontSize, UIScale);

    private static float Width(Font font, string text)
    {
        var w = 0f;
        foreach (var rune in text.EnumerateRunes())
        {
            if (font.TryGetCharMetrics(rune, 1f, out var m))
                w += m.Advance;
        }
        return w;
    }

    private void Wrap(float maxPx)
    {
        _lines.Clear();
        _wrappedFor = maxPx;
        if (_text.Length == 0)
            return;

        var font = CurrentFont;
        var line = string.Empty;
        foreach (var word in _text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var attempt = line.Length == 0 ? word : string.Concat(line, " ", word);
            if (line.Length > 0 && Width(font, attempt) > maxPx)
            {
                _lines.Add(line);
                line = word;
            }
            else
            {
                line = attempt;
            }
        }
        if (line.Length > 0)
            _lines.Add(line);
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        // Wrap to whatever width the page gives; never ask for more width than that.
        var maxPx = float.IsFinite(availableSize.X) ? availableSize.X * UIScale : 100000f;
        Wrap(Math.Max(20f, maxPx));
        var lineH = CurrentFont.GetLineHeight(1f) / UIScale;
        return new Vector2(0, _lines.Count * lineH);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        if (Math.Abs(_wrappedFor - PixelSize.X) > 0.5f)
            Wrap(Math.Max(20f, PixelSize.X));

        var font = CurrentFont;
        var lineH = font.GetLineHeight(1f);
        var y = 0f;
        foreach (var line in _lines)
        {
            var w = Width(font, line);
            handle.DrawString(font, new Vector2(MathF.Max(0f, (PixelSize.X - w) / 2f), y), line, FontColor);
            y += lineH;
        }
    }
}
