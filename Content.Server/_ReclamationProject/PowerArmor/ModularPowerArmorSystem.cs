using Content.Server.Explosion.Components;
using Content.Shared._Misfits.PowerArmor;
using Content.Shared._ReclamationProject.PowerArmor;
using Content.Shared.Alert;
using Content.Shared.Armor;
using Content.Shared.Clothing;
using Content.Shared.Clothing.Components;
using Content.Shared.Clothing.EntitySystems;
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
                foreach (var slot in PowerArmorPlates.Slots)
                {
                    var key = PowerArmorPlates.Container(slot);
                    if (_slots.GetItemOrNull(ent, key) != null) continue;
                    var piece = Spawn("ReclamationPlate" + slot, Transform(ent).Coordinates);
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
        if (!ev.Container.ID.StartsWith("plate-")) return;
        if (!TryComp<PowerArmorPlateComponent>(ev.EntityUid, out var plate)
            || PowerArmorPlates.Container(plate.Slot) != ev.Container.ID
            || Comp<DamageableComponent>(ev.EntityUid).TotalDamage >= plate.HitPoints)
            ev.Cancel();
    }

    private void OnInserted(Entity<ModularPowerArmorComponent> ent, ref EntInsertedIntoContainerMessage ev)
    {
        if (ev.Container.ID.StartsWith("plate-")) Recalculate(ent);
    }
    private void OnRemoved(Entity<ModularPowerArmorComponent> ent, ref EntRemovedFromContainerMessage ev)
    {
        if (ev.Container.ID.StartsWith("plate-") && !TerminatingOrDeleted(ent)) Recalculate(ent);
    }

    // Blasts calculate worn explosion resistance upstream and bypass the ordinary armor relay.
    private void OnBlast(Entity<PowerArmorWornComponent> wearer, ref Content.Shared.Explosion.BeforeExplodeEvent ev)
    {
        if (!HasComp<ModularPowerArmorComponent>(wearer.Comp.Armor) || !ev.Damage.AnyPositive()) return;
        if (_plateDamage.ImpactPlate(wearer.Comp.Armor) is {} plate)
            _damage.TryChangeDamage(plate, new DamageSpecifier(ev.Damage), ignoreResistances: true, doPartDamage: false);
    }

    private void OnWear(Entity<PowerArmorPlateComponent> ent, ref DamageChangedEvent ev)
    {
        if (ev.Damageable.TotalDamage < ent.Comp.HitPoints || ent.Comp.HitPoints == 0) return;
        var frame = Transform(ent).ParentUid;
        if (!HasComp<ModularPowerArmorComponent>(frame)) return;
        var id = PowerArmorPlates.Container(ent.Comp.Slot);
        if (_slots.GetItemOrNull(frame, id) != ent.Owner) return;
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
        _alerts.ShowAlert(args.Wearer, Alert);
        if (!TryComp<ToggleableClothingComponent>(ent, out var kit)) return;
        foreach (var (piece, slot) in kit.ClothingUids)
        {
            if ((slot is "gloves" or "shoes" or "head") && !_inventory.TryGetSlotEntity(args.Wearer, slot, out _))
                _inventory.TryEquip(args.Wearer, piece, slot, silent: true, force: true);
        }
    }

    private void OnUnequip(Entity<ModularPowerArmorComponent> ent, ref ClothingGotUnequippedEvent args)
    {
        _alerts.ClearAlert(args.Wearer, Alert);
        RemCompDeferred<PowerArmorWornComponent>(args.Wearer);
    }

    private void OnExamine(Entity<ModularPowerArmorComponent> ent, ref ExaminedEvent ev)
    {
        if (ev.IsInDetailsRange)
            ev.PushMarkup(Loc.GetString(ent.Comp.Sealed ? "pa-frame-sealed" : "pa-frame-open",
                ("rating", ent.Comp.Rating)));
    }
    private void OnExaminePlate(Entity<PowerArmorPlateComponent> ent, ref ExaminedEvent ev)
    {
        if (ev.IsInDetailsRange)
            ev.PushMarkup(Loc.GetString("pa-plate-status", ("rating", ent.Comp.Rating),
                ("hp", Math.Max(0, ent.Comp.HitPoints - (int) Math.Ceiling(Comp<DamageableComponent>(ent).TotalDamage.Float()))),
                ("max", ent.Comp.HitPoints)));
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
        foreach (var slot in PowerArmorPlates.Slots)
        {
            var item = _slots.GetItemOrNull(ent, PowerArmorPlates.Container(slot));
            if (!TryComp<PowerArmorPlateComponent>(item, out var plate)) continue;
            var donor = _protos.Index(plate.Model);
            var share = PowerArmorPlates.Share(slot);
            var accessorySlot = slot switch { ArmorPlateSlot.Chest => "shoes", ArmorPlateSlot.Gloves => "gloves", _ => null };
            if (accessorySlot != null && donor.TryGetComponent<ToggleableClothingComponent>(out var gear, _components)
                && gear.ClothingPrototypes.TryGetValue(accessorySlot, out var accessory) &&
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
            ent.Comp.Visuals[slot] = _protos.Index<PowerArmorModelPrototype>(donor.ID).Sprite;
        }
        foreach (var accessory in accessories)
            foreach (var (type, coefficient) in accessory.Modifiers.Coefficients)
                modifiers.Coefficients[type] = modifiers.Coefficients.GetValueOrDefault(type, 1f) * coefficient;
        var current = Comp<ArmorComponent>(ent);
        current.Modifiers = modifiers;
        Dirty(ent, current);
        Comp<ExplosionResistanceComponent>(ent).DamageCoefficient = blast;
        UpdateHelmet(ent);
        UpdateDeflection(ent);
        ent.Comp.Rating = rating;
        ent.Comp.Absorption = absorption;
        ent.Comp.Sealed = ent.Comp.Visuals.Count == 4;
        if (ent.Comp.Sealed && TryComp<ClothingComponent>(ent, out var clothing) && clothing.InSlot == "outerClothing")
            _fire.Extinguish(Transform(ent).ParentUid);
        Dirty(ent);
    }

    private void UpdateHelmet(Entity<ModularPowerArmorComponent> ent)
    {
        if (!TryComp<ToggleableClothingComponent>(ent, out var kit)) return;
        EntProtoId? desired = null;
        if (_slots.GetItemOrNull(ent, PowerArmorPlates.Container(ArmorPlateSlot.Helmet)) is {} piece &&
            _protos.Index(Comp<PowerArmorPlateComponent>(piece).Model)
                .TryGetComponent<ToggleableClothingComponent>(out var source, _components) &&
            source.ClothingPrototypes.TryGetValue("head", out var helmet))
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
