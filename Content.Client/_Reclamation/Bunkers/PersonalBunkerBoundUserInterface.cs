using Content.Client.UserInterface.Controls;
using Content.Shared._Reclamation.Bunkers;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._Reclamation.Bunkers;

[UsedImplicitly]
public sealed class PersonalBunkerBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    protected override void Open()
    {
        base.Open();
        var window = this.CreateWindow<PersonalBunkerWindow>();
        window.Room.OnPressed += _ => SendMessage(new PersonalBunkerChoiceMessage(true));
        window.Cryo.OnPressed += _ => SendMessage(new PersonalBunkerChoiceMessage(false));
    }
}

public sealed class PersonalBunkerWindow : FancyWindow
{
    public readonly Button Room = new() { Text = "Enter my room" };
    public readonly Button Cryo = new() { Text = "Enter cryo" };

    public PersonalBunkerWindow()
    {
        Title = "RobCo Bunker Access";
        var box = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 8 };
        box.AddChild(new Label { Text = "Personal quarters — saved on this server" });
        box.AddChild(new Label { Text = "Weapons left behind become decorations when the room is empty." });
        box.AddChild(new Label { Text = "Guns lose their ammunition and cannot fire. Melee deals 1 blunt." });
        box.AddChild(new Label { Text = "Your equipped and carried belongings are excluded." });
        box.AddChild(Room);
        box.AddChild(Cryo);
        ContentsContainer.AddChild(box);
    }
}
