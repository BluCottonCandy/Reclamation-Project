using Content.Shared.Examine;
using Content.Shared.Tag;
using Content.Shared.Verbs;
using Content.Shared.Weapons.Melee;
using Robust.Shared.Utility;

namespace Content.Shared._ReclamationProject.GunAttachments;

/// <summary>Uses the same detailed examine button as shields, for loose and installed attachments.</summary>
public sealed class GunAttachmentExamineSystem : EntitySystem
{
    [Dependency] private readonly ExamineSystemShared _examine = default!;
    [Dependency] private readonly TagSystem _tags = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<GetVerbsEvent<ExamineVerb>>(OnExamine);
    }

    private void OnExamine(GetVerbsEvent<ExamineVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract) return;
        var message = new FormattedMessage();
        Component? source;
        if (TryComp<GunAttachmentsComponent>(args.Target, out var gun))
        {
            source = gun;
            foreach (var item in Installed(gun)) Append(item, message);
        }
        else source = Append(args.Target, message);
        if (source == null || message.IsEmpty) return;
        _examine.AddDetailedExamineVerb(args, source, message,
            Loc.GetString("gun-attachment-details-title"),
            "/Textures/Interface/VerbIcons/dot.svg.192dpi.png",
            Loc.GetString("gun-attachment-details-hint"));
    }

    private static IEnumerable<EntityUid> Installed(GunAttachmentsComponent gun)
    {
        if (gun.MuzzleSlot.Item is { } muzzle) yield return muzzle;
        if (gun.GripSlot.Item is { } grip) yield return grip;
        if (gun.LightSlot.Item is { } light) yield return light;
        if (gun.StockSlot.Item is { } stock) yield return stock;
        if (gun.LaserSlot.Item is { } laser) yield return laser;
        if (gun.HoloSlot.Item is { } holo) yield return holo;
    }

    private Component? Append(EntityUid item, FormattedMessage message)
    {
        Component? component = null;
        string? key = null;
        if (TryComp<GunSuppressorComponent>(item, out var suppressor)) { component = suppressor; key = "suppressor"; }
        else if (TryComp<GunGripComponent>(item, out var grip)) { component = grip; key = "grip"; }
        else if (TryComp<GunStockComponent>(item, out var stock)) { component = stock; key = "stock"; }
        else if (TryComp<GunLaserSightComponent>(item, out var laser)) { component = laser; key = "laser"; }
        else if (TryComp<GunHoloSightComponent>(item, out var holo)) { component = holo; key = "holo"; }
        else if (TryComp<GunBipodComponent>(item, out var bipod)) { component = bipod; key = "bipod"; }
        else if (_tags.HasTag(item, "ReclamationGunLight")) { component = Comp<TagComponent>(item); key = "light"; }
        else if (_tags.HasTag(item, "CombatKnife") && TryComp<MeleeWeaponComponent>(item, out var melee)) { component = melee; key = "bayonet"; }
        if (key == null) return null;
        if (!message.IsEmpty) message.PushNewline();
        message.AddMarkup(Loc.GetString("gun-attachment-details-heading", ("name", Name(item))));
        message.PushNewline();
        var multiplier = component switch
        {
            GunGripComponent gripComponent => gripComponent.RecoilMultiplier,
            GunStockComponent stockComponent => stockComponent.ControlMultiplier,
            GunLaserSightComponent laserComponent => laserComponent.AccuracyMultiplier,
            GunHoloSightComponent holoComponent => holoComponent.AccuracyMultiplier,
            _ => 1f,
        };
        message.AddMarkup(Loc.GetString("gun-attachment-details-" + key,
            ("value", MathF.Round((1f - multiplier) * 100f))));
        if (key is "suppressor" or "grip" or "stock" or "bipod" or "bayonet" or "holo")
        {
            message.PushNewline();
            message.AddMarkup(Loc.GetString("gun-attachment-limit-" + key));
        }
        return component;
    }
}
