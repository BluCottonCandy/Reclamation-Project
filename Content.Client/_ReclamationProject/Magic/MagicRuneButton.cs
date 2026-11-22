using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Input;

namespace Content.Client._ReclamationProject.Magic;

/// <summary>
/// #Reclamation Project - one clickable rune on the grimoire's rune palette. Shows only the rune's shape
/// (match it to the Codex), never the letter.
/// </summary>
public sealed class MagicRuneButton : Control
{
    public event Action<char>? OnPressed;
    public readonly char Letter;

    public MagicRuneButton(char letter)
    {
        Letter = letter;
        MouseFilter = MouseFilterMode.Stop;
        MinSize = new Vector2(20, 22);
        MaxSize = new Vector2(20, 22);
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if (args.Function != EngineKeyFunctions.UIClick)
            return;
        OnPressed?.Invoke(Letter);
        args.Handle();
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        var s = UIScale;
        var hovered = UserInterfaceManager.CurrentlyHovered == this;
        if (PixelSize.X < 4 * s || PixelSize.Y < 4 * s)
            return;
        var box = new UIBox2(1 * s, 1 * s, PixelSize.X - 1 * s, PixelSize.Y - 1 * s);
        if (hovered)
            handle.DrawRect(box, Color.FromHex("#d8c69a"));
        handle.DrawRect(box, Color.FromHex("#c9b07e"), false);

        var scale = 15f / 18f * s;
        var origin = new Vector2((PixelSize.X - 12f * scale) / 2f, (PixelSize.Y - 18f * scale) / 2f);
        var ink = hovered ? Color.FromHex("#7a1f1f") : MagicGrimoireWindow.Ink;
        MagicGlyphControl.DrawRune(handle, Letter, origin, 18f * scale, ink);
    }
}
