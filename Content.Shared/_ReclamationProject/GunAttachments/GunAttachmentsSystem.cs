using System.Linq;
using Content.Shared.Popups;
// Attachment slot/event-relay design adapted from RMC-14 (MIT).
// See LICENSE-RMC.txt and https://github.com/RMC-14/RMC-14/tree/master/Content.Shared/_RMC14/Attachable
using Content.Shared._RMC.Attachable.Events;
using Content.Shared.Actions;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Hands;
using Content.Shared.Standing;
using Content.Shared.Movement.Systems;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Tag;
using Content.Shared.Verbs;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Whitelist;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._ReclamationProject.GunAttachments;

public sealed class GunAttachmentsSystem : EntitySystem
{
    public const string MuzzleSlotId = "gun_muzzle";
    public const string StockSlotId = "gun_stock";
    public const string LaserSlotId = "gun_laser";
    public const string HoloSlotId = "gun_holo";
    public const string GripSlotId = "gun_grip";
    public const string LightSlotId = "gun_light";
    public const string TemplateId = "WeaponSniperMosin";
    public const string LightActionId = "ReclamationActionToggleWeaponLight";
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;
    [Dependency] private readonly ItemSlotsSystem _slots = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly TagSystem _tags = default!;
    [Dependency] private readonly SharedGunSystem _guns = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly UnpoweredFlashlightSystem _lights = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GunAttachmentsComponent, GotUnequippedHandEvent>(OnDropped);
        SubscribeLocalEvent<GunAttachmentsComponent, HeldRelayedEvent<RefreshMovementSpeedModifiersEvent>>(OnSpeed);
        SubscribeLocalEvent<StandingStateComponent, StoodEvent>(OnStood);
        SubscribeLocalEvent<GunAttachmentsComponent, ToggleGunBipodActionEvent>(OnToggleBipod);
        SubscribeLocalEvent<GunAttachmentsComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<GunAttachmentsComponent, ComponentRemove>(OnRemove);
        SubscribeLocalEvent<GunAttachmentsComponent, EntInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<GunAttachmentsComponent, EntRemovedFromContainerMessage>(OnRemoved);
        SubscribeLocalEvent<GunAttachmentsComponent, GetMeleeDamageEvent>(OnDamage, after: new[] { typeof(WieldableSystem) });
        SubscribeLocalEvent<GunAttachmentsComponent, GunRefreshModifiersEvent>(OnRefresh, after: new[] { typeof(WieldableSystem) });
        SubscribeLocalEvent<GunAttachmentsComponent, GunMuzzleFlashAttemptEvent>(OnMuzzleFlash);
        SubscribeLocalEvent<GunAttachmentsComponent, GunGetAmmoSpreadEvent>(OnAmmoSpread);
        SubscribeLocalEvent<GunAttachmentsComponent, ItemWieldedEvent>(OnWielded, after: new[] { typeof(WieldableSystem) });
        SubscribeLocalEvent<GunAttachmentsComponent, ItemUnwieldedEvent>(OnUnwielded, after: new[] { typeof(WieldableSystem) });
        SubscribeLocalEvent<GunAttachmentsComponent, GetItemActionsEvent>(OnGetActions);
        SubscribeLocalEvent<GunAttachmentsComponent, ToggleWeaponLightActionEvent>(OnToggleLight);
        SubscribeLocalEvent<GunAttachmentsComponent, GetVerbsEvent<ActivationVerb>>(OnLightVerb);
        SubscribeLocalEvent<GunAttachmentsComponent, ExaminedEvent>(OnExamine);
    }

    public bool SupportsTwoHands(EntityUid uid)
        => TryComp<WieldableComponent>(uid, out var wield) && wield.FreeHandsRequired >= 1;

    private bool HasAncestor(EntityPrototype prototype, string ancestor)
        => _prototypes.EnumerateAllParents<EntityPrototype>(prototype.ID, includeSelf: true).Any(parent => parent.id == ancestor);

    public bool SupportsStock(EntityUid uid)
        => MetaData(uid).EntityPrototype is { } prototype &&
           (HasAncestor(prototype, "BaseWeaponSubMachineGun") || prototype.ID.Contains("SMG", StringComparison.Ordinal));

    public bool SupportsBipod(EntityUid uid)
        => SupportsTwoHands(uid) && !SupportsStock(uid) && MetaData(uid).EntityPrototype is { } prototype &&
           (HasAncestor(prototype, "BaseWeaponRifle") || HasAncestor(prototype, "BaseWeaponSniper") ||
            prototype.ID.Contains("Rifle", StringComparison.Ordinal));

    public bool SupportsHolo(EntityUid uid)
        => MetaData(uid).EntityPrototype is { } prototype &&
           !HasAncestor(prototype, "BaseWeaponSniper") &&
           !prototype.ID.Contains("Sniper", StringComparison.OrdinalIgnoreCase);

    public bool BipodActive(EntityUid uid, GunAttachmentsComponent component)
        => component.BipodDeployed && component.GripSlot.Item is { } item && HasComp<GunBipodComponent>(item) &&
           _hands.IsHolding(Transform(uid).ParentUid, uid, out _) &&
           TryComp<StandingStateComponent>(Transform(uid).ParentUid, out var standing) && standing.CurrentState == StandingState.Lying;

    public bool HasBayonet(GunAttachmentsComponent component)
        => component.MuzzleSlot.Item is { } item && _tags.HasTag(item, "CombatKnife");

    public MeleeWeaponComponent GetTemplate()
        => (MeleeWeaponComponent) _prototypes.Index<EntityPrototype>(TemplateId).Components["MeleeWeapon"].Component;

    public MeleeWeaponComponent GetStabTemplate()
        => (MeleeWeaponComponent) _prototypes.Index<EntityPrototype>("Spear").Components["MeleeWeapon"].Component;

    private void OnInit(EntityUid uid, GunAttachmentsComponent component, ComponentInit args)
    {
        var tags = new List<ProtoId<TagPrototype>>();
        // Energy weapons retain bayonets where supported, but cannot be silenced.
        if (!HasComp<HitscanBatteryAmmoProviderComponent>(uid) &&
            !HasComp<ProjectileBatteryAmmoProviderComponent>(uid) &&
            !(MetaData(uid).EntityPrototype is { } prototype && HasAncestor(prototype, "N14BaseWeaponPowerCell")))
            tags.Add("ReclamationGunSuppressor");
        if (SupportsTwoHands(uid))
            tags.Add("CombatKnife");
        component.MuzzleSlot.Whitelist = new EntityWhitelist { Tags = tags };
        _slots.AddItemSlot(uid, MuzzleSlotId, component.MuzzleSlot);
        if (SupportsTwoHands(uid))
        {
            var underbarrel = new List<ProtoId<TagPrototype>> { "ReclamationGunGrip" };
            if (SupportsBipod(uid)) underbarrel.Add("ReclamationGunBipod");
            component.GripSlot.Whitelist = new EntityWhitelist { Tags = underbarrel };
            _slots.AddItemSlot(uid, GripSlotId, component.GripSlot);
        }
        if (SupportsStock(uid)) _slots.AddItemSlot(uid, StockSlotId, component.StockSlot);
        _slots.AddItemSlot(uid, LaserSlotId, component.LaserSlot);
        if (SupportsHolo(uid)) _slots.AddItemSlot(uid, HoloSlotId, component.HoloSlot);
        _slots.AddItemSlot(uid, LightSlotId, component.LightSlot);
        // As in RMC, a light inside an attachment container must still illuminate the world.
        var lightContainer = _containers.EnsureContainer<ContainerSlot>(uid, LightSlotId);
        lightContainer.OccludesLight = false;
    }

    private void OnRemove(EntityUid uid, GunAttachmentsComponent component, ComponentRemove args)
    {
        _slots.RemoveItemSlot(uid, component.MuzzleSlot);
        _slots.RemoveItemSlot(uid, component.GripSlot);
        _slots.RemoveItemSlot(uid, component.LightSlot);
        _slots.RemoveItemSlot(uid, component.StockSlot);
        _slots.RemoveItemSlot(uid, component.LaserSlot);
        _slots.RemoveItemSlot(uid, component.HoloSlot);
    }

    private void OnInserted(EntityUid uid, GunAttachmentsComponent component, EntInsertedIntoContainerMessage args)
        => AttachmentChanged(uid, component, args.Container.ID, args.Entity, true);

    private void OnRemoved(EntityUid uid, GunAttachmentsComponent component, EntRemovedFromContainerMessage args)
        => AttachmentChanged(uid, component, args.Container.ID, args.Entity, false);

    private void AttachmentChanged(EntityUid uid, GunAttachmentsComponent component, string slot, EntityUid item, bool inserted)
    {
        if (TerminatingOrDeleted(uid) || slot != MuzzleSlotId && slot != GripSlotId && slot != LightSlotId && slot != StockSlotId && slot != LaserSlotId && slot != HoloSlotId)
            return;
        var altered = new AttachableAlteredEvent(uid, inserted ? AttachableAlteredType.Attached : AttachableAlteredType.Detached);
        RaiseLocalEvent(item, ref altered);
        var changed = new GunAttachmentChangedEvent(slot, item, inserted);
        RaiseLocalEvent(uid, ref changed);
        _guns.RefreshModifiers(uid, rescaleRecoil: true);
        if (slot == GripSlotId)
        {
            SetBipod(uid, component, false);
            if (!inserted || !HasComp<GunBipodComponent>(item))
                _actions.RemoveAction(component.BipodAction);
            else if (_hands.IsHolding(Transform(uid).ParentUid, uid, out _))
                _actions.AddAction(Transform(uid).ParentUid, ref component.BipodAction, "ReclamationActionToggleGunBipod", uid);
            Dirty(uid, component);
        }
        if (slot != LightSlotId)
            return;
        if (!inserted)
        {
            _actions.RemoveAction(component.LightAction);
            _actions.SetToggled(component.LightAction, false);
            return;
        }
        var holder = Transform(uid).ParentUid;
        if (_hands.IsHolding(holder, uid, out _))
        {
            _actions.AddAction(holder, ref component.LightAction, LightActionId, uid);
            _actions.SetToggled(component.LightAction, TryComp<UnpoweredFlashlightComponent>(item, out var light) && light.LightOn);
            Dirty(uid, component);
        }
    }

    private void OnDamage(EntityUid uid, GunAttachmentsComponent component, ref GetMeleeDamageEvent args)
    {
        if (!HasBayonet(component))
            return;
        args.Damage = new DamageSpecifier(GetTemplate().Damage);
        if (TryComp<WieldableComponent>(uid, out var wieldable) && wieldable.Wielded)
        {
            var bonus = (IncreaseDamageOnWieldComponent) _prototypes.Index<EntityPrototype>(TemplateId)
                .Components["IncreaseDamageOnWield"].Component;
            args.Damage += bonus.BonusDamage;
        }
    }

    private IEnumerable<EntityUid> Attachments(GunAttachmentsComponent component)
    {
        if (component.MuzzleSlot.Item is { } muzzle) yield return muzzle;
        if (component.GripSlot.Item is { } grip) yield return grip;
        if (component.LightSlot.Item is { } light) yield return light;
        if (component.StockSlot.Item is { } stock) yield return stock;
        if (component.LaserSlot.Item is { } laser) yield return laser;
        if (component.HoloSlot.Item is { } holo) yield return holo;
    }

    private void OnRefresh(EntityUid uid, GunAttachmentsComponent component, ref GunRefreshModifiersEvent args)
    {
        foreach (var item in Attachments(component))
        {
            var relay = new GunAttachmentRelayedEvent<GunRefreshModifiersEvent>(uid, args);
            RaiseLocalEvent(item, ref relay);
            args = relay.Args;
        }
    }

    private void OnAmmoSpread(EntityUid uid, GunAttachmentsComponent component, ref GunGetAmmoSpreadEvent args)
    {
        foreach (var item in Attachments(component))
        {
            var relay = new GunAttachmentRelayedEvent<GunGetAmmoSpreadEvent>(uid, args);
            RaiseLocalEvent(item, ref relay);
            args = relay.Args;
        }
    }

    private void OnMuzzleFlash(EntityUid uid, GunAttachmentsComponent component, ref GunMuzzleFlashAttemptEvent args)
    {
        foreach (var item in Attachments(component))
        {
            var relay = new GunAttachmentRelayedEvent<GunMuzzleFlashAttemptEvent>(uid, args);
            RaiseLocalEvent(item, ref relay);
            args = relay.Args;
        }
    }

    private void OnWielded(EntityUid uid, GunAttachmentsComponent component, ref ItemWieldedEvent args) => _guns.RefreshModifiers(uid, rescaleRecoil: true);
    private void OnUnwielded(EntityUid uid, GunAttachmentsComponent component, ItemUnwieldedEvent args) => _guns.RefreshModifiers(uid, rescaleRecoil: true);

    private void OnGetActions(EntityUid uid, GunAttachmentsComponent component, GetItemActionsEvent args)
    {
        if (!args.InHands) return;
        if (component.LightSlot.Item != null) args.AddAction(ref component.LightAction, LightActionId);
        if (component.GripSlot.Item is { } bipod && HasComp<GunBipodComponent>(bipod))
            args.AddAction(ref component.BipodAction, "ReclamationActionToggleGunBipod");
        Dirty(uid, component);
    }

    private void OnToggleBipod(EntityUid uid, GunAttachmentsComponent component, ToggleGunBipodActionEvent args)
    {
        if (args.Handled || !_hands.IsHolding(args.Performer, uid, out _) || component.GripSlot.Item is not { } item || !HasComp<GunBipodComponent>(item)) return;
        args.Handled = true;
        if (!component.BipodDeployed && (!TryComp<StandingStateComponent>(args.Performer, out var standing) || standing.CurrentState != StandingState.Lying))
        {
            _popup.PopupClient(Loc.GetString("gun-bipod-prone-required"), args.Performer, args.Performer);
            return;
        }
        SetBipod(uid, component, !component.BipodDeployed);
        _popup.PopupClient(Loc.GetString(component.BipodDeployed ? "gun-bipod-deployed" : "gun-bipod-folded"), args.Performer, args.Performer);
    }

    private void SetBipod(EntityUid uid, GunAttachmentsComponent component, bool deployed)
    {
        component.BipodDeployed = deployed;
        _actions.SetToggled(component.BipodAction, deployed);
        Dirty(uid, component);
        _guns.RefreshModifiers(uid, rescaleRecoil: true);
        var holder = Transform(uid).ParentUid;
        if (_hands.IsHolding(holder, uid, out _)) _movement.RefreshMovementSpeedModifiers(holder);
    }

    private void OnDropped(EntityUid uid, GunAttachmentsComponent component, GotUnequippedHandEvent args)
    {
        SetBipod(uid, component, false);
        _movement.RefreshMovementSpeedModifiers(args.User);
    }

    private void OnStood(EntityUid uid, StandingStateComponent component, StoodEvent args)
    {
        foreach (var gun in _hands.EnumerateHeld(uid))
            if (TryComp<GunAttachmentsComponent>(gun, out var attachments)) SetBipod(gun, attachments, false);
    }

    private void OnSpeed(EntityUid uid, GunAttachmentsComponent component, HeldRelayedEvent<RefreshMovementSpeedModifiersEvent> args)
    {
        if (!BipodActive(uid, component)) return;
        // Prone already applies its own movement multiplier. Cap at 10% instead of stacking to 1%.
        var prone = TryComp<LayingDownComponent>(args.Holder, out var laying) ? laying.LyingSpeedModifier : 1f;
        args.Args.ModifySpeed(prone > 0f ? Math.Min(1f, 0.1f / prone) : 1f);
    }

    private void OnToggleLight(EntityUid uid, GunAttachmentsComponent component, ToggleWeaponLightActionEvent args)
    {
        if (args.Handled || !_hands.IsHolding(args.Performer, uid, out _))
            return;
        ToggleLight(uid, component, args.Performer);
        args.Handled = true;
    }

    private void ToggleLight(EntityUid uid, GunAttachmentsComponent component, EntityUid user)
    {
        if (component.LightSlot.Item is not { } item || !TryComp<UnpoweredFlashlightComponent>(item, out var light))
            return;
        _lights.TryToggleLight((item, light), user);
        _actions.SetToggled(component.LightAction, light.LightOn);
    }

    private void OnLightVerb(EntityUid uid, GunAttachmentsComponent component, GetVerbsEvent<ActivationVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || component.LightSlot.Item == null)
            return;
        args.Verbs.Add(new ActivationVerb
        {
            Text = Loc.GetString("gun-attachment-toggle-light"),
            Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/light.svg.192dpi.png")),
            Act = () => ToggleLight(uid, component, args.User),
        });
    }

    private void OnExamine(EntityUid uid, GunAttachmentsComponent component, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;
        var names = new List<string>();
        foreach (var item in Attachments(component)) names.Add(Name(item));
        if (names.Count > 0)
            args.PushMarkup(Loc.GetString("gun-attachments-installed", ("items", string.Join(", ", names))));
    }
}
