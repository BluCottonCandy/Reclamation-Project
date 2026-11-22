using System.Numerics;
using Content.Shared._ReclamationProject.Magic;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Timing;

namespace Content.Client._ReclamationProject.Magic;

// #Reclamation Project - the Codex pages, drawn by hand like an old manuscript: gothic headings,
// runes flowing in lines like reading text, ornamental dividers, a star chart and margin vines.

/// <summary>Shared drawing helpers for the hand-drawn Codex pages.</summary>
public abstract class MagicManuscriptPage : Control
{
    protected static readonly Color Ink = Color.FromHex("#3a2410");
    protected static readonly Color FadedInk = Color.FromHex("#b89a68");
    protected static readonly Color Rubric = Color.FromHex("#7a1f1f");
    protected static readonly Color Gold = Color.FromHex("#b08a3e");

    private readonly IResourceCache _cache = IoCManager.Resolve<IResourceCache>();
    private readonly Dictionary<(string, int), Font> _fonts = new();
    protected float Time;

    protected MagicManuscriptPage()
    {
        HorizontalExpand = true;
        VerticalExpand = true;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        Time += args.DeltaSeconds;
    }

    protected Font Gothic(float size) => GetFont("/Fonts/Grenze_Gotisch/GrenzeGotisch.ttf", size);
    protected Font Serif(float size) => GetFont("/Fonts/Cambria.ttf", size);

    private Font GetFont(string path, float size)
    {
        var px = Math.Max(6, (int) MathF.Round(size * UIScale));
        if (!_fonts.TryGetValue((path, px), out var font))
        {
            font = new VectorFont(_cache.GetResource<FontResource>(path), px);
            _fonts[(path, px)] = font;
        }
        return font;
    }

    /// <summary>Width of a string (drawn invisibly far away to measure it).</summary>
    protected static float Measure(DrawingHandleScreen h, Font font, string text)
        => h.DrawString(font, new Vector2(-10000, -10000), text, Color.Transparent).X;

    protected static void Centered(DrawingHandleScreen h, Font font, string text, float y, float width, Color color)
    {
        var w = Measure(h, font, text);
        h.DrawString(font, new Vector2((width - w) / 2f, y), text, color);
    }

    /// <summary>Ornamental divider: ~~~ line ~ diamond ~ line ~~~</summary>
    protected void Divider(DrawingHandleScreen h, float y, float width)
    {
        var s = UIScale;
        var mid = width / 2f;
        h.DrawLine(new Vector2(width * 0.18f, y), new Vector2(mid - 8 * s, y), Gold);
        h.DrawLine(new Vector2(mid + 8 * s, y), new Vector2(width * 0.82f, y), Gold);
        Diamond(h, new Vector2(mid, y), 4 * s, Rubric);
        h.DrawCircle(new Vector2(width * 0.18f, y), 1.5f * s, Gold);
        h.DrawCircle(new Vector2(width * 0.82f, y), 1.5f * s, Gold);
    }

    protected static void Diamond(DrawingHandleScreen h, Vector2 c, float r, Color color)
    {
        h.DrawLine(c + new Vector2(0, -r), c + new Vector2(r, 0), color);
        h.DrawLine(c + new Vector2(r, 0), c + new Vector2(0, r), color);
        h.DrawLine(c + new Vector2(0, r), c + new Vector2(-r, 0), color);
        h.DrawLine(c + new Vector2(-r, 0), c + new Vector2(0, -r), color);
    }

    protected static void Rune(DrawingHandleScreen h, char c, Vector2 origin, float height, Color color, float thickness = 1.5f)
    {
        MagicGlyphControl.DrawRune(h, c, origin, height, color);
    }

    protected static void Ring(DrawingHandleScreen h, Vector2 c, float r, Color color, int dashes = 0)
    {
        const int steps = 48;
        for (var i = 0; i < steps; i++)
        {
            if (dashes > 0 && i % 2 == 1)
                continue;
            var a0 = i / (float) steps * MathF.Tau;
            var a1 = (i + 1) / (float) steps * MathF.Tau;
            h.DrawLine(c + new Vector2(MathF.Cos(a0), MathF.Sin(a0)) * r, c + new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * r, color);
        }
    }
}

/// <summary>Left page: the Key of Runes, written as flowing lines, with a star chart underneath.</summary>
public sealed class MagicCodexKeyPage : MagicManuscriptPage
{
    protected override void Draw(DrawingHandleScreen h)
    {
        var s = UIScale;
        var w = PixelSize.X;
        var hgt = PixelSize.Y;
        if (w < 60 * s || hgt < 80 * s)
            return;

        var margin = 18 * s;
        var y = 10 * s;
        Centered(h, Gothic(20), Loc.GetString("magic-codex-key"), y, w, Rubric);
        y += 28 * s;
        Divider(h, y, w);
        y += 10 * s;

        // Drop cap: a big boxed red rune, then the key flowing like a paragraph.
        var capSize = 34 * s;
        var capBox = new UIBox2(margin, y, margin + capSize, y + capSize);
        h.DrawRect(capBox, Color.FromHex("#f3e6c4"));
        h.DrawRect(capBox, Gold, false);
        Rune(h, 'A', new Vector2(margin + capSize * 0.22f, y + capSize * 0.08f), capSize * 0.85f, Rubric, 2f);

        var serif = Serif(11);
        var runeH = 21 * s;
        var lineH = 28 * s;
        var x = margin + capSize + 8 * s;
        var lineY = y + 2 * s;
        var right = w - margin;

        for (var c = 'A'; c <= 'Z'; c++)
        {
            var letter = c.ToString();
            var entryW = 12 * runeH / 18f + 4 * s + Measure(h, serif, letter) + 12 * s;
            if (x + entryW > right)
            {
                lineY += lineH;
                x = lineY < y + capSize ? margin + capSize + 8 * s : margin;
            }
            Rune(h, c, new Vector2(x, lineY), runeH, Color.FromHex("#1e1208"));
            x += 12 * runeH / 18f + 4 * s;
            h.DrawString(serif, new Vector2(x, lineY + runeH / 2f - 8 * s), letter, Rubric);
            x += Measure(h, serif, letter) + 4 * s;
            h.DrawCircle(new Vector2(x + 2 * s, lineY + runeH * 0.55f), 1.2f * s, FadedInk);
            x += 8 * s;
        }

        y = lineY + lineH + 6 * s;
        Divider(h, y, w);
        y += 8 * s;

        // A star chart fills the rest of the page.
        var room = hgt - y - 14 * s;
        if (room < 40 * s)
            return;
        var r = MathF.Min(room, w - 2 * margin) / 2f;
        var center = new Vector2(w / 2f, y + room / 2f);
        for (var i = 0; i < 4; i++)
            Ring(h, center, r * (1f - i * 0.18f), i % 2 == 0 ? Gold : FadedInk, i % 2);
        for (var i = 0; i < 12; i++)
        {
            var a = i / 12f * MathF.Tau + Time * 0.05f;
            var dir = new Vector2(MathF.Cos(a), MathF.Sin(a));
            h.DrawLine(center + dir * r * 0.46f, center + dir * r, FadedInk);
            Rune(h, (char) ('A' + (i * 7) % 26), center + dir * r * 0.73f - new Vector2(4, 7) * s, 12 * s, Ink, 1f);
        }
        h.DrawCircle(center, r * 0.44f, Color.FromHex("#1a1210"));
        // A star in the middle that slowly turns, with twinkling stars around it.
        for (var i = 0; i < 5; i++)
        {
            var a0 = -MathF.PI / 2 + i * 4 * MathF.PI / 5 + Time * 0.1f;
            var a1 = -MathF.PI / 2 + (i + 1) * 4 * MathF.PI / 5 + Time * 0.1f;
            h.DrawLine(center + new Vector2(MathF.Cos(a0), MathF.Sin(a0)) * r * 0.36f,
                center + new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * r * 0.36f, Color.FromHex("#e8d9b0"));
        }
        var rng = new System.Random(7);
        for (var i = 0; i < 22; i++)
        {
            var a = (float) rng.NextDouble() * MathF.Tau;
            var d = MathF.Sqrt((float) rng.NextDouble()) * r * 0.4f;
            var tw = (MathF.Sin(Time * 2f + i) + 1f) / 2f;
            h.DrawCircle(center + new Vector2(MathF.Cos(a), MathF.Sin(a)) * d, (0.6f + tw) * s,
                Color.InterpolateBetween(Color.FromHex("#8a7a5a"), Color.FromHex("#fff4d0"), tw));
        }
    }
}

/// <summary>Right page: each word as a line of jumbled runes; the first rune of each glows.</summary>
public sealed class MagicCodexWordsPage : MagicManuscriptPage
{
    private List<MagicCodexEntry> _entries = new();

    public void SetEntries(List<MagicCodexEntry> entries) => _entries = entries;

    protected override void Draw(DrawingHandleScreen h)
    {
        var s = UIScale;
        var w = PixelSize.X;
        var hgt = PixelSize.Y;
        if (w < 60 * s || hgt < 80 * s)
            return;

        // Faint watermark sigil behind the writing.
        var mark = new Vector2(w / 2f, hgt * 0.58f);
        var mr = MathF.Min(w, hgt) * 0.36f;
        var faint = Color.FromHex("#e2d2ab");
        Ring(h, mark, mr, faint);
        Ring(h, mark, mr * 0.8f, faint, 1);
        for (var i = 0; i < 5; i++)
        {
            var a0 = -MathF.PI / 2 + i * 4 * MathF.PI / 5;
            var a1 = -MathF.PI / 2 + (i + 1) * 4 * MathF.PI / 5;
            h.DrawLine(mark + new Vector2(MathF.Cos(a0), MathF.Sin(a0)) * mr * 0.8f, mark + new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * mr * 0.8f, faint);
        }

        var margin = 26 * s;
        var y = 10 * s;
        Centered(h, Gothic(20), Loc.GetString("magic-codex-words"), y, w, Rubric);
        y += 28 * s;
        Divider(h, y, w);
        y += 8 * s;
        var serif = Serif(10);
        Centered(h, serif, Loc.GetString("magic-codex-hint-1"), y, w, Ink);
        y += 14 * s;
        Centered(h, serif, Loc.GetString("magic-codex-hint-2"), y, w, Ink);
        y += 20 * s;

        // Margin vine down the left side.
        for (var vy = y; vy < hgt - 16 * s; vy += 4 * s)
        {
            var x0 = 12 * s + MathF.Sin(vy / (14 * s)) * 4 * s;
            var x1 = 12 * s + MathF.Sin((vy + 4 * s) / (14 * s)) * 4 * s;
            h.DrawLine(new Vector2(x0, vy), new Vector2(x1, vy + 4 * s), Color.FromHex("#6b7a3a"));
            if ((int) (vy / s) % 28 < 4)
                h.DrawCircle(new Vector2(x0 + 4 * s, vy), 2 * s, Color.FromHex("#7f8f45"));
        }

        var gothic = Gothic(14);
        var serifSmall = Serif(10);
        var runeH = 18 * s;
        var lineH = 34 * s;
        foreach (var entry in _entries)
        {
            if (y + lineH > hgt - 10 * s)
                break;
            var (dark, light) = MagicGrimoireWindow.ClassColors(entry.Class);
            Diamond(h, new Vector2(margin, y + 9 * s), 4 * s, entry.Solved ? dark : FadedInk);

            // Unsolved: a one-word hint in faded ink. Solved: the word's name in its colour.
            if (entry.Solved)
                h.DrawString(gothic, new Vector2(margin + 10 * s, y), entry.Name, dark);
            else
                h.DrawString(serifSmall, new Vector2(margin + 10 * s, y + 3 * s), string.Concat("\"", entry.Hint, "\""), Color.FromHex("#8a6a3e"));

            var x = margin + 10 * s + 78 * s;
            if (entry.Solved)
            {
                // Spelled out in the right order, each rune with its letter under it.
                for (var i = 0; i < entry.Plain.Length; i++)
                {
                    Rune(h, entry.Plain[i], new Vector2(x, y), runeH, dark);
                    h.DrawString(serifSmall, new Vector2(x + 1 * s, y + runeH + 1 * s), entry.Plain[i].ToString(), Rubric);
                    x += 12 * runeH / 18f + 6 * s;
                }
            }
            else
            {
                var glow = entry.Scrambled.IndexOf(entry.First);
                for (var i = 0; i < entry.Scrambled.Length; i++)
                {
                    var color = Ink;
                    if (i == glow)
                    {
                        var t = (MathF.Sin(Time * 3f) + 1f) / 2f;
                        color = Color.InterpolateBetween(dark, light, t);
                        var c = new Vector2(x + 6 * runeH / 18f, y + runeH / 2f);
                        h.DrawCircle(c, runeH * 0.62f, Color.InterpolateBetween(light, Color.Transparent, 0.75f + 0.15f * t));
                    }
                    Rune(h, entry.Scrambled[i], new Vector2(x, y), runeH, color);
                    x += 12 * runeH / 18f + 5 * s;
                }
            }

            y += lineH - 6 * s;
            for (var dx = margin; dx < w - margin; dx += 6 * s)
                h.DrawLine(new Vector2(dx, y), new Vector2(dx + 2 * s, y), FadedInk);
            y += 6 * s;
        }
    }
}

/// <summary>The Words tab's right page: one word you know, large, with its letters, class and hint.</summary>
public sealed class MagicKnownWordPage : MagicManuscriptPage
{
    private MagicCodexEntry? _entry;

    public void SetWord(MagicCodexEntry? entry) => _entry = entry;

    protected override void Draw(DrawingHandleScreen h)
    {
        var s = UIScale;
        var w = PixelSize.X;
        var hgt = PixelSize.Y;
        if (w < 80 * s || hgt < 120 * s || _entry is not { } entry)
            return;

        var (dark, light) = MagicGrimoireWindow.ClassColors(entry.Class);

        // Faint sigil behind the word.
        var mark = new Vector2(w / 2f, hgt * 0.5f);
        var mr = MathF.Min(w, hgt) * 0.34f;
        var faint = Color.FromHex("#e2d2ab");
        Ring(h, mark, mr, faint);
        Ring(h, mark, mr * 0.82f, faint, 1);

        var y = 8 * s;
        Centered(h, Gothic(24), entry.Name, y, w, dark);
        y += 34 * s;
        Divider(h, y, w);
        y += 10 * s;
        var className = Loc.GetString($"magic-grimoire-class-{entry.Class.ToString().ToLowerInvariant()}");
        Centered(h, Serif(11), Loc.GetString("magic-grimoire-word-class", ("class", className)), y, w, Ink);
        y += 16 * s;
        Centered(h, Serif(11), string.Concat("\"", entry.Hint, "\""), y, w, Color.FromHex("#8a6a3e"));
        y += 30 * s;

        // The word, big, each rune glowing gently with its letter underneath.
        var runeH = 40 * s;
        var step = runeH * 12f / 18f + 8 * s;
        var total = entry.Plain.Length * step - 8 * s;
        if (total > w - 24 * s)
        {
            runeH *= (w - 24 * s) / total;
            step = runeH * 12f / 18f + 8 * s * (w - 24 * s) / total;
            total = w - 24 * s;
        }
        var x = (w - total) / 2f;
        var serif = Serif(14);
        for (var i = 0; i < entry.Plain.Length; i++)
        {
            var t = (MathF.Sin(Time * 2.4f - i * 0.7f) + 1f) / 2f;
            var center = new Vector2(x + runeH * 6f / 18f, y + runeH / 2f);
            h.DrawCircle(center, runeH * 0.55f, Color.InterpolateBetween(light, Color.Transparent, 0.72f + 0.18f * t));
            Rune(h, entry.Plain[i], new Vector2(x, y), runeH, Color.InterpolateBetween(dark, light, t * 0.6f));
            var letter = entry.Plain[i].ToString();
            var lw = Measure(h, serif, letter);
            h.DrawString(serif, new Vector2(center.X - lw / 2f, y + runeH + 4 * s), letter, Rubric);
            x += step;
        }
        y += runeH + 34 * s;
        Divider(h, y, w);
        y += 10 * s;
        Centered(h, Serif(10), Loc.GetString("magic-grimoire-word-combine"), y, w, Ink);
    }
}

/// <summary>#Reclamation Project - Spells tab page: one known spell, how to say it, what it does, where it is held.</summary>
public sealed class MagicKnownSpellPage : MagicManuscriptPage
{
    private MagicKnownSpellEntry? _entry;

    public void SetSpell(MagicKnownSpellEntry? entry) => _entry = entry;

    /// <summary>Draws text centred, wrapped to the page width. Returns the y under it.</summary>
    private static float Wrapped(DrawingHandleScreen h, Font font, string text, float y, float width, float lineH, Color color)
    {
        var line = string.Empty;
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var attempt = line.Length == 0 ? word : string.Concat(line, " ", word);
            if (line.Length > 0 && Measure(h, font, attempt) > width)
            {
                Centered(h, font, line, y, width, color);
                y += lineH;
                line = word;
            }
            else
                line = attempt;
        }
        if (line.Length > 0)
        {
            Centered(h, font, line, y, width, color);
            y += lineH;
        }
        return y;
    }

    protected override void Draw(DrawingHandleScreen h)
    {
        var s = UIScale;
        var w = PixelSize.X;
        var hgt = PixelSize.Y;
        if (w < 80 * s || hgt < 120 * s || _entry is not { } entry)
            return;

        var (dark, light) = MagicGrimoireWindow.ClassColors(entry.Class);
        var faint = Color.FromHex("#e2d2ab");
        var mark = new Vector2(w / 2f, hgt * 0.55f);
        var mr = MathF.Min(w, hgt) * 0.34f;
        Ring(h, mark, mr, faint);
        Ring(h, mark, mr * 0.82f, faint, 1);

        var y = 6 * s;
        y = Wrapped(h, Gothic(22), entry.Name, y, w, 26 * s, dark) + 4 * s;
        Divider(h, y, w);
        y += 8 * s;
        var className = Loc.GetString($"magic-grimoire-class-{entry.Class.ToString().ToLowerInvariant()}");
        Centered(h, Serif(11), Loc.GetString("magic-grimoire-spell-mana", ("class", className), ("mana", entry.ManaCost)), y, w, Ink);
        y += 16 * s;
        y = Wrapped(h, Serif(11), Loc.GetString("magic-grimoire-spell-words", ("words", entry.Words)), y, w - 16 * s, 15 * s, Color.FromHex("#8a6a3e"));
        y += 8 * s;

        // The incantation as runes, letter under each (shrinks to fit the page).
        var letters = entry.Incantation.Replace(" ", string.Empty);
        if (letters.Length > 0)
        {
            var runeH = 26 * s;
            var step = runeH * 12f / 18f + 5 * s;
            var total = letters.Length * step - 5 * s;
            if (total > w - 20 * s)
            {
                var k = (w - 20 * s) / total;
                runeH *= k;
                step *= k;
                total = w - 20 * s;
            }
            var x = (w - total) / 2f;
            var serif = Serif(10);
            for (var i = 0; i < letters.Length; i++)
            {
                var t = (MathF.Sin(Time * 2.4f - i * 0.7f) + 1f) / 2f;
                Rune(h, letters[i], new Vector2(x, y), runeH, Color.InterpolateBetween(dark, light, t * 0.6f));
                var letter = letters[i].ToString();
                var lw = Measure(h, serif, letter);
                h.DrawString(serif, new Vector2(x + runeH * 6f / 18f - lw / 2f, y + runeH + 2 * s), letter, Rubric);
                x += step;
            }
            y += runeH + 20 * s;
        }

        Divider(h, y, w);
        y += 10 * s;
        y = Wrapped(h, Serif(12), entry.Summary, y, w - 20 * s, 17 * s, Ink);
        y += 10 * s;
        Wrapped(h, Serif(10), Loc.GetString("magic-grimoire-spell-where", ("where", entry.Where)), y, w - 20 * s, 14 * s, Color.FromHex("#8a6a3e"));
    }
}

