using Content.Server.Explosion.Components;
using Content.Server.Atmos.Components;
using Content.Shared._Misfits.PowerArmor;
using Content.Shared._ReclamationProject.PowerArmor;
using Content.Shared.Alert;
using Content.Shared.Armor;
using Content.Shared.Clothing;
using Content.Shared.Clothing.Components;
using Content.Shared.Clothing.EntitySystems;
using Content.Shared._ReclamationProject.Medical;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Examine;
using Content.Shared.Inventory;
using Content.Shared.Popups;
using Content.Shared.Whitelist;
using Content.Shared.Weapons.Reflect;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server._ReclamationProject.PowerArmor;

public sealed class ModularPowerArmorSystem : EntitySystem
{
    [Dependency] private readonly ItemSlotsSystem _slots = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly AlertsSystem _alerts = default!;
    [Dependency] private readonly MetaDataSystem _meta = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly Content.Server.Atmos.EntitySystems.FlammableSystem _fire = default!;
    [Dependency] private readonly ToggleableClothingSystem _attachments = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IPrototypeManager _protos = default!;
    [Dependency] private readonly IComponentFactory _components = default!;
    [Dependency] private readonly ModularArmorDamageSystem _plateDamage = default!;
    [Dependency] private readonly DamageableSystem _damage = default!;
    [Dependency] private readonly FireProtectionSystem _fireProtection = default!;
    [Dependency] private readonly TraumaSurgerySystem _walkingSupport = default!;
    [Dependency] private readonly PowerArmorModSystem _mods = default!;
    private const string Alert = "ModularPowerArmorRating";

    public override void Initialize()
    {
        SubscribeLocalEvent<N14PowerArmorComponent, ComponentInit>(OnInitialize);
        SubscribeLocalEvent<ModularPowerArmorComponent, MapInitEvent>(OnSpawn, before: new[] { typeof(ToggleableClothingSystem) });
        SubscribeLocalEvent<ModularPowerArmorComponent, EntInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<ModularPowerArmorComponent, EntRemovedFromContainerMessage>(OnRemoved);
        SubscribeLocalEvent<ModularPowerArmorComponent, ContainerIsInsertingAttemptEvent>(OnInsertAttempt);
        SubscribeLocalEvent<ModularPowerArmorComponent, ClothingGotEquippedEvent>(OnEquip);
        SubscribeLocalEvent<ModularPowerArmorComponent, ClothingGotUnequippedEvent>(OnUnequip);
        SubscribeLocalEvent<ModularPowerArmorComponent, ExaminedEvent>(OnExamine);
        SubscribeLocalEvent<PowerArmorWornComponent, Content.Shared.Explosion.BeforeExplodeEvent>(OnBlast);
        SubscribeLocalEvent<PowerArmorPlateComponent, MapInitEvent>(OnPlateSpawn, before: new[] { typeof(ToggleableClothingSystem) });
        SubscribeLocalEvent<PowerArmorPlateComponent, ExaminedEvent>(OnExaminePlate);
        SubscribeLocalEvent<PowerArmorPlateComponent, DamageChangedEvent>(OnWear);
        SubscribeLocalEvent<PowerArmorWornComponent, BeforeDamageChangedEvent>(OnWeaponFire);
        SubscribeLocalEvent<ModularPowerArmorComponent, BeforeDamageChangedEvent>(OnWeaponFire);
        SubscribeLocalEvent<PowerArmorPlateComponent, BeforeDamageChangedEvent>(OnWeaponFire);
    }

    // Incendiary rounds and burning melee weapons can deliver fire and physical impact in the same hit.
    private void OnWeaponFire<T>(Entity<T> target, ref BeforeDamageChangedEvent ev) where T : IComponent
    {
        if (ev.DamageSource is not {} source || ev.Damage.DamageDict.GetValueOrDefault("Heat") <= 0)
            return;
        if (!(TryComp<IgniteOnCollideComponent>(source, out var projectile) && projectile.FireStacks > 0) &&
            !(TryComp<IgniteOnMeleeHitComponent>(source, out var melee) && melee.FireStacks > 0))
            return;
        if (!_fireProtection.GetProtection(target).IsSealed)
            return;
        ev.Damage = new DamageSpecifier(ev.Damage);
        ev.Damage.DamageDict.Remove("Heat");
    }

    private void OnInitialize(Entity<N14PowerArmorComponent> ent, ref ComponentInit args)
    {
        var id = MetaData(ent).EntityPrototype?.ID;
        if (id == null || !_protos.HasIndex<PowerArmorModelPrototype>(id) && !HasComp<ModularPowerArmorComponent>(ent))
            return;

        EnsureComp<ModularPowerArmorComponent>(ent);
        var slots = EnsureComp<ItemSlotsComponent>(ent);
        if (TryComp<PowerArmorIntegrityComponent>(ent, out var integrity))
            integrity.ExternalHandling = true;
        foreach (var slot in PowerArmorPlates.Slots)
        {
            var key = PowerArmorPlates.Container(slot);
            if (!_slots.TryGetSlot(ent, key, out _, slots))
                _slots.AddItemSlot(ent, key, new ItemSlot
                {
                    Name = "pa-slot-" + slot.ToString().ToLowerInvariant(),
                    Swap = false,
                    Whitelist = new EntityWhitelist { Tags = new() { "PowerArmorPlate" + slot } }
                });
        }
    }

    private void OnSpawn(Entity<ModularPowerArmorComponent> ent, ref MapInitEvent args)
    {
        if (TryComp<ToggleableClothingComponent>(ent, out var kit))
        {
            _attachments.SetAttachmentPrototype((ent, kit), "gloves", "ReclamationFrameGauntlets");
            _attachments.SetAttachmentPrototype((ent, kit), "shoes", "ReclamationFrameBoots");
        }
        if (!ent.Comp.FactoryPartsCreated)
        {
            ent.Comp.FactoryPartsCreated = true;
            if (!ent.Comp.StartsEmpty && MetaData(ent).EntityPrototype is {} donor)
            {
                var model = _protos.Index<PowerArmorModelPrototype>(donor.ID);
                foreach (var slot in PowerArmorPlates.Slots)
                {
                    var key = PowerArmorPlates.Container(slot);
                    if (_slots.GetItemOrNull(ent, key) != null) continue;
                    var piece = Spawn(model.Parts.GetValueOrDefault(slot, "ReclamationPlate" + slot), Transform(ent).Coordinates);
                    var plate = Comp<PowerArmorPlateComponent>(piece);
                    plate.Model = donor.ID;
                    ConfigurePlate(piece, plate);
                    if (!_slots.TryInsert(ent, key, piece, null))
                    {
                        QueueDel(piece);
                        continue;
                    }
                    // Carry a saved pre-modular suit's accumulated wear into the replacement plates.
                    if (TryComp<DamageableComponent>(ent, out var oldSuit) && oldSuit.TotalDamage > 0)
                        _damage.TryChangeDamage(piece, oldSuit.Damage * PowerArmorPlates.Share(slot), ignoreResistances: true, doPartDamage: false);
                }
            }
        }
        Recalculate(ent);
    }

    private void OnPlateSpawn(Entity<PowerArmorPlateComponent> ent, ref MapInitEvent args) => ConfigurePlate(ent, ent.Comp);

    private void ConfigurePlate(EntityUid uid, PowerArmorPlateComponent plate)
    {
        var donor = _protos.Index(plate.Model);
        var model = _protos.Index<PowerArmorModelPrototype>(donor.ID);
        if (!donor.TryGetComponent<PowerArmorIntegrityComponent>(out var health, _components))
            throw new InvalidOperationException($"Missing power armor integrity on {donor.ID}");
        plate.HitPoints = PowerArmorPlates.Portion(health.MaxIntegrity.Int(), plate.Slot);
        plate.Rating = PowerArmorPlates.Portion(model.Rating - 20, plate.Slot);
        _meta.SetEntityName(uid, Loc.GetString("pa-piece-name", ("model", donor.Name),
            ("slot", Loc.GetString("pa-slot-" + plate.Slot.ToString().ToLowerInvariant()))));
        Dirty(uid, plate);
    }

    private void OnInsertAttempt(Entity<ModularPowerArmorComponent> ent, ref ContainerIsInsertingAttemptEvent ev)
    {
        if (!IsPlateSlot(ev.Container.ID)) return;
        if (!TryComp<PowerArmorPlateComponent>(ev.EntityUid, out var plate)
            || PowerArmorPlates.Container(plate.Slot) != ev.Container.ID
            || Comp<DamageableComponent>(ev.EntityUid).TotalDamage >= plate.HitPoints
            || !_mods.AcceptsPlate(ent, ev.EntityUid))
            ev.Cancel();
    }

    private void OnInserted(Entity<ModularPowerArmorComponent> ent, ref EntInsertedIntoContainerMessage ev)
    {
        if (IsPlateSlot(ev.Container.ID)) Recalculate(ent);
    }
    private void OnRemoved(Entity<ModularPowerArmorComponent> ent, ref EntRemovedFromContainerMessage ev)
    {
        if (IsPlateSlot(ev.Container.ID) && !TerminatingOrDeleted(ent)) Recalculate(ent);
    }

    private static bool IsPlateSlot(string id) => id.StartsWith("plate-", StringComparison.Ordinal);

    private static bool TryGetAttachment(ToggleableClothingComponent kit, string slot, out EntProtoId prototype)
    {
        IReadOnlyDictionary<string, EntProtoId> attachments = kit.ClothingPrototypes;
        return attachments.TryGetValue(slot, out prototype);
    }

    // Blasts calculate worn explosion resistance upstream and bypass the ordinary armor relay.
    private void OnBlast(Entity<PowerArmorWornComponent> wearer, ref Content.Shared.Explosion.BeforeExplodeEvent ev)
    {
        if (!TryComp<ModularPowerArmorComponent>(wearer.Comp.Armor, out var frame)) return;
        // The incinerator's fireball resolves as an explosion before normal fire protection runs.
        if (frame.Sealed && TryComp<ClothingComponent>(wearer.Comp.Armor, out var clothing) &&
            clothing.InSlot == "outerClothing" && _protos.Index<Content.Shared.Explosion.ExplosionPrototype>(ev.Id).FireStacks > 0)
            ev.Damage.DamageDict.Remove("Heat");
        if (!ev.Damage.AnyPositive()) return;
        if (_plateDamage.ImpactPlate(wearer.Comp.Armor) is {} plate)
            _damage.TryChangeDamage(plate, new DamageSpecifier(ev.Damage), ignoreResistances: true, doPartDamage: false);
    }

    private void OnWear(Entity<PowerArmorPlateComponent> ent, ref DamageChangedEvent ev)
    {
        if (ent.Comp.HitPoints == 0) return;
        var frame = Transform(ent).ParentUid;
        if (!HasComp<ModularPowerArmorComponent>(frame)) return;
        var id = PowerArmorPlates.Container(ent.Comp.Slot);
        if (_slots.GetItemOrNull(frame, id) != ent.Owner) return;
        if (ev.Damageable.TotalDamage < ent.Comp.HitPoints)
        {
            UpdateIntegrityAlert(frame);
            return;
        }
        if (_slots.TryEject(frame, id, null, out _))
        {
            _transform.DropNextTo(ent.Owner, frame);
            _popup.PopupEntity(Loc.GetString("pa-plate-breaks", ("plate", ent.Owner)), frame);
        }
    }

    private void OnEquip(Entity<ModularPowerArmorComponent> ent, ref ClothingGotEquippedEvent args)
    {
        EnsureComp<PowerArmorWornComponent>(args.Wearer).Armor = ent.Owner;
        Recalculate(ent);
        if (args.Clothing.InSlot == "outerClothing")
            _walkingSupport.RefreshWalkingSupport(args.Wearer);
        if (!TryComp<ToggleableClothingComponent>(ent, out var kit)) return;
        foreach (var (piece, slot) in kit.ClothingUids)
        {
            if ((slot is "gloves" or "shoes" or "head") && !_inventory.TryGetSlotEntity(args.Wearer, slot, out _))
                _inventory.TryEquip(args.Wearer, piece, slot, silent: true, force: true);
        }
        _mods.RefreshWearer(args.Wearer);
    }

    private void OnUnequip(Entity<ModularPowerArmorComponent> ent, ref ClothingGotUnequippedEvent args)
    {
        _mods.ClearWearer(args.Wearer);
        _alerts.ClearAlert(args.Wearer, Alert);
        RemComp<WornArmorConditionComponent>(args.Wearer);
        RemCompDeferred<PowerArmorWornComponent>(args.Wearer);
        if (args.Clothing.InSlot == "outerClothing")
            _walkingSupport.RefreshWalkingSupport(args.Wearer, ent.Owner);
    }

    private void OnExamine(Entity<ModularPowerArmorComponent> ent, ref ExaminedEvent ev)
    {
        if (ev.IsInDetailsRange)
            ev.PushMarkup(Loc.GetString(ent.Comp.Sealed ? "pa-frame-sealed" : "pa-frame-open",
                ("rating", ent.Comp.Rating)));
        if (ev.IsInDetailsRange) _mods.ExamineFrame(ent, ref ev);
    }
    private void OnExaminePlate(Entity<PowerArmorPlateComponent> ent, ref ExaminedEvent ev)
    {
        if (ev.IsInDetailsRange)
            ev.PushMarkup(Loc.GetString("pa-plate-status", ("rating", ent.Comp.Rating),
                ("hp", Math.Max(0, ent.Comp.HitPoints - (int) Math.Ceiling(Comp<DamageableComponent>(ent).TotalDamage.Float()))),
                ("max", ent.Comp.HitPoints)));
        if (ev.IsInDetailsRange) _mods.ExaminePlate(ent, ref ev);
    }

    private bool FitsGarageStencil(string modelId) =>
        _protos.TryIndex<PowerArmorModelPrototype>(modelId, out var model) && model.FitsGarageStencils;

    private IEnumerable<Entity<PowerArmorPlateComponent>> GetStencilPlates(EntityUid target)
    {
        if (TryComp<PowerArmorPlateComponent>(target, out var loose))
        {
            if (FitsGarageStencil(loose.Model.Id))
                yield return (target, loose);
            yield break;
        }
        if (!HasComp<ModularPowerArmorComponent>(target))
            yield break;
        foreach (var slot in PowerArmorPlates.Slots)
        {
            if (_slots.GetItemOrNull(target, PowerArmorPlates.Container(slot)) is {} piece &&
                TryComp<PowerArmorPlateComponent>(piece, out var plate) && FitsGarageStencil(plate.Model.Id))
                yield return (piece, plate);
        }
    }

    public bool CanApplyStencil(EntityUid target) => GetStencilPlates(target).Any();

    public bool TryApplyStencil(EntityUid target, string finish, Color? frameColor = null)
    {
        var frameUid = HasComp<ModularPowerArmorComponent>(target) ? target : Transform(target).ParentUid;
        var frame = CompOrNull<ModularPowerArmorComponent>(frameUid);
        var painted = 0;
        foreach (var plate in GetStencilPlates(target))
        {
            plate.Comp.PaintFinish = finish;
            Dirty(plate);
            painted++;
            if (frame == null)
                continue;
            frame.Visuals[plate.Comp.Slot] = finish;
            if (plate.Comp.Slot == ArmorPlateSlot.Helmet)
                frame.PaintedHelmet = true;
        }

        if (painted == 0)
            return false;
        if (frame != null)
        {
            if (frameUid == target && frameColor is {} color && painted == frame.Visuals.Count &&
                MetaData(target).EntityPrototype is {} donor && FitsGarageStencil(donor.ID))
                frame.FrameColor = color;
            Dirty(frameUid, frame);
        }
        return true;
    }

    private void Recalculate(Entity<ModularPowerArmorComponent> ent)
    {
        var modifiers = new DamageModifierSet();
        foreach (var key in new[] { "Blunt", "Slash", "Piercing" })
            modifiers.Coefficients[key] = 0.8f;
        var rating = 20;
        var blast = 0.8f;
        var absorption = 0f;
        var accessories = new List<ArmorComponent>();
        ent.Comp.Visuals.Clear();
        ent.Comp.PaintedHelmet = false;
        foreach (var slot in PowerArmorPlates.Slots)
        {
            var item = _slots.GetItemOrNull(ent, PowerArmorPlates.Container(slot));
            if (!TryComp<PowerArmorPlateComponent>(item, out var plate)) continue;
            var donor = _protos.Index(plate.Model);
            var share = PowerArmorPlates.Share(slot);
            var accessorySlot = slot switch { ArmorPlateSlot.Chest => "shoes", ArmorPlateSlot.Gloves => "gloves", _ => null };
            if (accessorySlot != null && donor.TryGetComponent<ToggleableClothingComponent>(out var gear, _components)
                && TryGetAttachment(gear, accessorySlot, out var accessory) &&
                _protos.Index(accessory).TryGetComponent<ArmorComponent>(out var extra, _components))
                accessories.Add(extra);
            rating += plate.Rating;
            if (!donor.TryGetComponent<ArmorComponent>(out var armor, _components) ||
                !donor.TryGetComponent<PowerArmorIntegrityComponent>(out var pool, _components)) continue;
            foreach (var (type, coefficient) in armor.Modifiers.Coefficients)
            {
                var baseline = type is "Blunt" or "Slash" or "Piercing" ? 0.8f : 1f;
                modifiers.Coefficients[type] = modifiers.Coefficients.GetValueOrDefault(type, 1f) + share * (coefficient - baseline);
            }
            absorption += share * (1f - pool.BleedthroughRatio);
            if (donor.TryGetComponent<ExplosionResistanceComponent>(out var resist, _components))
                blast += share * (resist.DamageCoefficient - 0.8f);
            ent.Comp.Visuals[slot] = plate.PaintFinish ?? _protos.Index<PowerArmorModelPrototype>(donor.ID).Sprite;
            if (slot == ArmorPlateSlot.Helmet)
                ent.Comp.PaintedHelmet = plate.PaintFinish != null;
        }
        foreach (var accessory in accessories)
            foreach (var (type, coefficient) in accessory.Modifiers.Coefficients)
                modifiers.Coefficients[type] = modifiers.Coefficients.GetValueOrDefault(type, 1f) * coefficient;
        var current = Comp<ArmorComponent>(ent);
        current.Modifiers = modifiers;
        Dirty(ent, current);
        Comp<ExplosionResistanceComponent>(ent).DamageCoefficient = blast;
        UpdateHelmet(ent);
        _mods.FrameChanged(ent);
        UpdateDeflection(ent);
        ent.Comp.Rating = rating;
        ent.Comp.Absorption = absorption;
        ent.Comp.Sealed = ent.Comp.Visuals.Count == 4;
        UpdateIntegrityAlert(ent);
        if (ent.Comp.Sealed && TryComp<ClothingComponent>(ent, out var clothing) && clothing.InSlot == "outerClothing")
            _fire.Extinguish(Transform(ent).ParentUid, force: true);
        Dirty(ent);
    }

    private void UpdateIntegrityAlert(EntityUid frame)
    {
        if (!TryComp<ClothingComponent>(frame, out var clothing) || clothing.InSlot != "outerClothing")
            return;

        var integrity = 0f;
        foreach (var slot in PowerArmorPlates.Slots)
        {
            var item = _slots.GetItemOrNull(frame, PowerArmorPlates.Container(slot));
            if (!TryComp<PowerArmorPlateComponent>(item, out var plate) || plate.HitPoints <= 0 ||
                !TryComp<DamageableComponent>(item, out var damage))
                continue;

            var remaining = Math.Clamp(1f - damage.TotalDamage.Float() / plate.HitPoints, 0f, 1f);
            integrity += PowerArmorPlates.Share(slot) * remaining;
        }

        var wearer = Transform(frame).ParentUid;
        var condition = EnsureComp<WornArmorConditionComponent>(wearer);
        integrity = Math.Clamp(integrity, 0f, 1f);
        if (condition.Fraction != integrity)
        {
            condition.Fraction = integrity;
            Dirty(wearer, condition);
        }

        var max = _alerts.GetMaxSeverity(Alert);
        // Reserve empty for a bare frame. Even a small hit should visibly interrupt a full gauge.
        var level = integrity > 0 ? Math.Clamp((int) MathF.Floor(integrity * max + 0.00001f), 1, max) : 0;
        _alerts.ShowAlert(wearer, Alert, (short) level);
    }

    private void UpdateHelmet(Entity<ModularPowerArmorComponent> ent)
    {
        if (!TryComp<ToggleableClothingComponent>(ent, out var kit)) return;
        EntProtoId? desired = null;
        if (_slots.GetItemOrNull(ent, PowerArmorPlates.Container(ArmorPlateSlot.Helmet)) is {} piece &&
            _protos.Index(Comp<PowerArmorPlateComponent>(piece).Model)
                .TryGetComponent<ToggleableClothingComponent>(out var source, _components) &&
            TryGetAttachment(source, "head", out var helmet))
            desired = helmet;
        EntityUid? wearer = TryComp<ClothingComponent>(ent, out var clothing) && clothing.InSlot == "outerClothing"
            ? Transform(ent).ParentUid : null;
        _attachments.SetAttachmentPrototype((ent, kit), "head", desired, wearer);
    }

    private void UpdateDeflection(Entity<ModularPowerArmorComponent> ent)
    {
        if (!TryComp<ReflectComponent>(ent, out var deflect)) return;
        var chance = 0f;
        var supported = ReflectType.None;
        var chances = new Dictionary<ReflectType, float>();
        foreach (var slot in PowerArmorPlates.Slots)
        {
            if (_slots.GetItemOrNull(ent, PowerArmorPlates.Container(slot)) is not {} piece ||
                !_protos.Index(Comp<PowerArmorPlateComponent>(piece).Model).TryGetComponent<ReflectComponent>(out var donor, _components))
                continue;
            var share = PowerArmorPlates.Share(slot);
            chance += donor.ReflectProb * share;
            supported |= donor.Reflects;
            foreach (var type in Enum.GetValues<ReflectType>())
            {
                if ((donor.Reflects & type) != 0)
                    chances[type] = chances.GetValueOrDefault(type) + share * donor.ReflectProbByType.GetValueOrDefault(type, donor.ReflectProb);
            }
        }
        deflect.ReflectProb = chance;
        deflect.Reflects = supported;
        deflect.ReflectProbByType = chances;
        Dirty(ent, deflect);
    }
}
