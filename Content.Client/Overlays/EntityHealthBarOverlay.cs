using System.Numerics;
using Content.Shared._ReclamationProject.PowerArmor;
using Content.Client.StatusIcon;
using Content.Client.UserInterface.Systems;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.StatusIcon;
using Content.Shared.StatusIcon.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using static Robust.Shared.Maths.Color;

namespace Content.Client.Overlays;

/// <summary>
/// Overlay that shows a health bar on mobs.
/// </summary>
public sealed class EntityHealthBarOverlay : Overlay
{
    private readonly IEntityManager _entManager;
    private readonly IPrototypeManager _prototype;

    private readonly SharedTransformSystem _transform;
    private readonly MobStateSystem _mobStateSystem;
    private readonly MobThresholdSystem _mobThresholdSystem;
    private readonly StatusIconSystem _statusIconSystem;
    private readonly ProgressColorSystem _progressColor;


    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;
    public HashSet<string> DamageContainers = new();
    public bool ShowArmorCondition;
    public ProtoId<HealthIconPrototype>? StatusIcon;

    public EntityHealthBarOverlay(IEntityManager entManager, IPrototypeManager prototype)
    {
        _entManager = entManager;
        _prototype = prototype;
        _transform = _entManager.System<SharedTransformSystem>();
        _mobStateSystem = _entManager.System<MobStateSystem>();
        _mobThresholdSystem = _entManager.System<MobThresholdSystem>();
        _statusIconSystem = _entManager.System<StatusIconSystem>();
        _progressColor = _entManager.System<ProgressColorSystem>();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        var rotation = args.Viewport.Eye?.Rotation ?? Angle.Zero;
        var xformQuery = _entManager.GetEntityQuery<TransformComponent>();

        const float scale = 1f;
        var scaleMatrix = Matrix3Helpers.CreateScale(new Vector2(scale, scale));
        var rotationMatrix = Matrix3Helpers.CreateRotation(-rotation);
        _prototype.TryIndex(StatusIcon, out var statusIcon);

        var query = _entManager.AllEntityQueryEnumerator<MobThresholdsComponent, MobStateComponent, DamageableComponent, SpriteComponent>();
        while (query.MoveNext(out var uid,
            out var mobThresholdsComponent,
            out var mobStateComponent,
            out var damageableComponent,
            out var spriteComponent))
        {
            if (statusIcon != null && !_statusIconSystem.IsVisible((uid, _entManager.GetComponent<MetaDataComponent>(uid)), statusIcon))
                continue;

            // We want the stealth user to still be able to see his health bar himself
            if (!xformQuery.TryGetComponent(uid, out var xform) ||
                xform.MapID != args.MapId)
                continue;

            var armorCondition = GetArmorCondition(uid);
            var showHealth = damageableComponent.DamageContainerID is {} container && DamageContainers.Contains(container);
            if (!showHealth && armorCondition == null)
                continue;

            // we use the status icon component bounds if specified otherwise use sprite
            var bounds = _entManager.GetComponentOrNull<StatusIconComponent>(uid)?.Bounds ?? spriteComponent.Bounds;
            var worldPos = _transform.GetWorldPosition(xform, xformQuery);

            if (!bounds.Translated(worldPos).Intersects(args.WorldAABB))
                continue;

            var health = showHealth ? CalcProgress(uid, mobStateComponent, damageableComponent, mobThresholdsComponent) : null;
            if (health == null && armorCondition == null)
                continue;

            var worldPosition = _transform.GetWorldPosition(xform);
            var worldMatrix = Matrix3Helpers.CreateTranslation(worldPosition);

            var scaledWorld = Matrix3x2.Multiply(scaleMatrix, worldMatrix);
            var matty = Matrix3x2.Multiply(rotationMatrix, scaledWorld);

            handle.SetTransform(matty);

            var yOffset = bounds.Height * EyeManager.PixelsPerMeter / 2 - 3f;
            var widthOfMob = bounds.Width * EyeManager.PixelsPerMeter;

            var position = new Vector2(-widthOfMob / EyeManager.PixelsPerMeter / 2, yOffset / EyeManager.PixelsPerMeter);
            if (health is {} medical)
            {
                DrawBar(handle, position, widthOfMob, medical.ratio, GetProgressColor(medical.ratio, medical.inCrit));
                position.Y += 4f / EyeManager.PixelsPerMeter;
            }

            if (armorCondition is {} armor)
                DrawBar(handle, position, widthOfMob, armor, Color.DarkOrange);
        }

        handle.SetTransform(Matrix3x2.Identity);
    }

    public float? GetArmorCondition(EntityUid wearer) =>
        ShowArmorCondition && _entManager.TryGetComponent<WornArmorConditionComponent>(wearer, out var condition)
            ? condition.Fraction
            : null;

    private static void DrawBar(DrawingHandleWorld handle, Vector2 position, float width, float fraction, Color color)
    {
        const float inset = 8f;
        const float ppm = EyeManager.PixelsPerMeter;
        var right = width - inset;
        var fill = inset + (right - inset) * fraction;
        Box2 Rect(float end, float bottom) => new Box2(new Vector2(inset, bottom) / ppm, new Vector2(end, 3f) / ppm).Translated(position);
        handle.DrawRect(Rect(right, 0f), Black.WithAlpha(192));
        if (fraction <= 0)
            return;
        handle.DrawRect(Rect(fill, 0f), color);
        handle.DrawRect(Rect(fill, 2f), Black.WithAlpha(128));
    }

    /// <summary>
    /// Returns a ratio between 0 and 1, and whether the entity is in crit.
    /// </summary>
    private (float ratio, bool inCrit)? CalcProgress(EntityUid uid, MobStateComponent component, DamageableComponent dmg, MobThresholdsComponent thresholds)
    {
        var displayedDamage = _mobThresholdSystem.GetDisplayedDamage(uid, dmg.TotalDamage);
        if (_mobStateSystem.IsAlive(uid, component))
        {
            if (dmg.HealthBarThreshold != null && displayedDamage < dmg.HealthBarThreshold)
                return null;

            if (!_mobThresholdSystem.TryGetThresholdForState(uid, MobState.Critical, out var threshold, thresholds) &&
                !_mobThresholdSystem.TryGetThresholdForState(uid, MobState.Dead, out threshold, thresholds))
                return (1, false);

            var ratio = 1 - ((FixedPoint2) (displayedDamage / threshold)).Float();
            return (ratio, false);
        }

        if (_mobStateSystem.IsCritical(uid, component))
        {
            if (!_mobThresholdSystem.TryGetThresholdForState(uid, MobState.Critical, out var critThreshold, thresholds) ||
                !_mobThresholdSystem.TryGetThresholdForState(uid, MobState.Dead, out var deadThreshold, thresholds))
            {
                return (1, true);
            }

            var ratio = 1 - ((displayedDamage - critThreshold) / (deadThreshold - critThreshold)).Value.Float();

            return (ratio, true);
        }

        return (0, true);
    }

    public Color GetProgressColor(float progress, bool crit)
    {
        if (crit)
            progress = 0;

        return _progressColor.GetProgressColor(progress);
    }
}
