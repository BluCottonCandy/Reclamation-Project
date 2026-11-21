using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Player;

namespace Content.Client.UserInterface.Systems.DamageOverlays;

public sealed class DamageOverlaySystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlayManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;

    [Dependency] private readonly MobThresholdSystem _mobThresholdSystem = default!;
    private Overlays.DamageOverlay _overlay = default!;

    public override void Initialize()
    {
        base.Initialize();
        _overlay = new Overlays.DamageOverlay();
        SubscribeLocalEvent<LocalPlayerAttachedEvent>(OnPlayerAttach);
        SubscribeLocalEvent<LocalPlayerDetachedEvent>(OnPlayerDetached);
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<MobThresholdChecked>(OnThresholdCheck);
    }

    public override void Shutdown()
    {
        _overlayManager.RemoveOverlay(_overlay);
        _overlay.Dispose();
        base.Shutdown();
    }

    private void OnPlayerAttach(LocalPlayerAttachedEvent args)
    {
        _overlay.Reset();
        if (!EntityManager.TryGetComponent<MobStateComponent>(args.Entity, out var mobState))
            return;
        if (mobState.CurrentState != MobState.Dead)
            UpdateOverlays(args.Entity, mobState);
        _overlayManager.AddOverlay(_overlay);
    }

    private void OnPlayerDetached(LocalPlayerDetachedEvent args)
    {
        _overlayManager.RemoveOverlay(_overlay);
        _overlay.Reset();
    }

    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        if (args.Target != _playerManager.LocalEntity)
            return;

        UpdateOverlays(args.Target, args.Component);
    }

    private void OnThresholdCheck(ref MobThresholdChecked args)
    {
        if (args.Target != _playerManager.LocalEntity)
            return;
        UpdateOverlays(args.Target, args.MobState, args.Damageable, args.Threshold);
        _overlayManager.AddOverlay(_overlay);
    }

    //TODO: Jezi: adjust oxygen and hp overlays to use appropriate systems once bodysim is implemented
    private void UpdateOverlays(EntityUid entity, MobStateComponent? mobState, DamageableComponent? damageable = null, MobThresholdsComponent? thresholds = null)
    {
        if (mobState == null && !EntityManager.TryGetComponent(entity, out mobState) ||
            thresholds == null && !EntityManager.TryGetComponent(entity, out thresholds) ||
            damageable == null && !EntityManager.TryGetComponent(entity, out  damageable))
            return;

        if (!thresholds.ShowOverlays ||
            !_mobThresholdSystem.TryGetIncapThreshold(entity, out var foundThreshold, thresholds) ||
            foundThreshold <= 0)
        {
            _overlay.Reset();
            return;
        }

        var critThreshold = foundThreshold.Value;
        _overlay.State = mobState.CurrentState;

        switch (mobState.CurrentState)
        {
            case MobState.Alive:
            case MobState.SoftCritical:
            {
                var airloss = damageable.DamagePerGroup.GetValueOrDefault("Airloss");
                var injury = _mobThresholdSystem.GetDisplayedDamage(entity, damageable.TotalDamage) - airloss;
                // Divide in floating point so small wounds that penetrate armor remain visible.
                _overlay.InjuryLevel = Math.Clamp(injury.Float() / critThreshold.Float(), 0f, 1f);
                _overlay.OxygenLevel = Math.Clamp(airloss.Float() / critThreshold.Float(), 0f, 1f);

                _overlay.CritLevel = 0;
                _overlay.DeadLevel = 0;
                break;
            }
            case MobState.Critical:
            {
                if (!_mobThresholdSystem.TryGetDeadPercentage(entity,
                        FixedPoint2.Max(0.0, _mobThresholdSystem.GetDisplayedDamage(entity, damageable.TotalDamage)), out var critLevel))
                    return;
                _overlay.CritLevel = critLevel.Value.Float();

                _overlay.InjuryLevel = 0;
                _overlay.DeadLevel = 0;
                break;
            }
            case MobState.Dead:
            {
                _overlay.InjuryLevel = 0;
                _overlay.CritLevel = 0;
                break;
            }
        }
    }
}
