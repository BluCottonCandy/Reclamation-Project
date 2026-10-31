using Content.Client.Actions;
using Content.Client.UserInterface.Systems.Actions;
using Content.Shared._ReclamationProject.Intelligence;
using Content.Shared.Hands.Components;
using Content.Shared.Mobs.Systems;
using Robust.Client.GameObjects;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._ReclamationProject.Intelligence;

/// <summary>A cosmetic sprite layer only: no item, weapon, spell or occupied hand is created.</summary>
public sealed class OverrideVisualSystem : EntitySystem
{
    private const string Layer = "ReclamationOverrideGrasp";
    private static readonly ResPath Rsi = new("/Textures/_ReclamationProject/Effects/override_grasp.rsi");
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IUserInterfaceManager _ui = default!;
    [Dependency] private readonly ActionsSystem _actions = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MobStateSystem _mob = default!;
    private bool _selected;
    private EntityUid? _player;
    private readonly Dictionary<EntityUid, HandLocation> _shownHands = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<IntelligenceOverrideComponent, ComponentShutdown>(OnShutdown);
    }

    public override void FrameUpdate(float frameTime)
    {
        var player = _players.LocalEntity;
        var selectedAction = _ui.GetUIController<ActionUIController>().SelectingTargetFor;
        var selected = player != null && selectedAction != null &&
            TryComp<IntelligenceOverrideComponent>(player, out var state) && state.Action == selectedAction &&
            _mob.IsAlive(player.Value) && state.NextUseAt <= _timing.CurTime &&
            _actions.TryGetActionData(selectedAction, out var action) && action.Enabled &&
            action.BaseEvent is IntelligenceOverrideActionEvent;
        if (_selected != selected || _player != player)
        {
            _selected = selected;
            _player = player;
            if (player != null)
                RaiseNetworkEvent(new OverrideVisualSelectionEvent(selected));
        }

        var query = EntityQueryEnumerator<IntelligenceOverrideComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out var ability, out var sprite))
        {
            var active = uid == player ? selected : ability.Selected;
            var hand = CompOrNull<HandsComponent>(uid)?.ActiveHand;
            var exists = sprite.LayerMapTryGet(Layer, out var layer);
            if (!active || hand == null)
            {
                if (exists)
                    sprite.LayerSetVisible(layer, false);
                _shownHands.Remove(uid);
                continue;
            }
            if (!exists)
            {
                layer = sprite.LayerMapReserveBlank(Layer);
                sprite.LayerSetRSI(layer, Rsi);
                sprite.LayerSetShader(layer, "unshaded");
            }
            if (!exists || !_shownHands.TryGetValue(uid, out var shownHand) || shownHand != hand.Location)
            {
                sprite.LayerSetState(layer, hand.Location == HandLocation.Left ? "inhand-left" : "inhand-right");
                _shownHands[uid] = hand.Location;
            }
            sprite.LayerSetVisible(layer, true);
        }
    }

    private void OnShutdown(EntityUid uid, IntelligenceOverrideComponent ability, ComponentShutdown args)
    {
        _shownHands.Remove(uid);
        if (!TerminatingOrDeleted(uid) && TryComp<SpriteComponent>(uid, out var sprite) && sprite.LayerMapTryGet(Layer, out var layer))
            sprite.LayerSetVisible(layer, false);
    }
}
