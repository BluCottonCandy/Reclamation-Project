using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Timing;

namespace Content.Client._ReclamationProject.Magic;

/// <summary>
/// #Reclamation Project - draws text in the Arcane glyph script.
/// Every letter A-Z is one fixed rune-like glyph: a vertical stem plus one or two marks.
/// Modes: still ink, a rainbow wave (while writing), or a wave in one class colour (a working page).
/// </summary>
public sealed class MagicGlyphControl : Control
{
    public enum GlyphMode
    {
        Ink,
        Rainbow,
        Glow,
    }

    // Marks on a 12 x 18 grid (from the approved mockup). Each mark is one or more line segments.
    private static readonly Vector2[][] Marks =
    {
        new[] { new Vector2(6, 2), new Vector2(11, 6) },
        new[] { new Vector2(6, 2), new Vector2(1, 6) },
        new[] { new Vector2(6, 9), new Vector2(11, 5) },
        new[] { new Vector2(6, 9), new Vector2(1, 5) },
        new[] { new Vector2(6, 9), new Vector2(11, 13) },
        new[] { new Vector2(6, 9), new Vector2(1, 13) },
        new[] { new Vector2(6, 16), new Vector2(11, 12) },
        new[] { new Vector2(6, 16), new Vector2(1, 12) },
        new[] { new Vector2(2, 9), new Vector2(10, 9) },
        new[] { new Vector2(4, 2), new Vector2(8, 2), new Vector2(8, 5), new Vector2(4, 5), new Vector2(4, 2) },
        new[] { new Vector2(3, 16), new Vector2(6, 12), new Vector2(9, 16) },
        new[] { new Vector2(3, 2), new Vector2(6, 6), new Vector2(9, 2) },
        new[] { new Vector2(9, 8), new Vector2(10, 9), new Vector2(9, 10), new Vector2(8, 9), new Vector2(9, 8) },
        new[] { new Vector2(3, 8), new Vector2(4, 9), new Vector2(3, 10), new Vector2(2, 9), new Vector2(3, 8) },
    };

    private static readonly Color[] Rainbow =
    {
        Color.FromHex("#D85A30"), Color.FromHex("#BA7517"), Color.FromHex("#639922"),
        Color.FromHex("#1D9E75"), Color.FromHex("#7F77DD"), Color.FromHex("#A32D2D"),
    };

    public string Text = string.Empty;
    public GlyphMode Mode = GlyphMode.Ink;
    public Color InkColor = Color.FromHex("#4a3418");
    public Color GlowDark = Color.White;
    public Color GlowLight = Color.White;
    public float GlyphHeight = 36f;
    public float Thickness = 2f;
    /// <summary>One glyph (by position) that pulses in the glow colours even in Ink mode - the Codex hint.</summary>
    public int HighlightIndex = -1;

    /// <summary>Glyphs bob up and down in a wave when not plain ink.</summary>
    public bool Bob = true;

    private float _time;

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _time += args.DeltaSeconds;
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        // Only as wide as the glyphs themselves (HorizontalExpand still lets it stretch).
        var scale = GlyphHeight / 18f;
        var width = 0f;
        foreach (var c in Text)
            width += c == ' ' ? 8f * scale : 12f * scale + 2f;
        return new Vector2(MathF.Min(availableSize.X, width), GlyphHeight + 10f);
    }

    private static readonly Texture?[] RuneTextures = new Texture?[26];
    private static bool _runesLoaded;

    /// <summary>The rune texture for a letter (white, so it can be tinted), or null for anything that isn't A-Z.</summary>
    public static Texture? RuneTexture(char c)
    {
        var i = char.ToUpperInvariant(c) - 'A';
        if (i is < 0 or > 25)
            return null;
        if (!_runesLoaded)
        {
            _runesLoaded = true;
            var cache = IoCManager.Resolve<IResourceCache>();
            var rsi = cache.GetResource<RSIResource>("/Textures/_ReclamationProject/Magic/runes.rsi").RSI;
            for (var n = 0; n < 26; n++)
            {
                if (rsi.TryGetState(((char) ('a' + n)).ToString(), out var state))
                    RuneTextures[n] = state.Frame0;
            }
        }
        return RuneTextures[i];
    }

    /// <summary>
    /// Draws one Arcane rune (the Daedric-style alphabet). <paramref name="origin"/> is the top-left of a
    /// 12 x 18 glyph box scaled to <paramref name="height"/>; the rune is centred in it.
    /// </summary>
    public static void DrawRune(DrawingHandleScreen h, char c, Vector2 origin, float height, Color color)
    {
        if (RuneTexture(c) is not { } tex || height <= 0f)
            return;
        var boxW = height * 12f / 18f;
        var w = height * 0.75f;
        var pos = new Vector2(origin.X + (boxW - w) / 2f, origin.Y);
        h.DrawTextureRect(tex, UIBox2.FromDimensions(pos, new Vector2(w, height)), color);
    }

    /// <summary>Draws a rune centred on <paramref name="center"/>, turned by <paramref name="angle"/> radians.</summary>
    public static void DrawRuneRotated(DrawingHandleScreen h, char c, Vector2 center, float height, Color color, float angle)
    {
        if (RuneTexture(c) is not { } tex || height <= 0f)
            return;
        var w = height * 0.75f;
        var old = h.GetTransform();
        h.SetTransform(Matrix3x2.CreateRotation(angle) * Matrix3x2.CreateTranslation(center) * old);
        h.DrawTextureRect(tex, UIBox2.FromDimensions(new Vector2(-w / 2f, -height / 2f), new Vector2(w, height)), color);
        h.SetTransform(old);
    }

    public static IEnumerable<(Vector2 A, Vector2 B)> Segments(char c)
    {
        var i = char.ToUpperInvariant(c) - 'A';
        if (i is < 0 or > 25)
            yield break;

        yield return (new Vector2(6, 2), new Vector2(6, 16)); // stem
        foreach (var mark in new[] { Marks[i % Marks.Length], i % 3 == 0 ? null : Marks[(i * 5 + 3) % Marks.Length] })
        {
            if (mark == null)
                continue;
            for (var p = 0; p < mark.Length - 1; p++)
                yield return (mark[p], mark[p + 1]);
        }
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        if (string.IsNullOrEmpty(Text))
            return;

        var scale = GlyphHeight / 18f * UIScale;
        var glyphWidth = 12f * scale;
        var space = 8f * scale;
        var total = 0f;
        foreach (var c in Text)
            total += c == ' ' ? space : glyphWidth + 2f * UIScale;

        var x = MathF.Max(0f, (PixelSize.X - total) / 2f);
        var baseY = (PixelSize.Y - GlyphHeight * UIScale) / 2f;
        var index = 0;

        foreach (var c in Text)
        {
            if (c == ' ')
            {
                x += space;
                continue;
            }

            var phase = _time * 3.5f - index * 0.75f;
            var bob = Mode != GlyphMode.Ink && Bob ? MathF.Sin(phase) * 4f * UIScale : 0f;
            var color = Mode switch
            {
                GlyphMode.Rainbow => Rainbow[(int) MathF.Floor((_time * 4f + index * 0.7f) % Rainbow.Length + Rainbow.Length) % Rainbow.Length],
                GlyphMode.Glow => Color.InterpolateBetween(GlowDark, GlowLight, (MathF.Sin(phase) + 1f) / 2f),
                _ => InkColor,
            };
            if (index == HighlightIndex)
                color = Color.InterpolateBetween(GlowDark, GlowLight, (MathF.Sin(_time * 3f) + 1f) / 2f);

            var origin = new Vector2(x, baseY + bob);
            DrawRune(handle, c, origin, 18f * scale, color);

            x += glyphWidth + 2f * UIScale;
            index++;
        }
    }
}
