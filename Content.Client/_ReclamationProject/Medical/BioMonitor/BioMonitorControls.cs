// #Reclamation Project - Pip-Boy bio-monitor drawing controls
using System.Linq;
using System.Numerics;
using Content.Shared._ReclamationProject.Medical.BioMonitor;
using Content.Shared._Shitmed.Targeting;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Input;
using Robust.Shared.Timing;

namespace Content.Client._ReclamationProject.Medical.BioMonitor;

public static class BioMonitorColors
{
    // Current theme. Pip-Boy green by default; the Institute gets a clean white/blue BioScience look.
    public static Color Background, Header, Border, Text, Bright, Dim, Warn, Danger, Selected;
    public static Color FillOk, FillHurt, FillCrit, FillMissing;
    public static bool Institute { get; private set; }

    static BioMonitorColors() => SetTheme(false);

    public static void SetTheme(bool institute)
    {
        Institute = institute;
        if (!institute)
        {
            Background = Color.FromHex("#0b1a06");
            Header = Color.FromHex("#173404");
            Border = Color.FromHex("#3B6D11");
            Text = Color.FromHex("#97C459");
            Bright = Color.FromHex("#C0DD97");
            Dim = Color.FromHex("#639922");
            Warn = Color.FromHex("#EF9F27");
            Danger = Color.FromHex("#E24B4A");
            Selected = Color.White;
            FillOk = Color.FromHex("#3B6D11");
            FillHurt = Color.FromHex("#854F0B");
            FillCrit = Color.FromHex("#A32D2D");
            FillMissing = Color.FromHex("#1c2a14");
            return;
        }

        // Dark slate with soft cyan text: still Institute blue/white, but no bright white glare.
        Background = Color.FromHex("#141C24");
        Header = Color.FromHex("#1E2B36");
        Border = Color.FromHex("#3A5568");
        Text = Color.FromHex("#A9C7D8");
        Bright = Color.FromHex("#DCEAF2");
        Dim = Color.FromHex("#6E8A9C");
        Warn = Color.FromHex("#D9A35A");
        Danger = Color.FromHex("#E07A74");
        Selected = Color.FromHex("#7FD3F0");
        FillOk = Color.FromHex("#3D7FA0");
        FillHurt = Color.FromHex("#8C6A33");
        FillCrit = Color.FromHex("#8A3E3B");
        FillMissing = Color.FromHex("#26323C");
    }

    public static Color ForHealth(float health) =>
        health > 75f ? (Institute ? Color.FromHex("#5FB3D6") : Text) : health > 40f ? Warn : Danger;
}

/// <summary>
/// The clickable body doll. Shapes are drawn in a 124 x 305 "doll space" and scaled to fit.
/// The body faces you, so the character's left arm is on the right of the screen.
/// Hover a limb or an impact dot for details; click a limb to select it, click a dot to pull that item out.
/// </summary>
public sealed class BioMonitorDoll : Control
{
    private const float SpaceX = 48f, SpaceW = 124f, SpaceH = 305f;

    private abstract record Shape;
    private sealed record Box(float X, float Y, float W, float H) : Shape;
    private sealed record Circle(float X, float Y, float R) : Shape;

    private static readonly (TargetBodyPart Part, Shape Shape, Vector2 Badge)[] Layout =
    {
        (TargetBodyPart.Head, new Circle(110, 30, 22), new Vector2(110, 30)),
        (TargetBodyPart.Torso, new Box(82, 56, 56, 70), new Vector2(110, 90)),
        (TargetBodyPart.Groin, new Box(84, 128, 52, 28), new Vector2(110, 142)),
        (TargetBodyPart.RightArm, new Box(61, 58, 18, 98), new Vector2(70, 105)),
        (TargetBodyPart.LeftArm, new Box(141, 58, 18, 98), new Vector2(150, 105)),
        (TargetBodyPart.RightHand, new Circle(70, 168, 10), new Vector2(70, 168)),
        (TargetBodyPart.LeftHand, new Circle(150, 168, 10), new Vector2(150, 168)),
        (TargetBodyPart.RightLeg, new Box(85, 160, 23, 120), new Vector2(96, 220)),
        (TargetBodyPart.LeftLeg, new Box(112, 160, 23, 120), new Vector2(124, 220)),
        (TargetBodyPart.RightFoot, new Box(78, 284, 30, 14), new Vector2(93, 291)),
        (TargetBodyPart.LeftFoot, new Box(112, 284, 30, 14), new Vector2(127, 291)),
    };

    private Dictionary<TargetBodyPart, BioMonitorPartState> _parts = new();
    public TargetBodyPart Selected = TargetBodyPart.Torso;

    /// <summary>Whole-body bleeding: a blood drop is drawn on the chest.</summary>
    public bool Bleeding;

    /// <summary>Items to ring on the doll (the list row under the mouse).</summary>
    public HashSet<NetEntity> Highlighted = new();

    /// <summary>Items being pulled out right now (drawn hollow).</summary>
    public Func<NetEntity, bool>? IsPending;

    public event Action<TargetBodyPart>? OnPartSelected;
    /// <summary>A dot was clicked: pull that item out.</summary>
    public event Action<TargetBodyPart, BioMonitorEmbedState>? OnDotClicked;

    private Vector2? _mouse;
    // Rebuilt every draw: where each dot is on screen, for hover and clicks.
    private readonly List<(TargetBodyPart Part, BioMonitorEmbedState Item, Vector2 Pos, float Radius)> _dots = new();

    public BioMonitorDoll()
    {
        MouseFilter = MouseFilterMode.Stop;
        MinSize = new Vector2(170, 340);
    }

    public void SetParts(Dictionary<TargetBodyPart, BioMonitorPartState> parts) => _parts = parts;

    private (float Scale, Vector2 Offset) Fit()
    {
        var scale = MathF.Min(PixelWidth / SpaceW, PixelHeight / SpaceH);
        var offset = new Vector2((PixelWidth - SpaceW * scale) / 2f, (PixelHeight - SpaceH * scale) / 2f);
        return (scale, offset);
    }

    private Vector2 ToScreen(Vector2 p, float scale, Vector2 offset) =>
        offset + new Vector2((p.X - SpaceX) * scale, p.Y * scale);

    private Vector2 ToDoll(Vector2 screen, float scale, Vector2 offset) =>
        new((screen.X - offset.X) / scale + SpaceX, (screen.Y - offset.Y) / scale);

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var (scale, offset) = Fit();
        _dots.Clear();

        foreach (var (part, shape, badge) in Layout)
        {
            _parts.TryGetValue(part, out var state);
            var missing = state == null || state.Missing;
            var health = state?.Health ?? 100f;
            var fill = missing ? BioMonitorColors.FillMissing
                : health > 75f ? BioMonitorColors.FillOk
                : health > 40f ? BioMonitorColors.FillHurt
                : BioMonitorColors.FillCrit;
            var hovered = HoveredPart(scale, offset) == part;
            var outline = part == Selected ? BioMonitorColors.Selected
                : BioMonitorColors.Institute ? BioMonitorColors.Border : BioMonitorColors.Bright;
            var thickness = part == Selected ? 3 : hovered ? 2 : 1;

            switch (shape)
            {
                case Box b:
                {
                    var tl = ToScreen(new Vector2(b.X, b.Y), scale, offset);
                    var box = new UIBox2(tl, tl + new Vector2(b.W, b.H) * scale);
                    handle.DrawRect(box, fill);
                    for (var i = 0; i < thickness; i++)
                        handle.DrawRect(new UIBox2(box.Left - i, box.Top - i, box.Right + i, box.Bottom + i), outline, false);
                    break;
                }
                case Circle c:
                {
                    var center = ToScreen(new Vector2(c.X, c.Y), scale, offset);
                    handle.DrawCircle(center, c.R * scale, fill);
                    for (var i = 0; i < thickness; i++)
                        handle.DrawCircle(center, c.R * scale + i, outline, false);
                    break;
                }
            }

            if (state == null || missing)
                continue;

            var pos = ToScreen(badge, scale, offset);
            var mark = MathF.Max(6f, 3.5f * scale);

            // Fracture: an X in the top-right of the limb.
            if (state.Fracture)
            {
                var at = pos + new Vector2(mark * 0.6f, -mark * 2f);
                var color = state.Splinted ? BioMonitorColors.Dim : BioMonitorColors.Warn;
                handle.DrawLine(at, at + new Vector2(mark, mark), color);
                handle.DrawLine(at + new Vector2(mark, 0), at + new Vector2(0, mark), color);
                handle.DrawLine(at + new Vector2(1, 0), at + new Vector2(mark + 1, mark), color);
            }

            // Burn: a small triangle in the top-left of the limb.
            if (state.Damage.TryGetValue("Heat", out var heat) && heat > 0f
                || state.Damage.TryGetValue("Caustic", out var caustic) && caustic > 0f)
            {
                var at = pos + new Vector2(-mark * 1.6f, -mark * 1f);
                var tri = new[] { at + new Vector2(mark / 2f, 0), at + new Vector2(mark, mark), at + new Vector2(0, mark) };
                handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, tri, BioMonitorColors.Warn);
            }

            // Bleeding: a blood drop on the chest.
            if (part == TargetBodyPart.Torso && Bleeding)
            {
                var at = pos + new Vector2(mark * 1.2f, mark * 1.5f);
                handle.DrawCircle(at, mark / 2f, BioMonitorColors.Danger);
                var tri = new[] { at + new Vector2(-mark / 2f, 0), at + new Vector2(mark / 2f, 0), at + new Vector2(0, -mark) };
                handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, tri, BioMonitorColors.Danger);
            }

            // One impact dot per stuck item, each at its own spot on the limb.
            // The spot comes from the item's id, so it stays put between refreshes.
            foreach (var stuck in state.Embedded)
            {
                var center = ToScreen(ImpactSpot(shape, stuck.Item.Id), scale, offset);
                var radius = MathF.Max(3f, 1.8f * scale);
                _dots.Add((part, stuck, center, radius));

                var color = stuck.Deep ? BioMonitorColors.Danger : BioMonitorColors.Warn;
                var lit = Highlighted.Contains(stuck.Item);
                if (lit)
                    handle.DrawCircle(center, radius + 4f, BioMonitorColors.Selected, false);

                handle.DrawCircle(center, radius + 1f, BioMonitorColors.Background);
                if (IsPending?.Invoke(stuck.Item) == true)
                    handle.DrawCircle(center, radius, color, false); // being pulled out: hollow
                else
                    handle.DrawCircle(center, radius, color);
            }
        }

        DrawTooltip(handle, scale, offset);
    }

    #region Hover and tooltips

    private (TargetBodyPart Part, BioMonitorEmbedState Item)? HoveredDot()
    {
        if (_mouse is not { } mouse)
            return null;

        foreach (var (part, item, pos, radius) in _dots)
        {
            if (Vector2.Distance(mouse, pos) <= radius + 3f)
                return (part, item);
        }

        return null;
    }

    private TargetBodyPart? HoveredPart(float scale, Vector2 offset)
    {
        if (_mouse is not { } mouse)
            return null;

        var p = ToDoll(mouse, scale, offset);
        // Check small parts (hands, feet) first so they win over the arm/leg boxes they touch.
        for (var i = Layout.Length - 1; i >= 0; i--)
        {
            var (part, shape, _) = Layout[i];
            var hit = shape switch
            {
                Box b => p.X >= b.X && p.X <= b.X + b.W && p.Y >= b.Y && p.Y <= b.Y + b.H,
                Circle c => Vector2.Distance(p, new Vector2(c.X, c.Y)) <= c.R,
                _ => false,
            };

            if (hit)
                return part;
        }

        return null;
    }

    private List<(string Text, Color Color)> TooltipLines(float scale, Vector2 offset)
    {
        var lines = new List<(string, Color)>();

        if (HoveredDot() is { } dot)
        {
            var item = dot.Item;
            lines.Add((item.Name, BioMonitorColors.Bright));
            lines.Add((PartName(dot.Part), BioMonitorColors.Dim));
            if (item.NeedsTool)
                lines.Add((Loc.GetString("bio-monitor-how-needs-tool"), BioMonitorColors.Warn));
            else
            {
                lines.Add((Loc.GetString("bio-monitor-tooltip-click-remove", ("seconds", $"{item.Seconds:0.#}")), BioMonitorColors.Text));
                if (item.BleedRisk > 0f)
                {
                    lines.Add((Loc.GetString(item.HeavyBleed ? "bio-monitor-bleed-risk-heavy" : "bio-monitor-bleed-risk",
                        ("percent", $"{item.BleedRisk * 100f:0}")), item.HeavyBleed ? BioMonitorColors.Danger : BioMonitorColors.Warn));
                }
            }

            return lines;
        }

        if (HoveredPart(scale, offset) is not { } part || !_parts.TryGetValue(part, out var state))
            return lines;

        if (state.Missing)
        {
            lines.Add((PartName(part), BioMonitorColors.Bright));
            lines.Add((Loc.GetString("bio-monitor-limb-missing"), BioMonitorColors.Danger));
            return lines;
        }

        lines.Add(($"{PartName(part)}  {state.Health:0}%", BioMonitorColors.ForHealth(state.Health)));
        foreach (var (type, amount) in state.Damage.OrderByDescending(d => d.Value).Take(4))
            lines.Add(($"{type} {amount:0.#}", BioMonitorColors.Text));
        if (state.Fracture)
        {
            lines.Add((Loc.GetString(state.Splinted ? "bio-monitor-fracture-splinted" : "bio-monitor-fracture",
                ("healed", $"{state.FractureHealing:0}")), state.Splinted ? BioMonitorColors.Dim : BioMonitorColors.Warn));
            lines.Add((Loc.GetString("bio-monitor-fracture-time", ("minutes", state.FractureMinutesLeft)), BioMonitorColors.Dim));
        }
        if (state.Embedded.Count > 0)
            lines.Add((Loc.GetString("bio-monitor-embedded-count", ("count", state.Embedded.Count)), BioMonitorColors.Warn));
        if (part == TargetBodyPart.Torso && Bleeding)
            lines.Add((Loc.GetString("bio-monitor-bleeding"), BioMonitorColors.Danger));
        lines.Add((Loc.GetString("bio-monitor-tooltip-click-limb"), BioMonitorColors.Dim));
        return lines;
    }

    private void DrawTooltip(DrawingHandleScreen handle, float scale, Vector2 offset)
    {
        if (_mouse is not { } mouse)
            return;

        var lines = TooltipLines(scale, offset);
        if (lines.Count == 0)
            return;

        var font = UserInterfaceManager.ThemeDefaults.DefaultFont;
        var lineHeight = font.GetLineHeight(1f);
        var width = lines.Max(l => handle.GetDimensions(font, l.Text, 1f).X) + 12f;
        var height = lines.Count * lineHeight + 8f;

        // Keep it inside the doll area.
        var pos = mouse + new Vector2(14, 10);
        if (pos.X + width > PixelWidth)
            pos.X = MathF.Max(0, mouse.X - width - 8);
        if (pos.Y + height > PixelHeight)
            pos.Y = MathF.Max(0, PixelHeight - height);

        var box = new UIBox2(pos, pos + new Vector2(width, height));
        handle.DrawRect(box, BioMonitorColors.Background);
        handle.DrawRect(box, BioMonitorColors.Border, false);
        for (var i = 0; i < lines.Count; i++)
            handle.DrawString(font, pos + new Vector2(6, 4 + i * lineHeight), lines[i].Text, lines[i].Color);
    }

    private static string PartName(TargetBodyPart part) =>
        Loc.GetString($"body-part-name-{part.ToString().ToLowerInvariant()}");

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);
        _mouse = args.RelativePixelPosition;
    }

    protected override void MouseExited()
    {
        base.MouseExited();
        _mouse = null;
    }

    #endregion

    /// <summary>A stable, spread-out spot inside the limb shape for one stuck item.</summary>
    private static Vector2 ImpactSpot(Shape shape, int seed)
    {
        var random = new System.Random(seed * 7919 + 17);
        switch (shape)
        {
            case Box b:
                return new Vector2(
                    b.X + b.W * (0.2f + 0.6f * random.NextSingle()),
                    b.Y + b.H * (0.12f + 0.76f * random.NextSingle()));
            case Circle c:
                var angle = random.NextSingle() * MathF.Tau;
                var distance = c.R * 0.6f * MathF.Sqrt(random.NextSingle());
                return new Vector2(c.X + MathF.Cos(angle) * distance, c.Y + MathF.Sin(angle) * distance);
            default:
                return Vector2.Zero;
        }
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);

        if (args.Function != EngineKeyFunctions.UIClick)
            return;

        _mouse = args.RelativePixelPosition;

        // A dot wins over the limb under it: clicking it pulls that item out.
        if (HoveredDot() is { } dot)
        {
            Selected = dot.Part;
            OnPartSelected?.Invoke(dot.Part);
            OnDotClicked?.Invoke(dot.Part, dot.Item);
            args.Handle();
            return;
        }

        var (scale, offset) = Fit();
        if (HoveredPart(scale, offset) is not { } part)
            return;

        Selected = part;
        OnPartSelected?.Invoke(part);
        args.Handle();
    }
}

/// <summary>A flat progress bar in bio-monitor colours.</summary>
public sealed class BioMonitorBar : Control
{
    public float Value = 100f;
    public Color Color = BioMonitorColors.Text;

    public BioMonitorBar()
    {
        MinSize = new Vector2(40, 10);
        HorizontalExpand = true;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        var box = PixelSizeBox;
        handle.DrawRect(box, BioMonitorColors.Border, false);
        var fill = new UIBox2(box.Left + 1, box.Top + 1,
            box.Left + 1 + (box.Width - 2) * Math.Clamp(Value / 100f, 0f, 1f), box.Bottom - 1);
        handle.DrawRect(fill, Color);
    }
}

/// <summary>A scrolling heartbeat line. Beats at <see cref="Bpm"/>; flat when 0.</summary>
public sealed class BioMonitorPulse : Control
{
    public int Bpm = 70;
    private readonly List<float> _points = new();
    private float _phase;
    private float _accumulator;

    public BioMonitorPulse()
    {
        MinSize = new Vector2(120, 40);
        HorizontalExpand = true;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        // Add one point per 1/60 s regardless of framerate.
        _accumulator += args.DeltaSeconds;
        while (_accumulator >= 1f / 60f)
        {
            _accumulator -= 1f / 60f;
            float y = 0;
            if (Bpm > 0)
            {
                _phase = (_phase + Bpm / 60f / 60f) % 1f;
                y = _phase switch
                {
                    < 0.04f => 0.15f,
                    < 0.07f => -0.15f,
                    < 0.10f => 0.85f,
                    < 0.13f => -0.8f,
                    > 0.30f and < 0.40f => 0.18f,
                    _ => 0f,
                };
            }

            _points.Add(y);
        }

        var max = Math.Max(10, PixelWidth / 2);
        while (_points.Count > max)
            _points.RemoveAt(0);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        if (_points.Count < 2)
            return;

        var mid = PixelHeight / 2f;
        var amp = PixelHeight / 2f - 2f;
        var color = Bpm == 0 ? BioMonitorColors.Danger : BioMonitorColors.Text;
        for (var i = 1; i < _points.Count; i++)
        {
            var a = new Vector2((i - 1) * 2, mid - _points[i - 1] * amp);
            var b = new Vector2(i * 2, mid - _points[i] * amp);
            handle.DrawLine(a, b, color);
        }
    }
}
