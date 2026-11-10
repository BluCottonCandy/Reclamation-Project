using System.Linq;
using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared._ReclamationProject.Institute.Uplink;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._ReclamationProject.Institute.Uplink;

/// <summary>
/// #Reclamation Project - the uplink PIN prompt and the Institute network window.
/// Both are plain code-built windows driven by network events.
/// </summary>
public sealed class InstituteUplinkUiSystem : EntitySystem
{
    private FancyWindow? _pinWindow;
    private FancyWindow? _network;
    private InstituteNetworkStateEvent? _state;

    // Assign form selections survive a refresh.
    private NetEntity? _assignTarget;
    private InstituteObjectiveKind _assignKind = InstituteObjectiveKind.Steal;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<InstitutePinRequestEvent>(OnPinRequest);
        SubscribeNetworkEvent<InstituteNetworkStateEvent>(OnNetworkState);
    }

    public override void Shutdown()
    {
        _pinWindow?.Dispose();
        _network?.Dispose();
        base.Shutdown();
    }

    #region PIN

    private void OnPinRequest(InstitutePinRequestEvent msg, EntitySessionEventArgs args)
    {
        _pinWindow?.Dispose();
        _pinWindow = new FancyWindow
        {
            Title = Loc.GetString(msg.Tablet ? "institute-tablet-pin-title" : "institute-uplink-pin-title"),
            MinWidth = 320,
        };

        var box = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, Margin = new Thickness(8) };
        box.AddChild(new Label { Text = Loc.GetString(msg.Tablet ? "institute-tablet-pin-prompt" : "institute-uplink-pin-prompt") });

        if (msg.Error != null)
            box.AddChild(new Label { Text = msg.Error, FontColorOverride = Color.OrangeRed });

        var input = new LineEdit { PlaceHolder = "0000", HorizontalExpand = true };
        input.OnTextChanged += a =>
        {
            var digits = new string(a.Text.Where(char.IsDigit).Take(4).ToArray());
            if (digits != a.Text)
                input.SetText(digits, false);
        };
        box.AddChild(input);

        var submit = new Button { Text = Loc.GetString("institute-uplink-pin-submit") };
        void Send()
        {
            RaiseNetworkEvent(new InstitutePinSubmitEvent { Implant = msg.Implant, Pin = input.Text });
            _pinWindow?.Close();
        }
        submit.OnPressed += _ => Send();
        input.OnTextEntered += _ => Send();
        box.AddChild(submit);

        _pinWindow.ContentsContainer.AddChild(box);
        _pinWindow.OnClose += () =>
        {
            _pinWindow?.Dispose();
            _pinWindow = null;
        };
        _pinWindow.OpenCentered();
        input.GrabKeyboardFocus();
    }

    #endregion

    #region Network window

    private void OnNetworkState(InstituteNetworkStateEvent msg, EntitySessionEventArgs args)
    {
        _state = msg;
        if (_network is { Disposed: false })
        {
            Rebuild();
            return;
        }

        _network = new FancyWindow
        {
            Title = Loc.GetString("institute-network-title"),
            MinSize = new Vector2(560, 520),
        };
        _network.OnClose += () =>
        {
            _network?.Dispose();
            _network = null;
        };
        Rebuild();
        _network.OpenCentered();
    }

    private void Rebuild()
    {
        if (_network == null || _state == null)
            return;

        var state = _state;
        _network.ContentsContainer.RemoveAllChildren();

        var scroll = new ScrollContainer { HScrollEnabled = false, VerticalExpand = true, HorizontalExpand = true };
        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Margin = new Thickness(8),
            SeparationOverride = 6,
            HorizontalExpand = true,
        };
        scroll.AddChild(root);
        _network.ContentsContainer.AddChild(scroll);

        root.AddChild(new Label
        {
            Text = Loc.GetString("institute-network-balance", ("amount", state.Balance)),
            StyleClasses = { "LabelHeading" },
        });

        if (state.Status != null)
            root.AddChild(new Label { Text = state.Status, FontColorOverride = Color.FromHex("#4fd2e0") });

        var refresh = new Button { Text = Loc.GetString("institute-network-refresh"), HorizontalAlignment = Control.HAlignment.Left };
        refresh.OnPressed += _ => RaiseNetworkEvent(new InstituteNetworkRequestEvent());
        root.AddChild(refresh);

        BuildSendPoints(root, state);

        if (state.IsCommand)
        {
            BuildMembers(root, state);
            BuildAssign(root, state);
        }
    }

    private void BuildSendPoints(BoxContainer root, InstituteNetworkStateEvent state)
    {
        root.AddChild(Heading(Loc.GetString("institute-network-send-heading")));

        var others = state.Members.Where(m => m.Mind != state.Self).ToList();
        if (others.Count == 0)
        {
            root.AddChild(new Label { Text = Loc.GetString("institute-network-nobody") });
            return;
        }

        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
        var who = new OptionButton { HorizontalExpand = true };
        for (var i = 0; i < others.Count; i++)
        {
            who.AddItem($"{others[i].Name} ({others[i].Job})", i);
        }
        who.OnItemSelected += a => who.SelectId(a.Id);

        var amount = new LineEdit { PlaceHolder = Loc.GetString("institute-network-amount"), MinWidth = 80 };
        var send = new Button { Text = Loc.GetString("institute-network-send") };
        send.OnPressed += _ =>
        {
            if (!int.TryParse(amount.Text, out var value) || value <= 0)
                return;
            RaiseNetworkEvent(new InstituteSendPointsEvent { Target = others[who.SelectedId].Mind, Amount = value });
        };

        row.AddChild(who);
        row.AddChild(amount);
        row.AddChild(send);
        root.AddChild(row);
    }

    private void BuildMembers(BoxContainer root, InstituteNetworkStateEvent state)
    {
        root.AddChild(Heading(Loc.GetString("institute-network-members-heading")));

        var resetAll = new Button
        {
            Text = Loc.GetString("institute-network-reset-all"),
            HorizontalAlignment = Control.HAlignment.Left,
        };
        resetAll.OnPressed += _ => RaiseNetworkEvent(new InstituteResetPinEvent { Target = null });
        root.AddChild(resetAll);

        if (!state.Members.Any(m => m.CanManage))
            root.AddChild(new Label { Text = Loc.GetString("institute-network-nobody-managed"), FontColorOverride = Color.Gray });

        foreach (var member in state.Members.Where(m => m.CanManage))
        {
            var header = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
            header.AddChild(new Label { Text = $"{member.Name} ({member.Job})", HorizontalExpand = true });
            var mind = member.Mind;
            header.AddChild(new Label { Text = Loc.GetString("institute-network-pin-current", ("pin", member.Pin ?? "?")) });
            var newPin = new LineEdit { PlaceHolder = "0000", MinWidth = 60 };
            newPin.OnTextChanged += a =>
            {
                var digits = new string(a.Text.Where(char.IsDigit).Take(4).ToArray());
                if (digits != a.Text)
                    newPin.SetText(digits, false);
            };
            header.AddChild(newPin);
            var set = new Button { Text = Loc.GetString("institute-network-set-pin") };
            set.OnPressed += _ => RaiseNetworkEvent(new InstituteSetPinEvent { Target = mind, Pin = newPin.Text });
            header.AddChild(set);
            var reset = new Button { Text = Loc.GetString("institute-network-reset-pin") };
            reset.OnPressed += _ => RaiseNetworkEvent(new InstituteResetPinEvent { Target = mind });
            header.AddChild(reset);
            root.AddChild(header);

            if (member.Objectives.Count == 0)
            {
                root.AddChild(new Label
                {
                    Text = Loc.GetString("institute-network-no-objectives"),
                    Margin = new Thickness(16, 0, 0, 0),
                    FontColorOverride = Color.Gray,
                });
                continue;
            }

            foreach (var objective in member.Objectives)
            {
                var line = new BoxContainer
                {
                    Orientation = BoxContainer.LayoutOrientation.Horizontal,
                    SeparationOverride = 4,
                    Margin = new Thickness(16, 0, 0, 0),
                };
                var status = objective.Paid
                    ? Loc.GetString("institute-network-status-paid")
                    : objective.Done
                        ? Loc.GetString("institute-network-status-done")
                        : Loc.GetString("institute-network-status-open");
                line.AddChild(new Label
                {
                    Text = Loc.GetString("institute-network-objective-line",
                        ("title", objective.Title), ("reward", objective.Reward), ("status", status)),
                    HorizontalExpand = true,
                    ClipText = true,
                });

                var id = objective.Objective;
                if (!objective.Paid)
                {
                    var force = new Button { Text = Loc.GetString("institute-network-force") };
                    force.OnPressed += _ => RaiseNetworkEvent(new InstituteObjectiveActionEvent { Objective = id, Remove = false });
                    line.AddChild(force);
                }

                var remove = new Button { Text = Loc.GetString("institute-network-remove") };
                remove.OnPressed += _ => RaiseNetworkEvent(new InstituteObjectiveActionEvent { Objective = id, Remove = true });
                line.AddChild(remove);
                root.AddChild(line);
            }
        }
    }

    private void BuildAssign(BoxContainer root, InstituteNetworkStateEvent state)
    {
        root.AddChild(Heading(Loc.GetString("institute-network-assign-heading")));

        var managed = state.Members.Where(m => m.CanManage).ToList();
        if (managed.Count == 0)
        {
            root.AddChild(new Label { Text = Loc.GetString("institute-network-nobody-managed"), FontColorOverride = Color.Gray });
            return;
        }

        // Who gets the objective.
        var who = new OptionButton { HorizontalExpand = true };
        var whoIndex = Math.Max(0, managed.FindIndex(m => m.Mind == _assignTarget));
        for (var i = 0; i < managed.Count; i++)
        {
            who.AddItem($"{managed[i].Name} ({managed[i].Job})", i);
        }
        who.SelectId(whoIndex);
        _assignTarget = managed[whoIndex].Mind;
        who.OnItemSelected += a =>
        {
            who.SelectId(a.Id);
            _assignTarget = managed[a.Id].Mind;
        };
        root.AddChild(Row(Loc.GetString("institute-network-assign-to"), who));

        // What kind.
        var kinds = Enum.GetValues<InstituteObjectiveKind>();
        var kind = new OptionButton { HorizontalExpand = true };
        foreach (var k in kinds)
        {
            kind.AddItem(Loc.GetString($"institute-objective-kind-{k.ToString().ToLowerInvariant()}"), (int) k);
        }
        kind.SelectId((int) _assignKind);
        root.AddChild(Row(Loc.GetString("institute-network-assign-kind"), kind));

        // Kind-specific fields. Rebuilt when the kind changes.
        var fields = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 4 };
        root.AddChild(fields);

        OptionButton? person = null;
        OptionButton? item = null;
        LineEdit? title = null;
        LineEdit? text = null;

        void BuildFields()
        {
            fields.RemoveAllChildren();
            person = null;
            item = null;
            title = null;
            text = null;

            switch (_assignKind)
            {
                case InstituteObjectiveKind.Kill:
                case InstituteObjectiveKind.Protect:
                    var p = new OptionButton { HorizontalExpand = true };
                    for (var i = 0; i < state.People.Count; i++)
                    {
                        p.AddItem(state.People[i].Name, i);
                    }
                    p.OnItemSelected += a => p.SelectId(a.Id);
                    person = p;
                    fields.AddChild(Row(Loc.GetString("institute-network-assign-person"), p));
                    break;
                case InstituteObjectiveKind.Steal:
                    var it = new OptionButton { HorizontalExpand = true };
                    for (var i = 0; i < state.StealItems.Count; i++)
                    {
                        it.AddItem(state.StealItems[i].Name, i);
                    }
                    it.OnItemSelected += a => it.SelectId(a.Id);
                    item = it;
                    fields.AddChild(Row(Loc.GetString("institute-network-assign-item"), it));
                    break;
                case InstituteObjectiveKind.Custom:
                    title = new LineEdit { HorizontalExpand = true, PlaceHolder = Loc.GetString("institute-network-assign-title-hint") };
                    text = new LineEdit { HorizontalExpand = true, PlaceHolder = Loc.GetString("institute-network-assign-text-hint") };
                    fields.AddChild(Row(Loc.GetString("institute-network-assign-title"), title));
                    fields.AddChild(Row(Loc.GetString("institute-network-assign-text"), text));
                    break;
            }
        }

        kind.OnItemSelected += a =>
        {
            kind.SelectId(a.Id);
            _assignKind = (InstituteObjectiveKind) a.Id;
            BuildFields();
        };
        BuildFields();

        var reward = new LineEdit { Text = "20", MinWidth = 80 };
        root.AddChild(Row(Loc.GetString("institute-network-assign-reward"), reward));

        var give = new Button { Text = Loc.GetString("institute-network-assign-give"), HorizontalAlignment = Control.HAlignment.Left };
        give.OnPressed += _ =>
        {
            if (_assignTarget is not { } target)
                return;

            int.TryParse(reward.Text, out var points);
            var request = new InstituteAssignObjectiveEvent
            {
                Target = target,
                Kind = _assignKind,
                Reward = points,
            };
            if (person != null && state.People.Count > 0)
                request.Person = state.People[person.SelectedId].Mind;
            if (item != null && state.StealItems.Count > 0)
                request.Item = state.StealItems[item.SelectedId].Prototype;
            if (title != null)
                request.Title = title.Text;
            if (text != null)
                request.Text = text.Text;

            RaiseNetworkEvent(request);
        };
        root.AddChild(give);
    }

    private static Label Heading(string text)
    {
        return new Label { Text = text, StyleClasses = { "LabelHeading" }, Margin = new Thickness(0, 8, 0, 0) };
    }

    private static BoxContainer Row(string label, Control control)
    {
        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
        row.AddChild(new Label { Text = label, MinWidth = 110 });
        row.AddChild(control);
        return row;
    }

    #endregion
}
