using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared._ReclamationProject.Lockpick;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._ReclamationProject.Lockpick;

/// <summary>#Reclamation Project - lockpicking window: tension slider, release button, picks left, and the lock itself.</summary>
public sealed class LockpickWindow : FancyWindow
{
    public readonly LockpickCanvas Canvas = new();
    private readonly Slider _tension;
    private readonly Label _tensionLabel = new() { MinWidth = 80 };
    private readonly Label _picks = new();
    private readonly Label _message = new();
    private readonly ProgressBar _strain;

    public event Action? OnReset;

    public LockpickWindow()
    {
        Title = Loc.GetString("lockpick-window-title");
        MinSize = new Vector2(680, 420);

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Margin = new Thickness(8),
            SeparationOverride = 6,
        };

        var tensionRow = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 8 };
        tensionRow.AddChild(new Label { Text = Loc.GetString("lockpick-tension"), MinWidth = 70 });
        _tension = new Slider { MinValue = 0, MaxValue = 100, Value = 50, MinWidth = 220, VerticalAlignment = VAlignment.Center };
        _tension.OnValueChanged += _ => UpdateTension();
        tensionRow.AddChild(_tension);
        tensionRow.AddChild(_tensionLabel);
        var release = new Button { Text = Loc.GetString("lockpick-release") };
        release.OnPressed += _ => OnReset?.Invoke();
        tensionRow.AddChild(release);
        root.AddChild(tensionRow);

        root.AddChild(Canvas);

        var strainRow = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 8 };
        strainRow.AddChild(new Label { Text = Loc.GetString("lockpick-strain") });
        _strain = new ProgressBar { MinValue = 0, MaxValue = 100, MinWidth = 160, MinHeight = 10, VerticalAlignment = VAlignment.Center };
        strainRow.AddChild(_strain);
        strainRow.AddChild(_picks);
        root.AddChild(strainRow);

        _message.Text = Loc.GetString("lockpick-help");
        root.AddChild(_message);
        root.AddChild(new Label { Text = Loc.GetString("lockpick-legend"), FontColorOverride = Color.Gray });

        ContentsContainer.AddChild(root);
        Canvas.TensionSource = () => _tension.Value;
        Canvas.OnStrainChanged += v => _strain.Value = v;
        UpdateTension();
    }

    private void UpdateTension()
    {
        var tension = LockpickCanvas.TensionOf(_tension.Value);
        _tensionLabel.Text = Loc.GetString(tension switch
        {
            LockpickTension.Light => "lockpick-tension-light",
            LockpickTension.Heavy => "lockpick-tension-heavy",
            _ => "lockpick-tension-right",
        });
        _tensionLabel.FontColorOverride = tension == LockpickTension.Right ? Color.FromHex("#5fbf7f") : Color.FromHex("#e0a040");
    }

    public void UpdateState(LockpickBuiState state)
    {
        Canvas.UpdateState(state);
        _picks.Text = Loc.GetString("lockpick-picks-left", ("picks", state.Picks));
        if (state.Message != null)
            _message.Text = state.Message;
    }
}
