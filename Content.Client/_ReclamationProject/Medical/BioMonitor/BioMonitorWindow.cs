// #Reclamation Project - Pip-Boy bio-monitor window
using System.Linq;
using System.Numerics;
using Content.Shared._ReclamationProject.Medical.BioMonitor;
using Content.Shared._Shitmed.Targeting;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._ReclamationProject.Medical.BioMonitor;

public sealed class BioMonitorWindow : DefaultWindow
{
    public event Action<NetEntity>? OnRemove;

    private readonly Label _header = Text("", 18, BioMonitorColors.Bright);
    private readonly Label _status = Text("", 16, BioMonitorColors.Danger);
    private readonly BioMonitorDoll _doll = new();

    private readonly Label _limbName = Text("", 17, BioMonitorColors.Bright);
    private readonly Label _limbHealth = Text("");
    private readonly BioMonitorBar _limbBar = new();
    private readonly BoxContainer _limbDamage = VBox();
    private readonly Label _embedCount = Text("", 15, BioMonitorColors.Warn);
    private readonly BoxContainer _embedList = VBox();

    private readonly Label _overall = Text("");
    private readonly BioMonitorBar _overallBar = new();
    private readonly Label _blood = Text("");
    private readonly BioMonitorBar _bloodBar = new();
    private readonly Label _rads = Text("");
    private readonly BioMonitorBar _radBar = new();
    private readonly BoxContainer _radInfo = VBox();
    private readonly Label _bpm = Text("", 22, BioMonitorColors.Bright);
    private readonly BioMonitorPulse _pulse = new();
    private readonly BoxContainer _condition = VBox();
    private readonly GridContainer _chems = new() { Columns = 2, HSeparationOverride = 16 };

    private BioMonitorStateMessage? _state;
    /// <summary>Items we've asked to remove, and when to give up waiting.</summary>
    private readonly Dictionary<NetEntity, DateTime> _pending = new();
    private string _listKey = string.Empty;
    private Dictionary<TargetBodyPart, BioMonitorPartState> _parts = new();

    public readonly bool Institute;

    public BioMonitorWindow(bool institute)
    {
        // Colours are read while building the controls, so set the theme first.
        Institute = institute;
        BioMonitorColors.SetTheme(institute);
        Title = Loc.GetString(institute ? "bio-monitor-window-title-institute" : "bio-monitor-window-title");
        MinSize = new Vector2(560, 520);
        SetSize = new Vector2(600, 600);

        var root = VBox(8);
        root.Margin = new Thickness(4);

        // Header.
        var header = HBox(8);
        _header.HorizontalExpand = true;
        header.AddChild(_header);
        header.AddChild(_status);
        root.AddChild(header);

        // Middle: doll | limb info.
        var middle = HBox(8);
        middle.VerticalExpand = true;

        var dollBody = VBox(4);
        _doll.VerticalExpand = true;
        _doll.HorizontalExpand = true;
        _doll.OnPartSelected += _ => RefreshLimb();
        _doll.IsPending = item => _pending.ContainsKey(item);
        // Clicking an impact dot on the body pulls that item out, same as its Remove button.
        _doll.OnDotClicked += (_, item) => RequestRemove(item.Item, item.Seconds);
        dollBody.AddChild(_doll);
        var legend = new RichTextLabel();
        legend.SetMessage(FormattedMessage.FromMarkupOrThrow(Loc.GetString(institute ? "bio-monitor-legend-institute" : "bio-monitor-legend")));
        dollBody.AddChild(legend);
        var dollPanel = Panel(Loc.GetString("bio-monitor-body-header"), dollBody);
        dollPanel.HorizontalExpand = true;
        dollPanel.SizeFlagsStretchRatio = 1.2f;
        middle.AddChild(dollPanel);

        var right = VBox(8);
        right.HorizontalExpand = true;

        var limbBody = VBox(2);
        limbBody.AddChild(Row(Text(Loc.GetString("bio-monitor-integrity")), _limbHealth));
        limbBody.AddChild(_limbBar);
        limbBody.AddChild(_limbDamage);
        right.AddChild(Panel(_limbName, limbBody));

        var embedTitle = HBox(4);
        var embedHeader = Text(Loc.GetString("bio-monitor-embedded-header"), 17, BioMonitorColors.Bright);
        embedHeader.HorizontalExpand = true;
        embedTitle.AddChild(embedHeader);
        embedTitle.AddChild(_embedCount);
        // Fixed height inside the page scroll; it scrolls on its own when a limb has lots of items.
        var scroll = new ScrollContainer { HScrollEnabled = false, MinSize = new Vector2(0, 160), MaxHeight = 160 };
        scroll.AddChild(_embedList);
        var embedPanel = Panel(embedTitle, scroll);
        embedPanel.VerticalExpand = true;
        right.AddChild(embedPanel);

        middle.AddChild(right);
        root.AddChild(middle);

        // Bottom row: vitals | pulse | condition.
        var bottom = HBox(8);

        var vitals = VBox(2);
        vitals.AddChild(Row(Text(Loc.GetString("bio-monitor-overall")), _overall));
        vitals.AddChild(_overallBar);
        vitals.AddChild(Row(Text(Loc.GetString("bio-monitor-blood")), _blood));
        vitals.AddChild(_bloodBar);
        vitals.AddChild(Row(Text(Loc.GetString("bio-monitor-rads")), _rads));
        vitals.AddChild(_radBar);
        vitals.AddChild(_radInfo);
        var vitalsPanel = Panel(Loc.GetString("bio-monitor-vitals-header"), vitals);
        vitalsPanel.HorizontalExpand = true;
        bottom.AddChild(vitalsPanel);

        var pulse = VBox(2);
        pulse.AddChild(Row(_bpm, Text(Loc.GetString("bio-monitor-bpm"))));
        pulse.AddChild(_pulse);
        var pulsePanel = Panel(Loc.GetString("bio-monitor-pulse-header"), pulse);
        pulsePanel.HorizontalExpand = true;
        bottom.AddChild(pulsePanel);

        var conditionPanel = Panel(Loc.GetString("bio-monitor-condition-header"), _condition);
        conditionPanel.HorizontalExpand = true;
        bottom.AddChild(conditionPanel);

        root.AddChild(bottom);

        root.AddChild(Panel(Loc.GetString("bio-monitor-chems-header"), _chems));

        var background = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat { BackgroundColor = BioMonitorColors.Background },
            VerticalExpand = true,
            HorizontalExpand = true,
        };
        // The whole screen scrolls, so a small window never squashes or overlaps the sections:
        // everything keeps its size and you scroll down to reach the rest.
        var pageScroll = new ScrollContainer
        {
            HScrollEnabled = false,
            VerticalExpand = true,
            HorizontalExpand = true,
        };
        pageScroll.AddChild(root);
        background.AddChild(pageScroll);
        Contents.AddChild(background);
    }

    /// <summary>Ask the server to pull an item out. The button and dot change straight away.</summary>
    private void RequestRemove(NetEntity item, float seconds)
    {
        if (_pending.ContainsKey(item))
            return;

        _pending[item] = DateTime.UtcNow.AddSeconds(seconds + 0.75);
        OnRemove?.Invoke(item);
        RefreshLimb();
    }

    protected override void FrameUpdate(Robust.Shared.Timing.FrameEventArgs args)
    {
        base.FrameUpdate(args);

        // A removal that didn't finish (moved, got hit) frees its button again.
        if (_pending.Count == 0)
            return;

        var now = DateTime.UtcNow;
        var expired = _pending.Where(p => p.Value < now).Select(p => p.Key).ToList();
        if (expired.Count == 0)
            return;

        foreach (var item in expired)
            _pending.Remove(item);
        RefreshLimb();
    }

    public void UpdateState(BioMonitorStateMessage state)
    {
        _state = state;

        // Forget pending removals for items that are already gone.
        var present = state.Parts.SelectMany(p => p.Embedded).Select(e => e.Item).ToHashSet();
        foreach (var item in _pending.Keys.Where(i => !present.Contains(i)).ToList())
            _pending.Remove(item);
        _parts = state.Parts.ToDictionary(p => (TargetBodyPart) p.Part, p => p);
        _doll.SetParts(_parts);
        _doll.Bleeding = state.BleedRate > 0f;

        var source = Loc.GetString(state.InstituteStyle ? "bio-monitor-source-institute" : state.Source switch
        {
            BioMonitorSource.PipBoy => "bio-monitor-source-pipboy",
            BioMonitorSource.Intelligence => "bio-monitor-source-intelligence",
            _ => "bio-monitor-source-scanner",
        });
        _header.Text = state.Self ? source : $"{source} - {Loc.GetString("bio-monitor-patient", ("name", state.TargetName))}";
        _status.Text = state.Dead ? Loc.GetString("bio-monitor-status-dead")
            : state.Critical ? Loc.GetString("bio-monitor-status-crit")
            : string.Empty;

        // Vitals.
        _overall.Text = $"{state.Overall:0}%";
        _overallBar.Value = state.Overall;
        _overallBar.Color = BioMonitorColors.ForHealth(state.Overall);

        var bleeding = state.BleedRate > 0f;
        _blood.Text = bleeding ? $"{state.Blood:0}% {Loc.GetString("bio-monitor-bleeding")}" : $"{state.Blood:0}%";
        _blood.FontColorOverride = bleeding ? BioMonitorColors.Danger : BioMonitorColors.Text;
        _bloodBar.Value = state.Blood;
        _bloodBar.Color = state.Blood < 60f ? BioMonitorColors.Danger : BioMonitorColors.Text;
        // Radiation: absorbed rads, sickness stage, live exposure and rad meds.
        var (stage, stageColor) = state.Radiation switch
        {
            <= 0f => ("none", BioMonitorColors.Text),
            < 25f => ("minor", BioMonitorColors.Warn),
            < 50f => ("advanced", BioMonitorColors.Warn),
            < 100f => ("critical", BioMonitorColors.Danger),
            _ => ("deadly", BioMonitorColors.Danger),
        };
        _rads.Text = $"{state.Radiation:0} - {Loc.GetString($"bio-monitor-rad-stage-{stage}")}";
        _rads.FontColorOverride = stageColor;
        _radBar.Value = Math.Min(state.Radiation, 100f);
        _radBar.Color = stageColor;
        _radInfo.RemoveAllChildren();
        if (state.RadExposure > 0.01f)
        {
            _radInfo.AddChild(Text(Loc.GetString("bio-monitor-rad-exposure", ("rate", $"{state.RadExposure:0.0}")), 14,
                state.RadExposure >= 1f ? BioMonitorColors.Danger : BioMonitorColors.Warn));
        }
        if (state.RadX)
            _radInfo.AddChild(Text(Loc.GetString("bio-monitor-rad-x"), 14, BioMonitorColors.Dim));
        if (state.RadAway)
            _radInfo.AddChild(Text(Loc.GetString("bio-monitor-radaway"), 14, BioMonitorColors.Dim));

        _bpm.Text = state.Pulse.ToString();
        _pulse.Bpm = state.Pulse;

        // Condition.
        _condition.RemoveAllChildren();
        if (state.TemperatureC is { } temp)
            _condition.AddChild(Row(Text(Loc.GetString("bio-monitor-temp")), Text($"{temp:0.0} C")));
        if (state.Hunger != string.Empty)
            _condition.AddChild(Row(Text(Loc.GetString("bio-monitor-hunger")), Text(state.Hunger)));
        if (state.Thirst != string.Empty)
            _condition.AddChild(Row(Text(Loc.GetString("bio-monitor-thirst")), Text(state.Thirst)));
        var suffocating = state.Suffocation > 0f;
        _condition.AddChild(Row(Text(Loc.GetString("bio-monitor-breathing")),
            Text(Loc.GetString(suffocating ? "bio-monitor-breathing-bad" : "bio-monitor-breathing-ok"), 15,
                suffocating ? BioMonitorColors.Danger : BioMonitorColors.Text)));
        if (state.SyntheticPatient)
            _condition.AddChild(Text(Loc.GetString("bio-monitor-synthetic"), 14, BioMonitorColors.Dim));
        if (state.InternalBleeding)
            _condition.AddChild(Text(Loc.GetString("bio-monitor-internal-bleeding"), 15, BioMonitorColors.Danger));
        if (state.PoisonDamage > 0f)
            _condition.AddChild(Row(Text(Loc.GetString("bio-monitor-poisoned")), Text($"{state.PoisonDamage:0}", 15, BioMonitorColors.Danger)));

        // Chemicals: every reagent in the blood, tagged poison / drug / medicine.
        _chems.RemoveAllChildren();
        if (state.Chemicals.Count == 0)
            _chems.AddChild(Text(Loc.GetString("bio-monitor-in-blood-none"), 14, BioMonitorColors.Dim));
        foreach (var chem in state.Chemicals)
        {
            var (tag, color) = chem.Kind switch
            {
                BioMonitorChemKind.Poison => ("bio-monitor-chem-poison", BioMonitorColors.Danger),
                BioMonitorChemKind.Drug => ("bio-monitor-chem-drug", BioMonitorColors.Warn),
                BioMonitorChemKind.Medicine => ("bio-monitor-chem-medicine", BioMonitorColors.Text),
                _ => ("bio-monitor-chem-other", BioMonitorColors.Dim),
            };
            var entry = HBox(6);
            entry.AddChild(Text($"[{Loc.GetString(tag)}]", 14, color));
            entry.AddChild(Text($"{chem.Name} {chem.Quantity:0.#}u", 14, color));
            _chems.AddChild(entry);
        }

        RefreshLimb();
    }

    private void RefreshLimb()
    {
        if (_state == null)
            return;

        var part = _doll.Selected;
        _limbName.Text = Loc.GetString($"body-part-name-{part.ToString().ToLowerInvariant()}");
        _limbDamage.RemoveAllChildren();

        if (!_parts.TryGetValue(part, out var limb) || limb.Missing)
        {
            _limbHealth.Text = Loc.GetString("bio-monitor-limb-missing");
            _limbBar.Value = 0;
            _embedCount.Text = string.Empty;
            _embedList.RemoveAllChildren();
            _listKey = string.Empty;
            return;
        }

        _limbHealth.Text = $"{limb.Health:0}%";
        _limbBar.Value = limb.Health;
        _limbBar.Color = BioMonitorColors.ForHealth(limb.Health);

        if (limb.Damage.Count == 0)
            _limbDamage.AddChild(Text(Loc.GetString("bio-monitor-no-damage"), 15, BioMonitorColors.Dim));
        foreach (var (type, amount) in limb.Damage.OrderByDescending(d => d.Value))
            _limbDamage.AddChild(Row(Text(type, 15), Text($"{amount:0.#}", 15)));

        if (limb.Fracture)
        {
            _limbDamage.AddChild(Text(Loc.GetString(limb.Splinted ? "bio-monitor-fracture-splinted" : "bio-monitor-fracture",
                    ("healed", $"{limb.FractureHealing:0}")),
                15, limb.Splinted ? BioMonitorColors.Dim : BioMonitorColors.Warn));
            var healing = new BioMonitorBar { Value = limb.FractureHealing, Color = BioMonitorColors.Dim };
            _limbDamage.AddChild(healing);
            _limbDamage.AddChild(Text(Loc.GetString("bio-monitor-fracture-time", ("minutes", limb.FractureMinutesLeft)), 13, BioMonitorColors.Dim));
            _limbDamage.AddChild(Text(Loc.GetString("bio-monitor-fracture-help"), 13, BioMonitorColors.Dim));
        }

        // Embedded objects: identical ones stack into one row.
        _embedCount.Text = limb.Embedded.Count > 0
            ? Loc.GetString("bio-monitor-embedded-count", ("count", limb.Embedded.Count))
            : string.Empty;

        // The server refreshes every second. Rebuilding the buttons each time can eat a click,
        // so only rebuild when the list (or what the viewer is holding) actually changed.
        var key = $"{part}|{_state.ViewerHasTool}|{_state.ViewerSkilled}|" + string.Join(",",
            limb.Embedded.Select(e => $"{e.Item.Id}:{e.Seconds}:{e.BleedRisk}:{e.NeedsTool}:{_pending.ContainsKey(e.Item)}"));
        if (key == _listKey)
            return;

        _listKey = key;
        _embedList.RemoveAllChildren();

        if (limb.Embedded.Count == 0)
        {
            _embedList.AddChild(Text(Loc.GetString("bio-monitor-embedded-none"), 15, BioMonitorColors.Dim));
            return;
        }

        foreach (var group in limb.Embedded.GroupBy(e => (e.Prototype, e.Name)))
        {
            var first = group.First();
            var count = group.Count();

            var row = new PanelContainer
            {
                PanelOverride = new StyleBoxFlat { BorderColor = BioMonitorColors.Border, BorderThickness = new Thickness(1) },
                Margin = new Thickness(0, 2),
            };
            var box = HBox(8);
            box.Margin = new Thickness(4);

            var icon = new EntityPrototypeView { SetSize = new Vector2(24, 24), Scale = new Vector2(0.75f, 0.75f) };
            if (first.Prototype != null)
                icon.SetPrototype(new EntProtoId(first.Prototype));
            box.AddChild(icon);

            var text = VBox();
            text.HorizontalExpand = true;
            text.AddChild(Text(count > 1 ? $"{first.Name} x{count}" : first.Name, 16, BioMonitorColors.Bright));
            var how = first.NeedsTool ? "bio-monitor-how-needs-tool"
                : _state.ViewerHasTool ? "bio-monitor-how-tool"
                : _state.ViewerSkilled && !first.Deep ? "bio-monitor-how-skilled"
                : "bio-monitor-how-hands";
            text.AddChild(Text(Loc.GetString("bio-monitor-embedded-time", ("how", Loc.GetString(how)), ("seconds", $"{first.Seconds:0.#}")),
                13, first.NeedsTool ? BioMonitorColors.Warn : BioMonitorColors.Dim));
            if (!first.NeedsTool)
            {
                var risk = first.BleedRisk <= 0f
                    ? Loc.GetString("bio-monitor-bleed-risk-none")
                    : Loc.GetString(first.HeavyBleed ? "bio-monitor-bleed-risk-heavy" : "bio-monitor-bleed-risk",
                        ("percent", $"{first.BleedRisk * 100f:0}"));
                text.AddChild(Text(risk, 13, first.BleedRisk <= 0f ? BioMonitorColors.Dim
                    : first.HeavyBleed ? BioMonitorColors.Danger : BioMonitorColors.Warn));
            }
            box.AddChild(text);

            // Press = instant feedback. The button stays "Removing..." until the item is gone,
            // or the time runs out (you moved, got hit, or it was cancelled), then it comes back.
            var busy = group.Any(e => _pending.ContainsKey(e.Item));
            var next = group.FirstOrDefault(e => !_pending.ContainsKey(e.Item)) ?? first;
            var button = new Button
            {
                Text = Loc.GetString(busy ? "bio-monitor-removing" : "bio-monitor-remove"),
                Disabled = busy,
                VerticalAlignment = VAlignment.Center,
            };
            var item = next.Item;
            var seconds = next.Seconds;
            button.OnPressed += _ =>
            {
                button.Text = Loc.GetString("bio-monitor-removing");
                button.Disabled = true;
                RequestRemove(item, seconds);
            };
            box.AddChild(button);

            // Hovering a row lights up its impact dots on the body.
            var ids = group.Select(e => e.Item).ToHashSet();
            row.MouseFilter = MouseFilterMode.Pass;
            row.OnMouseEntered += _ => _doll.Highlighted = ids;
            row.OnMouseExited += _ =>
            {
                if (_doll.Highlighted == ids)
                    _doll.Highlighted = new HashSet<NetEntity>();
            };

            row.AddChild(box);
            _embedList.AddChild(row);
        }
    }

    #region Little helpers

    private static Label Text(string text, int size = 16, Color? color = null)
    {
        return new Label
        {
            Text = text,
            FontColorOverride = color ?? BioMonitorColors.Text,
            ClipText = false,
        };
    }

    private static BoxContainer VBox(int sep = 0) =>
        new() { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = sep };

    private static BoxContainer HBox(int sep = 0) =>
        new() { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = sep };

    private static BoxContainer Row(Control left, Control right)
    {
        var row = HBox(6);
        left.HorizontalExpand = true;
        row.AddChild(left);
        row.AddChild(right);
        return row;
    }

    private static PanelContainer Panel(string title, Control body) =>
        Panel(Text(title, 17, BioMonitorColors.Bright), body);

    private static PanelContainer Panel(Control title, Control body)
    {
        var panel = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = BioMonitorColors.Background,
                BorderColor = BioMonitorColors.Border,
                BorderThickness = new Thickness(1),
            },
        };

        var stack = VBox();
        var titleBar = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat { BackgroundColor = BioMonitorColors.Header },
        };
        title.Margin = new Thickness(6, 2);
        titleBar.AddChild(title);
        stack.AddChild(titleBar);

        body.Margin = new Thickness(6, 4);
        stack.AddChild(body);
        body.VerticalExpand = true;

        panel.AddChild(stack);
        return panel;
    }

    #endregion
}
