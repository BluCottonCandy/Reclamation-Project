using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Input;
using Robust.Shared.Timing;

namespace Content.Client._ReclamationProject.Magic;

/// <summary>
/// #Reclamation Project - the closed grimoire, shown when the window opens (approved mockup v21).
/// A leather strap from the back cover wraps the fore-edge and goes through a buckle at the right edge
/// of the front cover. Opening: the prong slides left out of the hole, the strap pulls back out and swings
/// down to hang off the back cover, the cover swings open, three pages fan over, then the book fades in.
/// Closing plays the same steps backwards. Click to skip.
/// </summary>
public sealed class MagicClosedBook : Control
{
    // Timeline in seconds (opening). Closing runs it backwards.
    private const float ProngEnd = 0.3f;
    private const float SlideStart = 0.32f, SlideEnd = 0.62f, SwingEnd = 1.0f;
    private const float CoverStart = 1.05f, CoverEnd = 1.95f;
    private const float PagesStart = 1.45f;
    private const float FadeStart = 2.1f, End = 2.4f;

    // The cover is drawn on a 260 x 350 grid (the mockup's size) and scaled to fit.
    private const float BookW = 260f, BookH = 350f;

    public event Action? OnOpened;
    public event Action? OnClosed;

    private float _time;
    private float _clock;
    private int _dir; // 1 opening, -1 closing, 0 still

    public MagicClosedBook()
    {
        MouseFilter = MouseFilterMode.Stop;
        HorizontalExpand = true;
        VerticalExpand = true;
    }

    public void PlayOpen()
    {
        _time = 0f;
        _dir = 1;
    }

    public void PlayClose()
    {
        _time = FadeStart;
        _dir = -1;
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if (args.Function != EngineKeyFunctions.UIClick || _dir == 0)
            return;
        // Skip to the end.
        _time = _dir > 0 ? End : 0f;
        args.Handle();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _clock += args.DeltaSeconds;
        if (_dir == 0)
            return;

        _time += args.DeltaSeconds * _dir;
        if (_dir > 0 && _time >= End)
        {
            _time = End;
            _dir = 0;
            OnOpened?.Invoke();
        }
        else if (_dir < 0 && _time <= 0f)
        {
            _time = 0f;
            _dir = 0;
            OnClosed?.Invoke();
        }
    }

    private static float Phase(float t, float start, float end) => Math.Clamp((t - start) / (end - start), 0f, 1f);
    private static float Ease(float t) => t * t * (3f - 2f * t);

    protected override void Draw(DrawingHandleScreen h)
    {
        var s = UIScale;
        var w = PixelSize.X;
        var hgt = PixelSize.Y;
        if (w < 120 * s || hgt < 120 * s)
            return;

        var t = _time;
        var prong = Ease(Phase(t, 0f, ProngEnd));
        var slide = Ease(Phase(t, SlideStart, SlideEnd));
        var swing = Ease(Phase(t, SlideEnd, SwingEnd));
        var cover = Ease(Phase(t, CoverStart, CoverEnd));
        var fade = Phase(t, FadeStart, End);
        h.Modulate = new Color(1f, 1f, 1f, 1f - fade);

        // Fit the book, and slide it right as it opens so the spine ends up in the middle.
        var u = MathF.Min(hgt * 0.88f / BookH, w * 0.45f / BookW);
        var bw = BookW * u;
        var bh = BookH * u;
        var x0 = (w - bw) / 2f + bw / 2f * cover;
        var y0 = (hgt - bh) / 2f;
        Vector2 L(float x, float y) => new(x0 + x * u, y0 + y * u);

        // Spine and the page block.
        h.DrawRect(new UIBox2(L(-12, 0), L(2, BookH)), Color.FromHex("#2a1620"));
        h.DrawRect(new UIBox2(L(6, 6), L(BookW, BookH - 6)), Color.FromHex("#e8dbbb"));
        for (var x = 9f; x < BookW; x += 3f)
            h.DrawLine(L(x, 6), L(x, BookH - 6), Color.FromHex("#d9c9a3"));

        // The cover turns around the spine (its width shrinks to nothing, then it opens out on the left).
        var turn = MathF.Cos(MathF.PI * cover * 178f / 180f);
        if (turn > 0.01f)
            DrawFront(h, L, u, turn, s, 1f - Math.Clamp(cover * 3f, 0f, 1f));
        else if (turn < -0.01f)
        {
            var back = new UIBox2(x0 + bw * turn, y0, x0, y0 + bh);
            h.DrawRect(back, Color.FromHex("#2a1620"));
            if (back.Width > 40 * u)
                h.DrawRect(new UIBox2(back.Left + 16 * u, back.Top + 16 * u, back.Right - 16 * u, back.Bottom - 16 * u), Color.FromHex("#6b4a2a"), false);
        }

        // Three pages fan over after the cover.
        for (var i = 0; i < 3; i++)
        {
            var start = PagesStart + i * 0.14f;
            var p = Phase(t, start, start + 0.55f - i * 0.08f);
            if (p <= 0f || p >= 1f)
                continue;
            var pw = (BookW - 4f) * u * MathF.Cos(MathF.PI * p * 176f / 180f);
            var box = pw >= 0 ? new UIBox2(x0, y0 + 4 * u, x0 + pw, y0 + bh - 4 * u) : new UIBox2(x0 + pw, y0 + 4 * u, x0, y0 + bh - 4 * u);
            if (box.Width < 1f)
                continue;
            h.DrawRect(box, pw >= 0 ? Color.FromHex("#e9dcbc") : Color.FromHex("#dccfae"));
            h.DrawRect(box, new Color(0f, 0f, 0f, 0.12f * (1f - MathF.Abs(pw) / bw)));
        }

        DrawStrap(h, L, u, slide, swing);

        if (turn > 0.01f)
            DrawBuckle(h, L, u, turn, prong, 1f - Math.Clamp(cover * 3f, 0f, 1f));

        h.Modulate = Color.White;
    }

    private static Texture? _coverTexture;
    private static bool _coverLoaded;

    private static Texture? CoverTexture()
    {
        if (_coverLoaded)
            return _coverTexture;
        _coverLoaded = true;
        var rsi = IoCManager.Resolve<IResourceCache>().GetResource<RSIResource>("/Textures/_ReclamationProject/Magic/cover.rsi").RSI;
        if (rsi.TryGetState("cover", out var state))
            _coverTexture = state.Frame0;
        return _coverTexture;
    }

    // The cover art is 512 x 660; these map its pixels onto the 260 x 350 book.
    private const float ArtW = 512f, ArtH = 660f;
    private const string RingText = "KAPTORAGLINSEMUROFLATIRNEDOKASUMARTILVOGENTHARISMOK";
    private const string InnerText = "VLAEUTRKOIGSNM";
    private static readonly Color CoverInk = Color.FromHex("#b09460"); // faded gold ink on the black leather
    private static readonly Color Crimson = Color.FromHex("#780a16");
    private static readonly Color CoverGold = Color.FromHex("#c49640");

    /// <summary>
    /// The front cover (approved draft v2): old black leather with blood-red brush runes and a seven-point star in faded gold
    /// (baked into cover.rsi). Drawn live on top: the ring of runes turning slowly, the runes orbiting the
    /// centre (never leaving the circle) and the crimson, gold-rimmed centre rune with an ember glow.
    /// </summary>
    private void DrawFront(DrawingHandleScreen h, Func<float, float, Vector2> l, float u, float turn, float s, float alpha)
    {
        // Horizontal squash around the spine (x = 0).
        Vector2 P(float x, float y) => l(x * turn, y);

        if (CoverTexture() is { } tex)
            h.DrawTextureRect(tex, new UIBox2(P(0, 0), P(BookW, BookH)));
        else
            h.DrawRect(new UIBox2(P(0, 0), P(BookW, BookH)), Color.FromHex("#b2a07c"));
        for (var i = 0; i < 4; i++)
            h.DrawRect(new UIBox2(P(i, i), P(BookW - i, BookH - i)), new Color(0.18f, 0.1f, 0.06f, 0.35f - i * 0.08f), false);

        // The moving parts only while the cover faces you (they'd smear while it turns).
        if (turn < 0.85f || alpha <= 0.05f)
            return;

        var sx = BookW / ArtW * turn * u;
        var sy = BookH / ArtH * u;
        var k = (sx + sy) / 2f;
        var c = P(256f * BookW / ArtW, 300f * BookH / ArtH);
        Vector2 At(float a, float r) => c + new Vector2(MathF.Cos(a) * r * sx, MathF.Sin(a) * r * sy);
        var ink = CoverInk.WithAlpha(0.92f * alpha);

        // Ring of runes between the two black circles, turning slowly one way.
        for (var i = 0; i < RingText.Length; i++)
        {
            var a = (i - _clock * 0.35f) / RingText.Length * MathF.Tau;
            MagicGlyphControl.DrawRuneRotated(h, RingText[i], At(a, 131f), 15f * k, ink, a + MathF.PI / 2f);
        }

        // Runes orbiting the centre in two rings, the inner ring the other way. Always inside the circle.
        for (var i = 0; i < InnerText.Length; i++)
        {
            var slot = i / 2;
            float a, r;
            if (i % 2 == 1)
            {
                a = (slot - _clock * 0.5f) / 7f * MathF.Tau + 0.45f;
                r = 104f * 0.58f;
            }
            else
            {
                a = (slot + _clock * 0.25f) / 7f * MathF.Tau;
                r = 104f * 0.84f;
            }
            MagicGlyphControl.DrawRuneRotated(h, InnerText[i], At(a, r), 18f * k, ink, 0f);
        }

        // Centre: an ember glow, then the crimson rune with a gold rim.
        var pulse = (MathF.Sin(_clock * 1.3f) + 1f) / 2f;
        for (var g = 0; g < 6; g++)
            h.DrawCircle(c, (58f - g * 7f + pulse * 6f) * k, new Color(0.82f, 0.24f, 0.12f, (0.05f + pulse * 0.04f) * alpha));
        var size = 150f * k;
        var rim = 3f * k;
        var gold = CoverGold.WithAlpha(alpha);
        for (var dx = -1; dx <= 1; dx++)
        for (var dy = -1; dy <= 1; dy++)
        {
            if (dx != 0 || dy != 0)
                MagicGlyphControl.DrawRuneRotated(h, 'O', c + new Vector2(dx * rim, dy * rim), size, gold, 0f);
        }
        MagicGlyphControl.DrawRuneRotated(h, 'O', c, size, Crimson.WithAlpha(alpha), 0f);
    }

    private static void DrawRing(DrawingHandleScreen h, Func<float, float, Vector2> p, Vector2 c, float r, Color color, bool dashed)
    {
        const int steps = 48;
        for (var i = 0; i < steps; i++)
        {
            if (dashed && i % 2 == 1)
                continue;
            var a0 = i / (float) steps * MathF.Tau;
            var a1 = (i + 1) / (float) steps * MathF.Tau;
            h.DrawLine(p(c.X + MathF.Cos(a0) * r, c.Y + MathF.Sin(a0) * r), p(c.X + MathF.Cos(a1) * r, c.Y + MathF.Sin(a1) * r), color);
        }
    }

    /// <summary>
    /// The strap belongs to the back cover. It slides right (out of the buckle), then swings down
    /// around the fore-edge, where it keeps hanging while the book is open.
    /// </summary>
    private static void DrawStrap(DrawingHandleScreen h, Func<float, float, Vector2> l, float u, float slide, float swing)
    {
        var dx = 22f * slide;
        var angle = swing * 82f * MathF.PI / 180f;
        var pivot = new Vector2(260, 175);
        var cos = MathF.Cos(angle);
        var sin = MathF.Sin(angle);
        Vector2 T(float x, float y)
        {
            var d = new Vector2(x, y) - pivot;
            var r = new Vector2(d.X * cos - d.Y * sin, d.X * sin + d.Y * cos) + pivot;
            return l(r.X + dx, r.Y);
        }

        // The bit wrapping round the fore-edge (darker), then the strap itself.
        MagicFonts.Quad(h, T(258, 163), T(272, 165), T(272, 185), T(258, 187), Color.FromHex("#3a2414"));
        MagicFonts.Quad(h, T(214, 163), T(262, 163), T(262, 187), T(214, 187), Color.FromHex("#5a3820"));
        var edge = Color.FromHex("#2a180c");
        h.DrawLine(T(214, 163), T(262, 163), edge);
        h.DrawLine(T(214, 187), T(262, 187), edge);
        h.DrawLine(T(214, 163), T(214, 187), edge);
        var stitch = new Color(0.79f, 0.63f, 0.44f, 0.7f);
        for (var x = 218f; x < 258f; x += 6f)
        {
            h.DrawLine(T(x, 167), T(x + 3, 167), stitch);
            h.DrawLine(T(x, 183), T(x + 3, 183), stitch);
        }
        foreach (var hx in new[] { 224f, 236f, 248f })
            h.DrawCircle(T(hx, 175), 2f * u, Color.FromHex("#1e1008"));
    }

    /// <summary>The buckle is fixed to the front cover's right edge; its prong slides left to let the strap go.</summary>
    private static void DrawBuckle(DrawingHandleScreen h, Func<float, float, Vector2> l, float u, float turn, float prong, float alpha)
    {
        if (alpha <= 0.02f)
            return;
        Vector2 P(float x, float y) => l(x * turn, y);
        Color A(Color c) => c.WithAlpha(c.A * alpha);

        h.DrawRect(new UIBox2(P(206, 167), P(214, 183)), A(Color.FromHex("#4a2c18")));
        var gold = A(Color.FromHex("#c9a85a"));
        // Frame: four thick bars.
        h.DrawRect(new UIBox2(P(212, 158), P(236, 161.5f)), gold);
        h.DrawRect(new UIBox2(P(212, 188.5f), P(236, 192)), gold);
        h.DrawRect(new UIBox2(P(212, 158), P(215.5f, 192)), gold);
        h.DrawRect(new UIBox2(P(232.5f, 158), P(236, 192)), gold);
        h.DrawRect(new UIBox2(P(212, 174), P(236, 176)), A(Color.FromHex("#b08a3e")));
        // Prong.
        var off = -10f * prong;
        h.DrawRect(new UIBox2(P(214 + off, 173.8f), P(227 + off, 176.2f)), A(Color.FromHex("#e6cf92")));
    }
}
