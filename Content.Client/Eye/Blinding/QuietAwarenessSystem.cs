using Content.Shared._ReclamationProject.Blindness;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client.Eye.Blinding;

/// <summary>Removes quiet characters from the scene before auditory outlines are generated.</summary>
public sealed class QuietAwarenessSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly SpriteSystem _sprites = default!;
    [Dependency] private readonly BlindAwarenessSystem _awareness = default!;
    private const string ShaderId = "ReclamationQuietAwareness";
    private readonly HashSet<EntityUid> _masked = new();
    private ShaderInstance _shader = default!;
    private ShaderInstance _echoShader = default!;
    private readonly Dictionary<EntityUid, TimeSpan> _echoes = new();
    [Dependency] private readonly SharedTransformSystem _transforms = default!;

    public override void Initialize()
    {
        base.Initialize();
        _shader = _prototypes.Index<ShaderPrototype>(ShaderId).InstanceUnique();
        _echoShader = _shader.Duplicate();
        _shader.SetParameter("Echo", 0f);
        _echoShader.SetParameter("Echo", 1f);
        SubscribeLocalEvent<QuietAwarenessComponent, BeforePostShaderRenderEvent>(OnRender);
        SubscribeLocalEvent<QuietShotEchoComponent, BeforePostShaderRenderEvent>(OnEchoRender);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        var observer = _player.LocalSession?.AttachedEntity;
        var enabled = observer != null && TryComp<BlindConcentrationComponent>(observer, out var focus)
            && _awareness.GetVisualProgress(observer.Value, focus, _timing.CurTime) > 0f;
        foreach (var (echo, expires) in _echoes.ToArray())
        {
            if (Deleted(echo) || _timing.CurTime >= expires)
            {
                if (!Deleted(echo))
                    QueueDel(echo);
                _echoes.Remove(echo);
                continue;
            }
            if (TryComp<SpriteComponent>(echo, out var echoSprite))
                echoSprite.Visible = enabled;
        }
        // Remove our own shader only, preserving other visibility and disguise effects.
        foreach (var uid in _masked)
        {
            if ((!enabled || !HasComp<QuietAwarenessComponent>(uid) || uid == observer)
                && TryComp<SpriteComponent>(uid, out var sprite))
                _sprites.RemovePostShader((uid, sprite), ShaderId);
        }
        _masked.RemoveWhere(uid => !enabled || !HasComp<QuietAwarenessComponent>(uid) || uid == observer);
        if (!enabled)
            return;
        var query = EntityQueryEnumerator<QuietAwarenessComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out var quiet, out var sprite))
        {
            if (quiet.LastShotAt != quiet.LastRenderedShot)
            {
                quiet.LastRenderedShot = quiet.LastShotAt;
                if (uid != observer && quiet.LastShotAt + TimeSpan.FromSeconds(QuietAwarenessComponent.EchoSeconds) > _timing.CurTime)
                {
                    var echo = Spawn(null, quiet.LastShotCoordinates);
                    var echoSprite = EnsureComp<SpriteComponent>(echo);
                    _sprites.CopySprite((uid, sprite), (echo, echoSprite));
                    _sprites.ClearPostShaders((echo, echoSprite));
                    _transforms.SetWorldRotation(echo, quiet.LastShotRotation);
                    EnsureComp<QuietShotEchoComponent>(echo);
                    _sprites.SetPostShader((echo, echoSprite), new SpriteComponent.PostShaderArgs(ShaderId, _echoShader)
                    {
                        RaiseShaderEvent = true,
                    });
                    _echoes.Add(echo, quiet.LastShotAt + TimeSpan.FromSeconds(QuietAwarenessComponent.EchoSeconds));
                }
            }
            if (uid == observer || !_masked.Add(uid))
                continue;
            _sprites.SetPostShader((uid, sprite), new SpriteComponent.PostShaderArgs(ShaderId, _shader)
            {
                RaiseShaderEvent = true,
            });
        }
    }

    private void OnRender(EntityUid uid, QuietAwarenessComponent component, ref BeforePostShaderRenderEvent args)
    {
        SetShaderParameters(ref args);
    }

    private void OnEchoRender(EntityUid uid, QuietShotEchoComponent component, ref BeforePostShaderRenderEvent args)
        => SetShaderParameters(ref args);

    private void SetShaderParameters(ref BeforePostShaderRenderEvent args)
    {
        if (args.Id != ShaderId)
            return;
        var observer = _player.LocalSession?.AttachedEntity;
        var zoom = TryComp<EyeComponent>(observer, out var eye) ? eye.Zoom.X : 1f;
        args.Shader.SetParameter("Zoom", zoom);
        args.Shader.SetParameter("ScreenSize", new System.Numerics.Vector2(args.Viewport.Size.X, args.Viewport.Size.Y));
    }
}

/// <summary>A client-only snapshot of a loud gunshot, never an interactive character.</summary>
[RegisterComponent]
public sealed partial class QuietShotEchoComponent : Component
{
}
