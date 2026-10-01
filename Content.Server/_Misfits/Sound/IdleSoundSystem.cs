// Misfits Change - System to suppress idle sounds during combat and when dead
using Content.Shared.Audio;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Sound;
using Content.Shared.Sound.Components;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Collections;

namespace Content.Server._Misfits.Sound;

/// <summary>
/// Temporarily disables <see cref="SpamEmitSoundComponent"/> when an entity with
/// <see cref="IdleSoundComponent"/> performs an attack, then re-enables it after a cooldown.
/// Also permanently disables idle sounds when the entity is no longer alive.
/// </summary>
public sealed class IdleSoundSystem : EntitySystem
{
    [Dependency] private readonly SharedAmbientSoundSystem _ambient = default!;
    [Dependency] private readonly SharedEmitSoundSystem _emitSound = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;

    // Misfits Fix: only track temporarily-suppressed entities so Update is O(suppressed) not O(all_NPCs).
    // Dead entities are NOT in this set; they are permanently silenced via OnMobStateChanged.
    private readonly HashSet<EntityUid> _suppressedEntities = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<IdleSoundComponent, MeleeAttackEvent>(OnMeleeAttack);
        SubscribeLocalEvent<IdleSoundComponent, MobStateChangedEvent>(OnMobStateChanged);
        // Misfits Fix: clean up tracking when entity is removed.
        SubscribeLocalEvent<IdleSoundComponent, ComponentShutdown>(OnIdleShutdown);
    }

    private void OnIdleShutdown(EntityUid uid, IdleSoundComponent _, ComponentShutdown args)
    {
        _suppressedEntities.Remove(uid);
    }

    private void OnMeleeAttack(Entity<IdleSoundComponent> entity, ref MeleeAttackEvent args)
    {
        Suppress(entity);
    }

    private void OnMobStateChanged(Entity<IdleSoundComponent> entity, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Alive)
        {
            // Permanently disable — mob is dead or incapacitated.
            // Remove from suppressed-timer tracking; no cooldown needed since it stays silent.
            entity.Comp.Suppressed = true;
            entity.Comp.CooldownRemaining = 0f;
            _suppressedEntities.Remove(entity.Owner);
            _emitSound.SetEnabled((entity.Owner, (SpamEmitSoundComponent?) null), false);

            // #Misfits Fix — also silence AmbientSound (e.g. Eyebot music loop) on death.
            _ambient.SetAmbience(entity.Owner, false);
        }
        else
        {
            // Mob came back to life; let idle sounds resume.
            entity.Comp.Suppressed = false;
            _suppressedEntities.Remove(entity.Owner);
            _emitSound.SetEnabled((entity.Owner, (SpamEmitSoundComponent?) null), true);

            // #Misfits Fix — re-enable AmbientSound on revive.
            _ambient.SetAmbience(entity.Owner, true);
        }
    }

    private void Suppress(Entity<IdleSoundComponent> entity)
    {
        entity.Comp.CooldownRemaining = entity.Comp.CooldownDuration;

        if (entity.Comp.Suppressed)
            return;

        entity.Comp.Suppressed = true;
        // Misfits Fix: register for per-tick cooldown tracking.
        _suppressedEntities.Add(entity.Owner);
        _emitSound.SetEnabled((entity.Owner, (SpamEmitSoundComponent?) null), false);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Misfits Fix: iterate only temporarily-suppressed entities instead of all IdleSoundComponent
        // entities — reduces from O(all_NPCs) to O(currently-suppressed_NPCs) per tick.
        var toRemove = new ValueList<EntityUid>();
        foreach (var uid in _suppressedEntities)
        {
            if (!TryComp<IdleSoundComponent>(uid, out var idle))
            {
                toRemove.Add(uid);
                continue;
            }

            idle.CooldownRemaining -= frameTime;

            if (idle.CooldownRemaining > 0f)
                continue;

            // Cooldown expired — do not re-enable if mob is no longer alive.
            if (!_mobState.IsAlive(uid))
            {
                toRemove.Add(uid);
                continue;
            }

            idle.Suppressed = false;
            toRemove.Add(uid);
            _emitSound.SetEnabled((uid, (SpamEmitSoundComponent?) null), true);
        }

        foreach (var uid in toRemove)
            _suppressedEntities.Remove(uid);
    }
}
