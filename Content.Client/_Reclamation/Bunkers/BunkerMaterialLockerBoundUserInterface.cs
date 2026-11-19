using System.Numerics;
using Content.Client.Materials.UI;
using Content.Client.UserInterface.Controls;
using Content.Shared._Reclamation.Bunkers;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._Reclamation.Bunkers;

public sealed class BunkerMaterialLockerBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    protected override void Open()
    {
        base.Open();
        var window = this.CreateWindow<FancyWindow>();
        window.Title = Loc.GetString("bunker-material-locker-title");
        window.MinSize = new Vector2(440, 300);
        var column = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        var materials = new MaterialStorageControl();
        materials.SetOwner(Owner);
        column.AddChild(materials);
        var drawer = new Button { Text = Loc.GetString("bunker-material-locker-drawer") };
        drawer.OnPressed += _ => SendMessage(new OpenBunkerSupplyDrawerMessage());
        column.AddChild(drawer);
        window.ContentsContainer.AddChild(column);
    }
}
