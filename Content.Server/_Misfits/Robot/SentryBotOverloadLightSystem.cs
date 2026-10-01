// #Misfits Change /Add/ - Drives sentry bot passive 360-degree red lighting and overload warning emitters.
using Content.Server.Chat.Systems;
using Content.Server.Explosion.EntitySystems;
using Content.Shared.Chat;
using Content.Shared.Explosion.Components;
using Content.Shared.Light.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Server._Misfits.Robot;

public sealed class SentryBotOverloadLightSystem : EntitySystem
{
    private const string OverloadEmitterPrototype = "N14SentryBotOverloadLightEmitter";
    private static readonly Angle[] OverloadEmitterRotations =
    {
        Angle.Zero,
        Angle.FromDegrees(90),
        Angle.FromDegrees(180),
        Angle.FromDegrees(270),
    };

    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly SharedPointLightSystem _pointLight = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SentryBotOverloadLightComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<SentryBotOverloadLightComponent, ActiveTimerTriggerEvent>(OnActiveTimerTrigger);
        SubscribeLocalEvent<SentryBotOverloadLightComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnMapInit(EntityUid uid, SentryBotOverloadLightComponent component, MapInitEvent args)
    {
        component.FlashState = component.SteadyLightEnabled;

        if (_pointLight.TryGetLight(uid, out var light))
            _pointLight.SetEnabled(uid, false, light);

        EnsureOverloadEmitters(uid, component);
        SetOverloadEmittersEnabled(component, component.SteadyLightEnabled);
    }

    private void OnActiveTimerTrigger(EntityUid uid, SentryBotOverloadLightComponent component, ref ActiveTimerTriggerEvent args)
    {
        component.Overloading = true;
        component.FlashState = true;
        component.NextFlashTime = _timing.CurTime + TimeSpan.FromSeconds(component.FlashInterval);

        if (_pointLight.TryGetLight(uid, out var light))
            _pointLight.SetEnabled(uid, false, light);

        SetOverloadEmittersEnabled(component, true);
        _chat.TrySendInGameDoMessage(uid,
            Loc.GetString("n14-sentrybot-overload-emote"),
            ChatTransmitRange.Normal,
            hideLog: true,
            ignoreActionBlocker: true);
    }

    private void OnShutdown(EntityUid uid, SentryBotOverloadLightComponent component, ComponentShutdown args)
    {
        foreach (var emitter in component.OverloadLightEmitters)
        {
            if (Deleted(emitter))
                continue;

            QueueDel(emitter);
        }

        component.OverloadLightEmitters.Clear();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<SentryBotOverloadLightComponent, Robust.Server.GameObjects.PointLightComponent>();
        while (query.MoveNext(out var uid, out var component, out var light))
        {
            if (!component.Overloading)
            {
                if (light.Enabled)
                    _pointLight.SetEnabled(uid, false, light);

                continue;
            }

            if (!HasComp<ActiveTimerTriggerComponent>(uid))
            {
                component.Overloading = false;
                component.FlashState = component.SteadyLightEnabled;
                _pointLight.SetEnabled(uid, false, light);
                SetOverloadEmittersEnabled(component, component.SteadyLightEnabled);
                continue;
            }

            if (now < component.NextFlashTime)
                continue;

            component.FlashState = !component.FlashState;
            component.NextFlashTime = now + TimeSpan.FromSeconds(component.FlashInterval);
            SetOverloadEmittersEnabled(component, component.FlashState);
        }
    }

    private void EnsureOverloadEmitters(EntityUid uid, SentryBotOverloadLightComponent component)
    {
        if (component.OverloadLightEmitters.Count > 0)
            return;

        foreach (var rotation in OverloadEmitterRotations)
        {
            var emitter = Spawn(OverloadEmitterPrototype, Transform(uid).Coordinates);
            _transform.SetParent(emitter, uid);
            _transform.SetLocalRotation(emitter, rotation);
            component.OverloadLightEmitters.Add(emitter);

            if (_pointLight.TryGetLight(emitter, out var emitterLight))
                _pointLight.SetEnabled(emitter, component.SteadyLightEnabled, emitterLight);
        }
    }

    private void SetOverloadEmittersEnabled(SentryBotOverloadLightComponent component, bool enabled)
    {
        foreach (var emitter in component.OverloadLightEmitters)
        {
            if (Deleted(emitter) || !_pointLight.TryGetLight(emitter, out var light))
                continue;

            _pointLight.SetEnabled(emitter, enabled, light);
        }
    }
}