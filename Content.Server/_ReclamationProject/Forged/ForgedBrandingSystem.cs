using Content.Server.Chat.Systems;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using System.Linq;
using System.Threading.Tasks;
using Content.Server.Administration.Managers;
using Content.Server.DoAfter;
using Content.Server.GameTicking;
using Content.Server.Popups;
using Content.Server.Preferences.Managers;
using Content.Shared._ReclamationProject.Forged;
using Content.Shared.Administration;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Interaction;
using Content.Shared.Preferences;
using Content.Shared.DoAfter;
using Robust.Shared.Player;

namespace Content.Server._ReclamationProject.Forged;

public sealed partial class ForgedBrandingSystem : EntitySystem
{
    [Dependency] private readonly IServerPreferencesManager _preferences = default!;
    [Dependency] private readonly IAdminManager _admins = default!;
    [Dependency] private readonly ISharedAdminLogManager _adminLogs = default!;
    [Dependency] private readonly SharedHumanoidAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly DoAfterSystem _doAfter = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    private readonly HashSet<EntityUid> _saving = new();

    public override void Initialize()
    {
        base.Initialize();
        InitializeRemoval();
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnSpawn);
        SubscribeLocalEvent<ForgedBrandingIronComponent, AfterInteractEvent>(OnUse);
        SubscribeLocalEvent<ForgedBrandingIronComponent, ForgedBrandingDoAfterEvent>(OnFinished);
        SubscribeLocalEvent<ForgedBrandingPracticeComponent, MapInitEvent>(OnPracticeInit);
    }

    private void OnPracticeInit(Entity<ForgedBrandingPracticeComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.StartingBrand is { } brand && ForgedBrandingProfiles.IsBrand(brand))
            ApplyVisible(ent, brand);
    }

    private void OnSpawn(PlayerSpawnCompleteEvent args)
    {
        var prefs = _preferences.GetPreferencesOrNull(args.Player.UserId);
        if (prefs == null || !HasComp<HumanoidAppearanceComponent>(args.Mob))
            return;
        var identity = EnsureComp<ForgedBrandProfileComponent>(args.Mob);
        identity.User = args.Player.UserId;
        identity.Slot = prefs.TryIndexOfCharacter(args.Profile, out var slot) ? slot : prefs.SelectedCharacterIndex;
        identity.CharacterName = args.Profile.Name;
        RefreshBrandPlacement(args.Mob);
        var brand = args.JobId switch
        {
            "ForgedWorker" => ForgedBrandingProfiles.Captive,
            "ForgedBranded" or "ForgedEnforcer" or "ForgedVeteran" or "Forgemaster"
                or "ForgedSmithy" or "ForgedAdminForgemaster" => ForgedBrandingProfiles.Forged,
            _ => null
        };
        if (brand != null)
            _ = ChangeBrand(args.Mob, args.Mob, brand, false);
    }

    private bool HasBrand(EntityUid target, string brand) =>
        TryComp<HumanoidAppearanceComponent>(target, out var appearance) &&
        appearance.MarkingSet.GetForwardEnumerator().Any(m => m.MarkingId == brand || brand == ForgedBrandingProfiles.Captive && m.MarkingId == ForgedBrandingProfiles.CaptiveChest);

    private bool Eligible(EntityUid target) => HasComp<HumanoidAppearanceComponent>(target)
        && (HasComp<ForgedBrandProfileComponent>(target) || HasComp<ForgedBrandingPracticeComponent>(target));

    private void OnUse(Entity<ForgedBrandingIronComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target || !Eligible(target))
            return;
        args.Handled = true;
        if (ent.Comp.Unbrand)
        {
            if (!_admins.HasAdminFlag(args.User, AdminFlags.Admin))
            {
                _popup.PopupEntity(Loc.GetString("forged-unbrand-admin-only"), ent, args.User);
                return;
            }
            _ = ChangeBrand(target, args.User, null, true);
            return;
        }
        if (target == args.User || _saving.Contains(target) || HasBrand(target, ent.Comp.Brand))
        {
            _popup.PopupEntity(Loc.GetString("forged-brand-unavailable"), target, args.User);
            return;
        }
        if (TryComp<HumanoidAppearanceComponent>(target, out var humanoid) && humanoid.HiddenLayers.Contains((ent.Comp.Brand == ForgedBrandingProfiles.Forged || HasBrand(target, ForgedBrandingProfiles.Forged)) ? HumanoidVisualLayers.Chest : HumanoidVisualLayers.LArm))
        {
            _popup.PopupEntity(Loc.GetString((ent.Comp.Brand == ForgedBrandingProfiles.Forged || HasBrand(target, ForgedBrandingProfiles.Forged)) ? "forged-brand-no-chest" : "forged-brand-no-arm"), target, args.User);
            return;
        }
        if (_doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, TimeSpan.FromSeconds(20),
                new ForgedBrandingDoAfterEvent(), ent, target: target, used: ent)
            {
                MultiplyDelay = false,
                NeedHand = true,
                BreakOnMove = true,
                BreakOnDamage = true,
                DistanceThreshold = 1.5f,
                BlockDuplicate = true,
                DuplicateCondition = DuplicateConditions.SameTool,
                CancelDuplicate = false
            }))
        {
            _popup.PopupEntity(Loc.GetString((ent.Comp.Brand == ForgedBrandingProfiles.Forged || HasBrand(target, ForgedBrandingProfiles.Forged)) ? "forged-brand-start-chest" : "forged-brand-start"), target, args.User);
            _chat.TryEmoteWithChat(target, "Scream", ignoreActionBlocker: true, forceEmote: true);
            _audio.PlayPvs(new SoundPathSpecifier("/Audio/_Nuclear14/Weapons/Effects/Wpn_dynam_fuse.ogg"), target,
                AudioParams.Default.WithVolume(-12f).WithPitchScale(0.75f));
        }
    }

    private void OnFinished(Entity<ForgedBrandingIronComponent> ent, ref ForgedBrandingDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || args.Target is not { } target || ent.Comp.Unbrand
            || !Eligible(target) || !_interaction.InRangeUnobstructed(args.User, target) || HasBrand(target, ent.Comp.Brand))
            return;
        args.Handled = true;
        _ = ChangeBrand(target, args.User, ent.Comp.Brand, false);
    }

    private async Task ChangeBrand(EntityUid target, EntityUid user, string? brand, bool remove, bool captiveOnly = false)
    {
        if (!_saving.Add(target))
            return;
        try
        {
            if (remove && !captiveOnly && !_admins.HasAdminFlag(user, AdminFlags.Admin))
                return;
            if (TryComp<ForgedBrandProfileComponent>(target, out var identity))
            {
                var prefs = _preferences.GetPreferencesOrNull(identity.User);
                if (prefs == null || !prefs.Characters.TryGetValue(identity.Slot, out var character)
                    || character is not HumanoidCharacterProfile profile || profile.Name != identity.CharacterName)
                    throw new InvalidOperationException("The target character preferences are unavailable.");
                var updated = captiveOnly ? ForgedBrandingProfiles.WithoutCaptiveBrand(profile)
                    : remove ? ForgedBrandingProfiles.WithoutBrands(profile)
                    : ForgedBrandingProfiles.WithBrand(profile, brand!);
                await _preferences.SetProfile(identity.User, identity.Slot, updated, selectCharacter: false, allowBrandChanges: true);
            }
            else if (!HasComp<ForgedBrandingPracticeComponent>(target))
                return;

            if (Deleted(target))
                return;
            if (remove && TryComp<HumanoidAppearanceComponent>(target, out var appearance))
            {
                if (!captiveOnly)
                    appearance.MarkingSet.Remove(MarkingCategories.Chest, ForgedBrandingProfiles.Forged);
                appearance.MarkingSet.Remove(MarkingCategories.LeftArm, ForgedBrandingProfiles.Captive);
                appearance.MarkingSet.Remove(MarkingCategories.Chest, ForgedBrandingProfiles.CaptiveChest);
                Dirty(target, appearance);
            }
            else if (brand != null)
                ApplyVisible(target, brand);
            _adminLogs.Add(LogType.Action, LogImpact.High,
                $"{ToPrettyString(user):user} {(remove ? "removed permanent brands from" : "permanently branded")} {ToPrettyString(target):target} ({brand ?? "both brands"})");
            if (!Deleted(user))
                _popup.PopupEntity(Loc.GetString(captiveOnly ? "forged-brand-cut-complete" : remove ? "forged-unbrand-complete" : "forged-brand-complete"), target, user);
        }
        catch (Exception error)
        {
            Log.Error($"Failed to save brand for {ToPrettyString(target)}: {error}");
            if (!Deleted(user))
                _popup.PopupEntity(Loc.GetString("forged-brand-save-failed"), user, user);
        }
        finally
        {
            _saving.Remove(target);
        }
    }

    private void ApplyVisible(EntityUid target, string brand)
    {
        if (!HasBrand(target, brand))
            _appearance.AddMarking(target, brand, ForgedBrandingProfiles.BrandColor, forced: true);
        RefreshBrandPlacement(target);
    }

    private void RefreshBrandPlacement(EntityUid target)
    {
        if (!TryComp<HumanoidAppearanceComponent>(target, out var appearance)
            || !HasBrand(target, ForgedBrandingProfiles.Captive))
            return;
        var chest = HasBrand(target, ForgedBrandingProfiles.Forged);
        appearance.MarkingSet.Remove(MarkingCategories.LeftArm, ForgedBrandingProfiles.Captive);
        appearance.MarkingSet.Remove(MarkingCategories.Chest, ForgedBrandingProfiles.CaptiveChest);
        // Keep the saved captive brand canonical; only its display changes when an anvil is present.
        _appearance.AddMarking(target, chest ? ForgedBrandingProfiles.CaptiveChest : ForgedBrandingProfiles.Captive,
            chest ? Color.FromHex("#B96552") : ForgedBrandingProfiles.BrandColor, forced: true);
        Dirty(target, appearance);
    }
}
