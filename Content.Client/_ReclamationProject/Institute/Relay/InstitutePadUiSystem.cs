using Content.Client.UserInterface.Controls;
using Content.Shared._ReclamationProject.Institute.Relay;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._ReclamationProject.Institute.Relay;

/// <summary>
/// #Reclamation Project - the "Relay to pad" picker: a drop-down of every Institute relay pad and a Relay button.
/// </summary>
public sealed class InstitutePadUiSystem : EntitySystem
{
    private FancyWindow? _window;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<InstitutePadListEvent>(OnList);
    }

    public override void Shutdown()
    {
        _window?.Dispose();
        base.Shutdown();
    }

    private void OnList(InstitutePadListEvent msg, EntitySessionEventArgs args)
    {
        _window?.Dispose();
        _window = new FancyWindow
        {
            Title = Loc.GetString("institute-pad-window-title"),
            MinWidth = 380,
        };

        var box = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Margin = new Thickness(8),
            SeparationOverride = 6,
        };

        if (msg.Status != null)
            box.AddChild(new Label { Text = msg.Status, FontColorOverride = Color.OrangeRed });

        if (msg.Pads.Count == 0)
        {
            box.AddChild(new Label { Text = Loc.GetString("institute-pad-none") });
        }
        else
        {
            box.AddChild(new Label { Text = Loc.GetString("institute-pad-pick") });

            var pads = new OptionButton { HorizontalExpand = true };
            for (var i = 0; i < msg.Pads.Count; i++)
            {
                pads.AddItem($"{msg.Pads[i].Name} - {msg.Pads[i].Where}", i);
            }
            pads.OnItemSelected += a => pads.SelectId(a.Id);
            box.AddChild(pads);

            var relay = new Button { Text = Loc.GetString("institute-pad-relay"), HorizontalAlignment = Control.HAlignment.Right };
            relay.OnPressed += _ =>
            {
                RaiseNetworkEvent(new InstitutePadRelayRequestEvent { Pad = msg.Pads[pads.SelectedId].Pad });
                _window?.Close();
            };
            box.AddChild(relay);
        }

        _window.ContentsContainer.AddChild(box);
        _window.OnClose += () =>
        {
            _window?.Dispose();
            _window = null;
        };
        _window.OpenCentered();
    }
}
