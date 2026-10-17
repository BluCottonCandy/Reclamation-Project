using Content.Shared._ReclamationProject.Blindness;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Content.Shared.Eye.Blinding.Components;
using Content.Shared.GameTicking;
using Robust.Shared.Player;

namespace Content.Client.Eye.Blinding;

public sealed class BlindingSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IOverlayManager _overlayMan = default!;
    [Dependency] ILightManager _lightManager = default!;


    private BlindOverlay _overlay = default!;
    private bool _lightingChanged;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BlindableComponent, ComponentInit>(OnBlindInit);
        SubscribeLocalEvent<BlindableComponent, ComponentShutdown>(OnBlindShutdown);

        SubscribeLocalEvent<BlindableComponent, LocalPlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<BlindableComponent, LocalPlayerDetachedEvent>(OnPlayerDetached);

        SubscribeNetworkEvent<RoundRestartCleanupEvent>(RoundRestartCleanup);

        _overlay = new();
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        if (TryComp<BlindableComponent>(_player.LocalEntity, out var blind) && blind.IsBlind)
        {
            var hearing = HasComp<BlindConcentrationComponent>(_player.LocalEntity);
            // Auditory outlines ignore illumination, but must retain the engine's wall FOV.
            // Disabling the entire manager also disables that occlusion pass.
            _lightManager.Enabled = hearing;
            _lightManager.DrawLighting = !hearing;
            _lightingChanged = true;
        }
        else if (_lightingChanged)
        {
            RestoreLighting();
        }
    }

    private void RestoreLighting()
    {
        _lightManager.Enabled = true;
        _lightManager.DrawLighting = true;
        _lightingChanged = false;
    }

    private void OnPlayerAttached(EntityUid uid, BlindableComponent component, LocalPlayerAttachedEvent args)
    {
        _overlayMan.AddOverlay(_overlay);
    }

    private void OnPlayerDetached(EntityUid uid, BlindableComponent component, LocalPlayerDetachedEvent args)
    {
        _overlayMan.RemoveOverlay(_overlay);
        RestoreLighting();
    }

    private void OnBlindInit(EntityUid uid, BlindableComponent component, ComponentInit args)
    {
        if (_player.LocalEntity == uid)
            _overlayMan.AddOverlay(_overlay);
    }

    private void OnBlindShutdown(EntityUid uid, BlindableComponent component, ComponentShutdown args)
    {
        if (_player.LocalEntity == uid)
        {
            _overlayMan.RemoveOverlay(_overlay);
            RestoreLighting();
        }
    }

    private void RoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        RestoreLighting();
    }
}
