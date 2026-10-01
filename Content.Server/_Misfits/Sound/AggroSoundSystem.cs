// Misfits Change - System to play aggro/alert sounds on combat entry, separate from idle ambient sounds
using Content.Shared._Misfits.Sound;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Server.NPC.Components;
using Robust.Server.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Collections;
using Robust.Shared.Random;

namespace Content.Server._Misfits.Sound;

/// <summary>
/// Plays an aggro/alert sound the first time an entity with
/// <see cref="AggroSoundComponent"/> attacks (melee or ranged), with a cooldown
/// to prevent spam. Keeps combat vocalizations separate from idle ambient sounds.
/// </summary>
public sealed class AggroSoundSystem : EntitySystem
{
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    // Misfits Fix: only track entities with an active cooldown so Update is O(active) not O(all_NPCs).
    private readonly HashSet<EntityUid> _activeCooldowns = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<AggroSoundComponent, MeleeAttackEvent>(OnMeleeAttack);
        SubscribeLocalEvent<AggroSoundComponent, GunShotEvent>(OnGunShot);
        SubscribeLocalEvent<NPCMeleeCombatComponent, ComponentInit>(OnMeleeCombatStartup);
        SubscribeLocalEvent<NPCRangedCombatComponent, ComponentInit>(OnRangedCombatStartup);
        // Misfits Fix: clean up when entity is removed so the set doesn't leak stale UIDs.
        SubscribeLocalEvent<AggroSoundComponent, ComponentShutdown>(OnAggroShutdown);
    }

    private void OnAggroShutdown(EntityUid uid, AggroSoundComponent _, ComponentShutdown args)
    {
        _activeCooldowns.Remove(uid);
    }

    private void OnMeleeAttack(Entity<AggroSoundComponent> entity, ref MeleeAttackEvent args)
    {
        TryPlayAggro(entity);
    }

    private void OnGunShot(Entity<AggroSoundComponent> entity, ref GunShotEvent args)
    {
        // GunShotEvent fires on the gun entity. For mobs that ARE their own gun
        // (Gun component directly on the mob), this fires on the mob itself.
        TryPlayAggro(entity);
    }

    // Misfits Change /Fix: Prime the aggro icon as soon as hostile NPC combat starts,
    // so ranged mobs like assaultrons show the exclamation mark on aggro instead of only after their first attack.
    private void OnMeleeCombatStartup(EntityUid uid, NPCMeleeCombatComponent component, ComponentInit args)
    {
        if (TryComp<AggroSoundComponent>(uid, out var aggro))
            TryPlayAggro((uid, aggro));
    }

    private void OnRangedCombatStartup(EntityUid uid, NPCRangedCombatComponent component, ComponentInit args)
    {
        if (TryComp<AggroSoundComponent>(uid, out var aggro))
            TryPlayAggro((uid, aggro));
    }

    private void TryPlayAggro(Entity<AggroSoundComponent> entity)
    {
        // #Misfits Fix — dead mobs should not play aggro sounds.
        if (TryComp<MobStateComponent>(entity.Owner, out var mobState) && mobState.CurrentState == MobState.Dead)
            return;

        if (entity.Comp.CooldownRemaining > 0f)
            return;

        _audio.PlayPvs(entity.Comp.Sound, entity.Owner);
        // Pick a random cooldown each play so mobs in a group do not vocalize in sync.
        entity.Comp.CooldownRemaining = _random.NextFloat(entity.Comp.CooldownMin, entity.Comp.CooldownMax);
        // Misfits Fix: register this entity in the active-cooldown set so Update only processes it while hot.
        _activeCooldowns.Add(entity.Owner);
        // Misfits Change /Fix: Dirty the component so clients see the updated CooldownRemaining
        // and the aggro status icon (ShowAggroIconSystem) appears correctly.
        Dirty(entity.Owner, entity.Comp);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Misfits Fix: iterate only entities with an active cooldown instead of ALL AggroSoundComponent
        // entities — reduces from O(all_NPCs) to O(currently-attacking_NPCs) every tick.
        var toRemove = new ValueList<EntityUid>();
        foreach (var uid in _activeCooldowns)
        {
            if (!TryComp<AggroSoundComponent>(uid, out var aggro))
            {
                toRemove.Add(uid);
                continue;
            }

            aggro.CooldownRemaining -= frameTime;

            if (aggro.CooldownRemaining > 0f)
                continue;

            aggro.CooldownRemaining = 0f;
            toRemove.Add(uid);
            // Misfits Change /Fix: Dirty when cooldown expires so clients hide the aggro icon.
            Dirty(uid, aggro);
        }

        foreach (var uid in toRemove)
            _activeCooldowns.Remove(uid);
    }
}
