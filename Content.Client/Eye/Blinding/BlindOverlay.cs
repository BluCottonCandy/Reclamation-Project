using Content.Shared._ReclamationProject.Blindness;
using Robust.Shared.Timing;
using Content.Client.Movement.Systems;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Content.Shared.Eye.Blinding;
using Content.Shared.Eye.Blinding.Components;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;

namespace Content.Client.Eye.Blinding
{
    public sealed class BlindOverlay : Overlay
    {
        [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
        [Dependency] private readonly IPlayerManager _playerManager = default!;
        [Dependency] private readonly IEntityManager _entityManager = default!;
        [Dependency] private readonly IGameTiming _timing = default!;

        public override bool RequestScreenTexture => true;
        public override OverlaySpace Space => OverlaySpace.WorldSpace;
        private readonly ShaderInstance _greyscaleShader;
        private readonly ShaderInstance _circleMaskShader;
        private readonly ShaderInstance _concentrationShader;

        private BlindableComponent _blindableComponent = default!;

        public BlindOverlay()
        {
            IoCManager.InjectDependencies(this);
            _greyscaleShader = _prototypeManager.Index<ShaderPrototype>("GreyscaleFullscreen").InstanceUnique();
            _circleMaskShader = _prototypeManager.Index<ShaderPrototype>("CircleMask").InstanceUnique();
            _concentrationShader = _prototypeManager.Index<ShaderPrototype>("ReclamationBlindConcentration").InstanceUnique();
        }
        protected override bool BeforeDraw(in OverlayDrawArgs args)
        {
            if (!_entityManager.TryGetComponent(_playerManager.LocalSession?.AttachedEntity, out EyeComponent? eyeComp))
                return false;

            if (args.Viewport.Eye != eyeComp.Eye)
                return false;

            var playerEntity = _playerManager.LocalSession?.AttachedEntity;

            if (playerEntity == null)
                return false;

            if (!_entityManager.TryGetComponent<BlindableComponent>(playerEntity, out var blindComp))
                return false;

            _blindableComponent = blindComp;

            var blind = _blindableComponent.IsBlind;

            return blind;
        }

        protected override void Draw(in OverlayDrawArgs args)
        {
            if (ScreenTexture == null)
                return;

            var playerEntity = _playerManager.LocalSession?.AttachedEntity;

            if (playerEntity == null)
                return;

            if (_entityManager.TryGetComponent<EyeComponent>(playerEntity, out var content))
            {
                _circleMaskShader?.SetParameter("Zoom", content.Zoom.X);
            }

            _greyscaleShader?.SetParameter("SCREEN_TEXTURE", ScreenTexture);

            var worldHandle = args.WorldHandle;
            var viewport = args.WorldBounds;
            worldHandle.UseShader(_greyscaleShader);
            worldHandle.DrawRect(viewport, Color.White);
            var mask = _circleMaskShader;
            if (_entityManager.TryGetComponent<BlindConcentrationComponent>(playerEntity, out var focus))
            {
                var progress = _entityManager.System<BlindAwarenessSystem>().GetVisualProgress(playerEntity.Value, focus, _timing.CurTime);
                if (focus.Concentrating || progress > 0f ||
                    _entityManager.TryGetComponent<EyeClosingComponent>(playerEntity, out var eyelids) && eyelids.EyesClosed)
                {
                    _concentrationShader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
                    _concentrationShader.SetParameter("Focus", progress);
                    _concentrationShader.SetParameter("MaximumRadiusMultiplier", focus.MaximumRadiusMultiplier);
                    _concentrationShader.SetParameter("Radius", 30f);
                    _concentrationShader.SetParameter("MaskPower", 0.2f);
                    _concentrationShader.SetParameter("MaskMax", 8f);
                    _concentrationShader.SetParameter("MaskMult", 0.75f);
                    _concentrationShader.SetParameter("Pulse", (float) _timing.CurTime.TotalSeconds * 2f);
                    _concentrationShader.SetParameter("Zoom", content?.Zoom.X ?? 1f);
                    mask = _concentrationShader;
                }
            }
            worldHandle.UseShader(mask);
            worldHandle.DrawRect(viewport, Color.White);
            worldHandle.UseShader(null);
        }
    }
}
