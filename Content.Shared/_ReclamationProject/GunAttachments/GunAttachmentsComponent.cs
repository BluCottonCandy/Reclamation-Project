using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Whitelist;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._ReclamationProject.GunAttachments;

/// <summary>Removable muzzle, grip and light attachments fitted to a handheld gun.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class GunAttachmentsComponent : Component
{
    [DataField] public ItemSlot MuzzleSlot = new() { Name = "gun-attachment-muzzle-slot" };
    [DataField] public ItemSlot GripSlot = new()
    {
        Name = "gun-attachment-grip-slot",
        Whitelist = new EntityWhitelist { Tags = new() { "ReclamationGunGrip" } },
    };
    [DataField] public ItemSlot LightSlot = new()
    {
        Name = "gun-attachment-light-slot",
        Whitelist = new EntityWhitelist { Tags = new() { "ReclamationGunLight" } },
    };
    [DataField] public ItemSlot StockSlot = new()
    {
        Name = "gun-attachment-stock-slot",
        Whitelist = new EntityWhitelist { Tags = new() { "ReclamationGunStock" } },
    };
    [DataField] public ItemSlot LaserSlot = new()
    {
        Name = "gun-attachment-laser-slot",
        Whitelist = new EntityWhitelist { Tags = new() { "ReclamationGunLaserSight" } },
    };
    [DataField] public ItemSlot HoloSlot = new()
    {
        Name = "gun-attachment-holo-slot",
        Whitelist = new EntityWhitelist { Tags = new() { "ReclamationGunHoloSight" } },
    };
    [DataField, AutoNetworkedField] public EntityUid? BipodAction;
    [DataField, AutoNetworkedField] public bool BipodDeployed;
    [DataField, AutoNetworkedField] public EntityUid? LightAction;

    // Saved so removing a bayonet restores the original gun even after a map save/load.
    [DataField] public bool Applied;
    [DataField] public bool HadMelee;
    [DataField] public DamageSpecifier? OriginalDamage;
    [DataField] public float OriginalRange;
    [DataField] public float OriginalAttackRate;
    [DataField] public Angle OriginalAngle;
    [DataField] public int OriginalMaxTargets;
    [DataField] public float OriginalHeavyRangeModifier;
    [DataField] public float OriginalHeavyRateModifier;
    [DataField] public bool OriginalDisableHeavy;
    [DataField] public bool OriginalCanWideSwing;
    [DataField] public EntProtoId OriginalAnimation = "WeaponArcPunch";
    [DataField] public EntProtoId OriginalWideAnimation = "WeaponArcSlash";
    [DataField] public Angle OriginalWideRotation;
    [DataField] public SoundSpecifier? OriginalSoundHit;
}
