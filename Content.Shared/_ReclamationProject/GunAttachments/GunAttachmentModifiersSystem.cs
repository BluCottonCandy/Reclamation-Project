// Adapted from RMC-14's AttachableSilencerSystem and ranged modifier relay (MIT).
// https://github.com/RMC-14/RMC-14/tree/master/Content.Shared/_RMC14/Attachable/Systems
// Copyright (c) 2023-2026 RMC-14. See LICENSE-RMC.txt.
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Wieldable.Components;
using Robust.Shared.Audio;

namespace Content.Shared._ReclamationProject.GunAttachments;

public sealed class GunAttachmentModifiersSystem : EntitySystem
{
    [Dependency] private readonly GunAttachmentsSystem _attachments = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GunSuppressorComponent, GunAttachmentRelayedEvent<GunRefreshModifiersEvent>>(OnSuppressorRefresh);
        SubscribeLocalEvent<GunSuppressorComponent, GunAttachmentRelayedEvent<GunMuzzleFlashAttemptEvent>>(OnSuppressorFlash);
        SubscribeLocalEvent<GunStockComponent, GunAttachmentRelayedEvent<GunRefreshModifiersEvent>>(OnStock);
        SubscribeLocalEvent<GunLaserSightComponent, GunAttachmentRelayedEvent<GunRefreshModifiersEvent>>(OnLaser);
        SubscribeLocalEvent<GunBipodComponent, GunAttachmentRelayedEvent<GunRefreshModifiersEvent>>(OnBipod);
        SubscribeLocalEvent<GunHoloSightComponent, GunAttachmentRelayedEvent<GunRefreshModifiersEvent>>(OnHolo);
        SubscribeLocalEvent<GunHoloSightComponent, GunAttachmentRelayedEvent<GunGetAmmoSpreadEvent>>(OnHoloAmmo);
        SubscribeLocalEvent<GunGripComponent, GunAttachmentRelayedEvent<GunRefreshModifiersEvent>>(OnGripRefresh);
        SubscribeLocalEvent<GunGripComponent, GunAttachmentRelayedEvent<GunGetAmmoSpreadEvent>>(OnGripAmmo);
        SubscribeLocalEvent<GunStockComponent, GunAttachmentRelayedEvent<GunGetAmmoSpreadEvent>>(OnStockAmmo);
        SubscribeLocalEvent<GunLaserSightComponent, GunAttachmentRelayedEvent<GunGetAmmoSpreadEvent>>(OnLaserAmmo);
        SubscribeLocalEvent<GunBipodComponent, GunAttachmentRelayedEvent<GunGetAmmoSpreadEvent>>(OnBipodAmmo);
    }

    private void OnSuppressorRefresh(EntityUid uid, GunSuppressorComponent component,
        ref GunAttachmentRelayedEvent<GunRefreshModifiersEvent> args)
    {
        args.Args.ProjectileSpeed *= component.VelocityMultiplier;
        if (args.Args.SoundGunshot is not { } sound)
            return;
        var parameters = sound.Params.WithVolume(sound.Params.Volume - component.SoundReduction);
        // Preserve the firearm's own sound and variation, including energy weapons.
        // Never mutate the prototype's shared SoundSpecifier or stack volume reductions.
        args.Args.SoundGunshot = sound switch
        {
            SoundPathSpecifier path => new SoundPathSpecifier(path.Path, parameters),
            SoundCollectionSpecifier collection when collection.Collection != null
                => new SoundCollectionSpecifier(collection.Collection.Value.Id, parameters),
            _ => sound,
        };
    }

    private void OnSuppressorFlash(EntityUid uid, GunSuppressorComponent component,
        ref GunAttachmentRelayedEvent<GunMuzzleFlashAttemptEvent> args)
    {
        if (component.HideMuzzleFlash)
            args.Args.Cancelled = true;
    }

    private void OnGripRefresh(EntityUid uid, GunGripComponent component,
        ref GunAttachmentRelayedEvent<GunRefreshModifiersEvent> args)
    {
        args.Args.CameraRecoilScalar *= component.RecoilMultiplier;
        Accuracy(ref args.Args, component.RecoilMultiplier);
    }
    private static void Accuracy(ref GunRefreshModifiersEvent args, float multiplier)
    {
        args.MinAngle *= multiplier;
        args.MaxAngle *= multiplier;
        args.AngleIncrease *= multiplier;
    }

    private void OnStock(EntityUid uid, GunStockComponent component, ref GunAttachmentRelayedEvent<GunRefreshModifiersEvent> args)
    {
        if (!TryComp<WieldableComponent>(args.Holder, out var wield) || !wield.Wielded) return;
        args.Args.CameraRecoilScalar *= component.ControlMultiplier;
        Accuracy(ref args.Args, component.ControlMultiplier);
    }

    private void OnLaser(EntityUid uid, GunLaserSightComponent component, ref GunAttachmentRelayedEvent<GunRefreshModifiersEvent> args)
        => Accuracy(ref args.Args, component.AccuracyMultiplier);

    private void OnHolo(EntityUid uid, GunHoloSightComponent component, ref GunAttachmentRelayedEvent<GunRefreshModifiersEvent> args)
        => Accuracy(ref args.Args, component.AccuracyMultiplier);

    private void OnHoloAmmo(EntityUid uid, GunHoloSightComponent component, ref GunAttachmentRelayedEvent<GunGetAmmoSpreadEvent> args)
        => args.Args.Spread *= component.AccuracyMultiplier;

    private void OnBipod(EntityUid uid, GunBipodComponent component, ref GunAttachmentRelayedEvent<GunRefreshModifiersEvent> args)
    {
        if (TryComp<GunAttachmentsComponent>(args.Holder, out var holder) && _attachments.BipodActive(args.Holder, holder))
            Accuracy(ref args.Args, 0.7f);
    }

    private void OnGripAmmo(EntityUid uid, GunGripComponent component, ref GunAttachmentRelayedEvent<GunGetAmmoSpreadEvent> args)
        => args.Args.Spread *= component.RecoilMultiplier;

    private void OnStockAmmo(EntityUid uid, GunStockComponent component, ref GunAttachmentRelayedEvent<GunGetAmmoSpreadEvent> args)
    {
        if (TryComp<WieldableComponent>(args.Holder, out var wield) && wield.Wielded)
            args.Args.Spread *= component.ControlMultiplier;
    }

    private void OnLaserAmmo(EntityUid uid, GunLaserSightComponent component, ref GunAttachmentRelayedEvent<GunGetAmmoSpreadEvent> args)
        => args.Args.Spread *= component.AccuracyMultiplier;

    private void OnBipodAmmo(EntityUid uid, GunBipodComponent component, ref GunAttachmentRelayedEvent<GunGetAmmoSpreadEvent> args)
    {
        if (TryComp<GunAttachmentsComponent>(args.Holder, out var holder) && _attachments.BipodActive(args.Holder, holder))
            args.Args.Spread *= 0.7f;
    }

}
