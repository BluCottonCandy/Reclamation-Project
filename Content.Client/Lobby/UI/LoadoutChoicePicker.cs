using System;
using System.Collections.Generic;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.Lobby.UI;

/// <summary>A collapsible choice list that shows only the selected option until it is deselected.</summary>
public sealed class LoadoutChoicePicker : BoxContainer
{
    public Button ToggleButton { get; }
    public BoxContainer Choices { get; } = new() { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true };
    private readonly List<(Control Control, Func<bool> Selected, Func<bool> Available)> _choices = new();
    private bool _expanded;
    public bool Expanded
    {
        get => _expanded;
        set { _expanded = value; Refresh(); }
    }

    public LoadoutChoicePicker(EntityUid icon, string title)
    {
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        ToggleButton = new Button { HorizontalExpand = false, HorizontalAlignment = HAlignment.Left };
        var sprite = new SpriteView { MinSize = new System.Numerics.Vector2(32, 32) };
        sprite.SetEntity(icon);
        ToggleButton.AddChild(new BoxContainer
        {
            Children = { sprite, new Label { Text = title, Margin = new Thickness(8, 0, 0, 0) } }
        });
        ToggleButton.OnPressed += _ => Expanded = !Expanded;
        AddChild(ToggleButton);
        AddChild(Choices);
        Refresh();
    }

    public void AddChoice(Control choice, Func<bool> selected, Func<bool> available)
    {
        _choices.Add((choice, selected, available));
        Choices.AddChild(choice);
        Refresh();
    }

    public void Refresh()
    {
        Control? selected = null;
        foreach (var choice in _choices)
            if (choice.Selected()) { selected = choice.Control; break; }
        foreach (var choice in _choices)
            choice.Control.Visible = selected != null ? choice.Control == selected : choice.Available();
        Choices.Visible = _expanded;
    }
}
