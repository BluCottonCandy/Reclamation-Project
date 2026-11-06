using System.Linq;
using Content.Shared.DoAfter;
using Content.Shared._Misfits.Special;
using Content.Shared._Misfits.SpecialStats;
using Content.Shared._ReclamationProject.Agility;
using Content.Shared.ActionBlocker;
using Content.Shared.Actions;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Hands.Components;
using Content.Shared.Inventory;
using Content.Shared.Lock;
using Content.Shared.Mobs.Systems;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Tag;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Timing;

namespace Content.Server._ReclamationProject.Agility;

public sealed class AgilityFastHandsSystem : EntitySystem
{
    public static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(2);
    [Dependency] private readonly SharedSpecialSystem _special = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;
    [Dependency] private readonly MobStateSystem _mob = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly ItemSlotsSystem _slots = default!;
    [Dependency] private readonly SharedStorageSystem _storage = default!;
    [Dependency] private readonly SharedGunSystem _guns = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    private readonly HashSet<EntityUid> _active = new();
    private TimeSpan _nextUpdate;

    public override void Initialize()
    {
        SubscribeLocalEvent<SpecialChangedEvent>(OnSpecialChanged);
        SubscribeLocalEvent<SpecialStatsReadyEvent>(OnStatsReady);
        SubscribeLocalEvent<SpecialShutdownEvent>(OnSpecialShutdown);
        SubscribeLocalEvent<AgilityFastHandsComponent, AgilityFastHandsActionEvent>(OnActivate);
        SubscribeLocalEvent<AgilityFastHandsComponent, FastHandsReloadDoAfterEvent>(OnReloadStep);
    }

    private void OnSpecialChanged(ref SpecialChangedEvent args) => RefreshAction(args.ChangedEntity);
    private void OnStatsReady(ref SpecialStatsReadyEvent args) => RefreshAction(args.Entity);
    private void OnSpecialShutdown(ref SpecialShutdownEvent args) => RemoveAction(args.Entity);

    private void RefreshAction(EntityUid uid)
    {
        if (_special.GetEffective(uid, SpecialStat.Agility) < 10)
        {
            RemoveAction(uid);
            return;
        }
        var ability = EnsureComp<AgilityFastHandsComponent>(uid);
        if (ability.Action != null && !Deleted(ability.Action.Value))
            return;
        _actions.AddAction(uid, ref ability.Action, "ActionAgilityFastHands");
        if (ability.NextUseAt > _timing.CurTime)
            _actions.SetCooldown(ability.Action, ability.NextUseAt - Cooldown, ability.NextUseAt);
        Dirty(uid, ability);
    }

    private void RemoveAction(EntityUid uid)
    {
        StopReload(uid);
        if (!TryComp<AgilityFastHandsComponent>(uid, out var ability))
            return;
        ability.Reloading = false;
        _actions.RemoveAction(uid, ability.Action);
        if (ability.Action is { } action && !Deleted(action))
            QueueDel(action);
        ability.Action = null;
        Dirty(uid, ability);
    }

    private void OnActivate(Entity<AgilityFastHandsComponent> ent, ref AgilityFastHandsActionEvent args)
    {
        if (args.Handled || args.Performer != ent.Owner || ent.Comp.Reloading ||
            ent.Comp.NextUseAt > _timing.CurTime || _special.GetEffective(ent, SpecialStat.Agility) < 10 ||
            !_mob.IsAlive(ent) || !_blocker.CanInteract(ent, null))
            return;
        if (!SelectTarget(ent, out var target, out var gun))
        {
            var swapped = SwapOneMagazine(ent);
            var refilled = RefillSpareMagazines(ent);
            if (!swapped && !refilled)
                return;
            ent.Comp.NextUseAt = _timing.CurTime + Cooldown;
            _actions.SetCooldown(ent.Comp.Action, _timing.CurTime, ent.Comp.NextUseAt);
            Dirty(ent);
            args.Handled = true;
            return;
        }
        ent.Comp.ReloadTarget = target;
        ent.Comp.ReloadGun = gun;
        ent.Comp.RoundsLoaded = 0;
        var (count, capacity) = _guns.GetFastHandsAmmo(target);
        ent.Comp.RoundsRemaining = capacity - count;
        ent.Comp.Reloading = true;
        if (!ScheduleRound(ent, ent.Comp))
        {
            StopReload(ent);
            return;
        }
        _active.Add(ent);
        ent.Comp.NextUseAt = _timing.CurTime + Cooldown;
        _actions.SetCooldown(ent.Comp.Action, _timing.CurTime, ent.Comp.NextUseAt);
        _actions.SetToggled(ent.Comp.Action, true);
        Dirty(ent);
        args.Handled = true;
    }

    public override void Update(float frameTime)
    {
        if (_active.Count == 0 || _timing.CurTime < _nextUpdate)
            return;
        _nextUpdate = _timing.CurTime + TimeSpan.FromSeconds(0.1);
        foreach (var uid in _active.ToArray())
        {
            if (Deleted(uid) || !TryComp<AgilityFastHandsComponent>(uid, out var ability))
            {
                _active.Remove(uid);
                continue;
            }
            if (!Paused(uid) && !ValidReload(uid, ability))
                StopReload(uid);
        }
    }

    private bool ValidReload(EntityUid uid, AgilityFastHandsComponent ability) => ability.Reloading &&
        _mob.IsAlive(uid) && _special.GetEffective(uid, SpecialStat.Agility) >= 10 &&
        _blocker.CanInteract(uid, null) && ability.ReloadTarget is { } target &&
        GetCarriedItems(uid).Contains(target) && (ability.ReloadGun is not { } gun || IsHeld(uid, gun));

    private bool IsHeld(EntityUid user, EntityUid item) => TryComp<HandsComponent>(user, out var hands) &&
        hands.Hands.Values.Any(hand => hand.HeldEntity == item);

    private EntityUid? FindSource(EntityUid user, EntityUid target) => GetCarriedItems(user)
        .Where(source => !IsMagazine(source) && !HasComp<GunComponent>(source) && _guns.CanFastHandsFill(target, source))
        .Select(source => (EntityUid?) source).FirstOrDefault();

    private EntityUid? GetHeldGun(EntityUid user)
    {
        if (!TryComp<HandsComponent>(user, out var hands))
            return null;
        if (hands.ActiveHandEntity is { } active && HasComp<GunComponent>(active))
            return active;
        return hands.Hands.Values.Select(hand => hand.HeldEntity)
            .FirstOrDefault(item => item != null && HasComp<GunComponent>(item.Value));
    }

    private bool SelectTarget(EntityUid user, out EntityUid target, out EntityUid? gun)
    {
        target = default;
        gun = GetHeldGun(user);
        if (gun is not { } internalGun ||
            !(HasComp<BallisticAmmoProviderComponent>(internalGun) || HasComp<RevolverAmmoProviderComponent>(internalGun)))
            return false;
        target = internalGun;
        return FindSource(user, internalGun) != null;
    }

    private HashSet<EntityUid> LoadedMagazines(List<EntityUid> items) => items
        .Where(item => HasComp<GunComponent>(item)).Select(item => _guns.GetMagazineEntity(item))
        .Where(magazine => magazine != null).Select(magazine => magazine!.Value).ToHashSet();

    private void StowMagazine(EntityUid user, EntityUid magazine, List<EntityUid> items)
    {
        var backpacks = _inventory.GetSlotEnumerator(user, SlotFlags.BACK);
        while (backpacks.MoveNext(out var slot))
            if (slot.ContainedEntity is { } backpack && TryComp<StorageComponent>(backpack, out _) &&
                !(TryComp<LockComponent>(backpack, out var locked) && locked.Locked) &&
                _storage.Insert(backpack, magazine, out _, user: user))
                return;
        foreach (var item in items)
            if (TryComp<StorageComponent>(item, out _) &&
                !(TryComp<LockComponent>(item, out var locked) && locked.Locked) &&
                _storage.Insert(item, magazine, out _, user: user))
                return;
    }

    private bool SwapOneMagazine(EntityUid user)
    {
        if (GetHeldGun(user) is not { } gun || !_slots.TryGetSlot(gun, "gun_magazine", out var slot))
            return false;
        var items = GetCarriedItems(user);
        var loaded = LoadedMagazines(items);
        var current = _guns.GetMagazineEntity(gun);
        var candidate = items.Where(IsMagazine).Where(item => !loaded.Contains(item) &&
                _slots.CanInsert(gun, item, user, slot, swap: true))
            .OrderByDescending(item => _guns.GetFastHandsAmmo(item).Count)
            .Select(item => (EntityUid?) item).FirstOrDefault();
        if (candidate is not { } replacement || current is { } oldMagazine &&
            _guns.GetFastHandsAmmo(replacement).Count < _guns.GetFastHandsAmmo(oldMagazine).Count)
            return false;
        if (current != null && !_slots.TryEject(gun, slot, user, out _))
            return false;
        if (!_slots.TryInsert(gun, slot, replacement, user))
        {
            if (current is { } restore)
                _slots.TryInsert(gun, slot, restore, user);
            return false;
        }
        if (current is { } ejected)
            StowMagazine(user, ejected, items);
        _guns.FastHandsChamber(gun, user);
        return true;
    }

    private bool RefillSpareMagazines(EntityUid user)
    {
        var items = GetCarriedItems(user);
        var loaded = LoadedMagazines(items);
        var changed = false;
        foreach (var target in items.Where(IsMagazine).Where(item => !loaded.Contains(item)))
        {
            foreach (var source in GetCarriedItems(user))
            {
                if (IsMagazine(source) || HasComp<GunComponent>(source))
                    continue;
                var (count, capacity) = _guns.GetFastHandsAmmo(target);
                // A loose cartridge can move only once; boxed ammo may supply multiple rounds.
                var rounds = HasComp<BallisticAmmoProviderComponent>(source) ? capacity - count : 1;
                for (var i = 0; i < rounds; i++)
                {
                    if (!_guns.TryFastHandsFill(target, source, user))
                        break;
                    changed = true;
                }
            }
        }
        return changed;
    }

    private bool ScheduleRound(EntityUid user, AgilityFastHandsComponent ability)
    {
        if (ability.RoundsRemaining <= 0 || !ValidReload(user, ability) ||
            ability.ReloadTarget is not { } target || FindSource(user, target) is not { } source)
            return false;
        var loading = new DoAfterArgs(EntityManager, user, _guns.GetFastHandsReloadDelay(source),
            new FastHandsReloadDoAfterEvent(), user, target: target, used: source)
        {
            NeedHand = false,
            BreakOnDropItem = false,
            BreakOnMove = false,
            BreakOnDamage = false,
            MultiplyDelay = false,
            BlockDuplicate = false,
            CancelDuplicate = false,
        };
        return _doAfter.TryStartDoAfter(loading, out ability.DoAfter);
    }

    private void OnReloadStep(Entity<AgilityFastHandsComponent> ent, ref FastHandsReloadDoAfterEvent args)
    {
        if (args.Handled || !ent.Comp.Reloading || ent.Comp.DoAfter is not { } id || id != args.DoAfter.Id)
            return;
        args.Handled = true;
        ent.Comp.DoAfter = null;
        if (args.Cancelled || !ValidReload(ent, ent.Comp))
        {
            StopReload(ent);
            return;
        }
        if (args.Target is { } target && args.Used is { } source && GetCarriedItems(ent).Contains(source) &&
            _guns.TryFastHandsFill(target, source, ent))
        {
            ent.Comp.RoundsLoaded++;
            ent.Comp.RoundsRemaining--;
        }
        else
        {
            FinishReload(ent, ent.Comp);
            return;
        }
        if (!ScheduleRound(ent, ent.Comp))
            FinishReload(ent, ent.Comp);
    }

    private void StopReload(EntityUid user)
    {
        _active.Remove(user);
        if (!TryComp<AgilityFastHandsComponent>(user, out var ability))
            return;
        var id = ability.DoAfter;
        ability.DoAfter = null;
        ability.Reloading = false;
        ability.ReloadTarget = null;
        ability.ReloadGun = null;
        ability.RoundsLoaded = 0;
        ability.RoundsRemaining = 0;
        _doAfter.Cancel(id);
        _actions.SetToggled(ability.Action, false);
        Dirty(user, ability);
    }

    private void FinishReload(EntityUid user, AgilityFastHandsComponent ability)
    {
        var gun = ability.ReloadGun;
        var loaded = ability.RoundsLoaded;
        StopReload(user);
        if (loaded > 0 && gun is { } heldGun && IsHeld(user, heldGun))
            _guns.FastHandsChamber(heldGun, user);
    }

    /// <summary>Only the owner's hands, inventory and accessible nested storage; never the world.</summary>
    public List<EntityUid> GetCarriedItems(EntityUid user)
    {
        var result = new List<EntityUid>();
        var visited = new HashSet<EntityUid>();
        void Visit(EntityUid item)
        {
            if (Deleted(item) || !visited.Add(item))
                return;
            result.Add(item);
            if (HasComp<GunComponent>(item))
            {
                if (_guns.GetMagazineEntity(item) is { } magazine)
                    Visit(magazine);
                return;
            }
            if (TryComp<LockComponent>(item, out var locked) && locked.Locked)
                return;
            if (TryComp<StorageComponent>(item, out var storage))
                foreach (var child in storage.Container.ContainedEntities.ToArray())
                    Visit(child);
            if (TryComp<ItemSlotsComponent>(item, out var itemSlots))
                foreach (var itemSlot in itemSlots.Slots.Values)
                    if (!itemSlot.Locked && itemSlot.Item is { } child)
                        Visit(child);
        }
        if (TryComp<HandsComponent>(user, out var hands))
            foreach (var hand in hands.SortedHands)
                if (hands.Hands[hand].HeldEntity is { } held)
                    Visit(held);
        var slots = _inventory.GetSlotEnumerator(user);
        while (slots.MoveNext(out var slot))
            if (slot.ContainedEntity is { } item)
                Visit(item);
        return result;
    }

    private bool IsMagazine(EntityUid uid) => !HasComp<GunComponent>(uid) &&
        HasComp<BallisticAmmoProviderComponent>(uid) && TryComp<TagComponent>(uid, out var tags) &&
        tags.Tags.Any(tag => tag.Id.Contains("Magazine", StringComparison.OrdinalIgnoreCase));

}
