using Content.Client.UserInterface.Controls;
using Content.Shared._Reclamation.Bunkers;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._Reclamation.Bunkers;

[UsedImplicitly]
public sealed class PersonalBunkerBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private PersonalBunkerWindow? _window;

    protected override void Open()
    {
        base.Open();
        var window = _window = this.CreateWindow<PersonalBunkerWindow>();
        window.Room.OnPressed += _ => SendMessage(new PersonalBunkerChoiceMessage(true));
        window.Cryo.OnPressed += _ => SendMessage(new PersonalBunkerChoiceMessage(false));
        window.RequestList += () => SendMessage(new ListBunkerVisitorsMessage());
        window.RequestAccess += map => SendMessage(new RequestBunkerVisitMessage(map));
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);
        if (message is BunkerVisitorListMessage list)
            _window?.ShowVisitors(list.Entries);
    }
}

public sealed class PersonalBunkerWindow : FancyWindow
{
    public readonly Button Room = new() { Text = "Enter my room" };
    public readonly Button Cryo = new() { Text = "Enter cryo" };
    public event Action? RequestList;
    public event Action<NetEntity>? RequestAccess;

    private readonly BoxContainer _menu = new() { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 8 };
    private readonly BoxContainer _visitors = new() { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 8, Visible = false };
    private readonly BoxContainer _rows = new() { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 4 };

    public PersonalBunkerWindow()
    {
        Title = "RobCo Bunker Access";
        _menu.AddChild(new Label { Text = "Personal quarters — saved on this server" });
        _menu.AddChild(new Label { Text = "Weapons left behind become decorations when the room is empty." });
        _menu.AddChild(new Label { Text = "Guns lose their ammunition and cannot fire. Melee deals 1 blunt." });
        _menu.AddChild(new Label { Text = "Your equipped and carried belongings are excluded." });
        _menu.AddChild(Room);
        _menu.AddChild(Cryo);
        var join = new Button { Text = Loc.GetString("bunker-visit-join") };
        join.OnPressed += _ =>
        {
            _menu.Visible = false;
            _visitors.Visible = true;
            _rows.RemoveAllChildren();
            _rows.AddChild(new Label { Text = Loc.GetString("bunker-visit-loading") });
            RequestList?.Invoke();
        };
        _menu.AddChild(join);

        var scroll = new ScrollContainer { MinHeight = 200, MinWidth = 350, HorizontalExpand = true };
        scroll.AddChild(_rows);
        _visitors.AddChild(scroll);
        var actions = new BoxContainer { SeparationOverride = 8 };
        var back = new Button { Text = Loc.GetString("bunker-visit-back") };
        back.OnPressed += _ =>
        {
            _visitors.Visible = false;
            _menu.Visible = true;
        };
        var refresh = new Button { Text = Loc.GetString("bunker-visit-refresh") };
        refresh.OnPressed += _ => RequestList?.Invoke();
        actions.AddChild(back);
        actions.AddChild(refresh);
        _visitors.AddChild(actions);
        ContentsContainer.AddChild(_menu);
        ContentsContainer.AddChild(_visitors);
    }

    public void ShowVisitors(BunkerVisitorEntry[] entries)
    {
        _rows.RemoveAllChildren();
        if (entries.Length == 0)
            _rows.AddChild(new Label { Text = Loc.GetString("bunker-visit-empty") });

        foreach (var entry in entries)
        {
            var row = new BoxContainer { SeparationOverride = 12 };
            row.AddChild(new Label { Text = entry.Owner, HorizontalExpand = true });
            var request = new Button
            {
                Text = Loc.GetString(entry.Available ? "bunker-visit-request" : "bunker-visit-owner-unavailable"),
                Disabled = !entry.Available
            };
            request.OnPressed += _ => RequestAccess?.Invoke(entry.Map);
            row.AddChild(request);
            _rows.AddChild(row);
        }
    }
}
