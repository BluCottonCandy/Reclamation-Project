using Content.Shared._ReclamationProject.Institute.Relay;
using Content.Shared.Mobs.Components;
using Robust.Client.GameObjects;
using Robust.Client.Player;

namespace Content.Client._ReclamationProject.Institute.Relay;

/// <summary>
/// While the local player rides a relay signal, hide every mob so only buildings and items show.
/// </summary>
public sealed class InstituteRelayVisionSystem : EntitySystem
{
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    // Only the sprites this system hid, so it never un-hides something another system hid.
    private readonly HashSet<EntityUid> _hidden = new();

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var local = _player.LocalEntity;
        var relaying = local != null && HasComp<InstituteRelayEyeComponent>(Transform(local.Value).ParentUid);

        if (!relaying)
        {
            RestoreAll();
            return;
        }

        var query = EntityQueryEnumerator<MobStateComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out _, out var sprite))
        {
            if (uid == local || !sprite.Visible)
                continue;

            _sprite.SetVisible((uid, sprite), false);
            _hidden.Add(uid);
        }
    }

    public override void Shutdown()
    {
        base.Shutdown();
        RestoreAll();
    }

    private void RestoreAll()
    {
        if (_hidden.Count == 0)
            return;

        foreach (var uid in _hidden)
        {
            if (TryComp<SpriteComponent>(uid, out var sprite))
                _sprite.SetVisible((uid, sprite), true);
        }

        _hidden.Clear();
    }
}
