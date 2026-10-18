using Content.Shared._ReclamationProject.GunAttachments;
using Content.Shared.Damage;
using Content.Shared.Item;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Wieldable.Components;
using Robust.Shared.Containers;

namespace Content.Server._ReclamationProject.GunAttachments;

/// <summary>Adds attachment mounts to handheld guns without changing their ammunition slots.</summary>
public sealed class GunAttachmentSetupSystem : EntitySystem
{
    [Dependency] private readonly GunAttachmentsSystem _bayonets = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ItemComponent, MapInitEvent>(OnGunInit);
        SubscribeLocalEvent<GunAttachmentsComponent, MapInitEvent>(OnMountInit);
        SubscribeLocalEvent<GunAttachmentsComponent, GunAttachmentChangedEvent>(OnContainerChanged);
    }

    private void OnGunInit(EntityUid uid, ItemComponent component, MapInitEvent args)
    {
        if (HasComp<GunComponent>(uid))
            EnsureComp<GunAttachmentsComponent>(uid);
    }

    private void OnMountInit(EntityUid uid, GunAttachmentsComponent component, MapInitEvent args)
        => UpdateMelee(uid, component);

    private void OnContainerChanged(EntityUid uid, GunAttachmentsComponent component, ref GunAttachmentChangedEvent args)
    {
        if (args.SlotId == GunAttachmentsSystem.MuzzleSlotId && !TerminatingOrDeleted(uid))
            UpdateMelee(uid, component);
    }

    private void UpdateMelee(EntityUid uid, GunAttachmentsComponent component)
    {
        if (_bayonets.HasBayonet(component))
        {
            if (!component.Applied)
            {
                component.HadMelee = TryComp<MeleeWeaponComponent>(uid, out var original);
                if (original != null)
                {
                    component.OriginalDamage = new DamageSpecifier(original.Damage);
                    component.OriginalRange = original.Range;
                    component.OriginalAttackRate = original.AttackRate;
                    component.OriginalAngle = original.Angle;
                    component.OriginalMaxTargets = original.MaxTargets;
                    component.OriginalHeavyRangeModifier = original.HeavyRangeModifier;
                    component.OriginalHeavyRateModifier = original.HeavyRateModifier;
                    component.OriginalDisableHeavy = original.DisableHeavy;
                    component.OriginalCanWideSwing = original.CanWideSwing;
                    component.OriginalAnimation = original.Animation;
                    component.OriginalWideAnimation = original.WideAnimation;
                    component.OriginalWideRotation = original.WideAnimationRotation;
                    component.OriginalSoundHit = original.SoundHit;
                }
                component.Applied = true;
            }
            var melee = EnsureComp<MeleeWeaponComponent>(uid);
            var template = _bayonets.GetTemplate();
            melee.Damage = new DamageSpecifier(template.Damage);
            melee.Range = template.Range;
            melee.AttackRate = template.AttackRate;
            melee.Animation = template.Animation;
            // Reuse the spear's directional attack geometry and thrust animation.
            var spear = _bayonets.GetStabTemplate();
            melee.Angle = spear.Angle;
            melee.MaxTargets = 1;
            melee.HeavyRangeModifier = 1f;
            melee.HeavyRateModifier = 1f;
            melee.DisableHeavy = false;
            melee.CanWideSwing = true;
            melee.WideAnimation = spear.Animation;
            melee.WideAnimationRotation = template.WideAnimationRotation;
            melee.SoundHit = template.SoundHit;
            Dirty(uid, melee);
            return;
        }
        if (!component.Applied)
            return;
        component.Applied = false;
        if (!component.HadMelee)
        {
            RemComp<MeleeWeaponComponent>(uid);
            return;
        }
        var restored = EnsureComp<MeleeWeaponComponent>(uid);
        restored.Damage = component.OriginalDamage!;
        restored.Range = component.OriginalRange;
        restored.AttackRate = component.OriginalAttackRate;
        restored.Angle = component.OriginalAngle;
        restored.MaxTargets = component.OriginalMaxTargets;
        restored.HeavyRangeModifier = component.OriginalHeavyRangeModifier;
        restored.HeavyRateModifier = component.OriginalHeavyRateModifier;
        restored.DisableHeavy = component.OriginalDisableHeavy;
        restored.CanWideSwing = component.OriginalCanWideSwing;
        restored.Animation = component.OriginalAnimation;
        restored.WideAnimation = component.OriginalWideAnimation;
        restored.WideAnimationRotation = component.OriginalWideRotation;
        restored.SoundHit = component.OriginalSoundHit;
        Dirty(uid, restored);
    }
}
