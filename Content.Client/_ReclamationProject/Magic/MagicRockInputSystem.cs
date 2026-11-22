using Content.Client.UserInterface.Systems.Actions;
using Content.Shared._ReclamationProject.Magic;
using Content.Shared.CombatMode;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Shared.Input;
using Robust.Shared.Map;

namespace Content.Client._ReclamationProject.Magic;

/// <summary>
/// #Reclamation Project - Rock Bullet: a left click in combat mode, anywhere on screen, fires the circling rocks
/// at the mouse. (A normal left-click attack only gets sent when the mouse is within melee reach.)
/// </summary>
public sealed class MagicRockInputSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IEyeManager _eye = default!;
    [Dependency] private readonly IInputManager _input = default!;
    [Dependency] private readonly InputSystem _inputSystem = default!;
    [Dependency] private readonly IUserInterfaceManager _ui = default!;
    [Dependency] private readonly SharedCombatModeSystem _combat = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private bool _wasDown;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var down = _inputSystem.CmdStates.GetState(EngineKeyFunctions.Use) == BoundKeyState.Down;
        var pressed = down && !_wasDown;
        _wasDown = down;
        if (!pressed)
            return;

        if (_player.LocalEntity is not { } me || !_combat.IsInCombatMode(me))
            return;
        // Something to throw: circling rocks (Rock Bullet) or caught bullets (Bullet Catch).
        var rocks = TryComp<MagicStoredRocksComponent>(me, out var stored) && stored.Count > 0;
        var bullets = TryComp<MagicBulletCatcherComponent>(me, out var catcher) && catcher.Count > 0;
        if (!rocks && !bullets)
            return;

        // Aiming a spell: that click is for the spell, not the rocks.
        if (_ui.GetUIController<ActionUIController>().SelectingTargetFor != null)
            return;

        var mouse = _eye.PixelToMap(_input.MouseScreenPosition);
        if (mouse.MapId == MapId.Nullspace)
            return;
        var coords = _transform.ToCoordinates(mouse);
        RaiseNetworkEvent(new MagicFireRocksEvent(GetNetCoordinates(coords)));
    }
}
