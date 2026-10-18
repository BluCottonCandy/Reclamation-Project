using Content.Server._Misfits.Silicon;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN; // #Misfits Add
using Content.Shared._Misfits.C27;
using Content.Shared._Misfits.Silicon;
using Content.Shared.CombatMode;
using Content.Shared.Damage;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Robust.Shared.Collections;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server.NPC.Systems;

/// <summary>
///     Handles NPC which become aggressive after being attacked.
/// </summary>
public sealed class NPCRetaliationSystem : EntitySystem
{
    [Dependency] private readonly NpcFactionSystem _npcFaction = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly HTNSystem _htn = default!; // #Misfits Add — trigger immediate replan on aggro

    // #Misfits Change — nearby friendly NPCs within roughly their aggro band will assist when one of them is attacked.
    // This fixes the common case where only the directly-hit mob retaliates while its packmates stay idle outside passive scan range.
    private const float DefaultAssistRange = 14f;
    [Dependency] private readonly NPCSystem _npc = default!;

    /// <inheritdoc />
    public override void Initialize()
    {
        SubscribeLocalEvent<NPCRetaliationComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<HTNComponent, DamageChangedEvent>(OnHostileDamaged);
        SubscribeLocalEvent<NPCRetaliationComponent, DisarmedEvent>(OnDisarmed);
    }

    private void OnHostileDamaged(Entity<HTNComponent> ent, ref DamageChangedEvent args)
    {
        // Some humanoid enemies do not inherit the animal retaliation component.
        if (HasComp<NPCRetaliationComponent>(ent) || !args.DamageIncreased ||
            args.Origin is not { } attacker || !_npcFaction.IsEntityHostile(ent.Owner, attacker))
            return;

        var retaliation = EnsureComp<NPCRetaliationComponent>(ent);
        if (TryRetaliate((ent.Owner, retaliation), attacker))
            TryProvokeNearbyFriendlies(ent, attacker);
    }

    private void OnDamageChanged(Entity<NPCRetaliationComponent> ent, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased)
            return;

        if (args.Origin is not {} origin)
            return;

        if (!TryRetaliate(ent, origin))
            return;

        TryProvokeNearbyFriendlies(ent.Owner, origin);
    }

    private void OnDisarmed(Entity<NPCRetaliationComponent> ent, ref DisarmedEvent args)
    {
        if (!TryRetaliate(ent, args.Source))
            return;

        TryProvokeNearbyFriendlies(ent.Owner, args.Source);
    }

    private void TryProvokeNearbyFriendlies(EntityUid victim, EntityUid attacker)
    {
        if (!TryComp<NpcFactionMemberComponent>(victim, out var victimFaction))
            return;

        var assistRange = GetAssistRange(victim);
        foreach (var friendly in _npcFaction.GetNearbyFriendlies((victim, victimFaction), assistRange))
        {
            if (!TryComp<NPCRetaliationComponent>(friendly, out var retaliation))
                continue;

            TryRetaliate((friendly, retaliation), attacker);
        }
    }

    private float GetAssistRange(EntityUid victim)
    {
        if (!TryComp<HTNComponent>(victim, out var htn))
            return DefaultAssistRange;

        // #Misfits Change — reuse the victim's configured aggro vision radius so assist behavior tracks per-mob tuning.
        var assistRange = htn.Blackboard.GetValueOrDefault<float>("AggroVisionRadius", EntityManager);
        return assistRange > 0f ? assistRange : DefaultAssistRange;
    }

    public bool TryRetaliate(Entity<NPCRetaliationComponent> ent, EntityUid target)
    {
        if (HasComp<ActorComponent>(ent) || ent.Owner == target ||
            TryComp<MobStateComponent>(ent, out var state) && state.CurrentState != MobState.Alive)
            return false;

        // don't retaliate against inanimate objects.
        if (!HasComp<MobStateComponent>(target))
            return false;

        // #Misfits Change - ZAX units can clip each other during commanded combat movement.
        // Accidental friendly fire should not seed forced hostility or pack-assist aggro.
        if (HasComp<ZaxUnitComponent>(ent.Owner) && HasComp<ZaxUnitComponent>(target))
            return false;

        // #Misfits Change - Hold order is absolute for ZAX NPCs.
        if (HasComp<ZaxUnitComponent>(ent.Owner) &&
            TryComp<StationAiCommandedNpcComponent>(ent.Owner, out var commanded) &&
            commanded.HoldingCommand)
        {
            return false;
        }

        // #Misfits Change - ZAX NPCs only retaliate against genuinely hostile NPC factions.
        // Player-controlled attackers are valid only when they directly damage the ZAX unit.
        if (HasComp<ZaxUnitComponent>(ent.Owner) && !CanZaxRetaliateAgainst(ent.Owner, target))
            return false;

        if (!ent.Comp.RetaliateFriendlies
            && _npcFaction.IsEntityFriendly(ent.Owner, target))
            return false;

        if (TryComp<RecruitedFollowerComponent>(ent.Owner, out var selfRecruited)
            && TryComp<RecruitedFollowerComponent>(target, out var targetRecruited)
            && selfRecruited.Commander == targetRecruited.Commander)
            return false;

        _npcFaction.AggroEntity(ent.Owner, target);
        if (ent.Comp.AttackMemoryLength is {} memoryLength)
            ent.Comp.AttackMemories[target] = _timing.CurTime + memoryLength;

        // #Misfits Add — Force immediate HTN replan so the NPC responds to aggro without waiting for the next replan window.
        // This cuts perceived combat response delay from 250ms → ~1-2ms (next frame).
        if (TryComp<HTNComponent>(ent.Owner, out var htn))
        {
            ent.Comp.PursuitTarget = target;
            ent.Comp.PursuitUntil = _timing.CurTime + ent.Comp.PursuitDuration;
            htn.Blackboard.SetValue("Target", target);
            _npc.WakeNPC(ent, htn);
            _htn.Replan(htn);
        }

        return true;
    }

    public bool TryGetPursuitTarget(EntityUid npc, out EntityUid target)
    {
        target = default;
        if (!TryComp<NPCRetaliationComponent>(npc, out var retaliation) ||
            retaliation.PursuitTarget is not { } attacker ||
            _timing.CurTime >= retaliation.PursuitUntil || HasComp<ActorComponent>(npc) ||
            !TryComp<MobStateComponent>(npc, out var npcState) || npcState.CurrentState != MobState.Alive ||
            !TryComp<MobStateComponent>(attacker, out var state) || state.CurrentState != MobState.Alive ||
            !TryComp<TransformComponent>(npc, out var transform) ||
            !TryComp<TransformComponent>(attacker, out var targetTransform) ||
            !transform.Coordinates.TryDistance(EntityManager, targetTransform.Coordinates, out var distance) ||
            distance > retaliation.PursuitRange)
            return false;

        target = attacker;
        return true;
    }

    public float GetTargetRange(EntityUid npc, EntityUid target, float ordinaryRange)
    {
        return TryGetPursuitTarget(npc, out var attacker) && attacker == target
            ? Math.Max(ordinaryRange, Comp<NPCRetaliationComponent>(npc).PursuitRange)
            : ordinaryRange;
    }

    private bool CanZaxRetaliateAgainst(EntityUid uid, EntityUid target)
    {
        if (HasComp<ActorComponent>(target))
            return true;

        if (HasComp<MisfitsC27Component>(target) ||
            !HasComp<NpcFactionMemberComponent>(target))
        {
            return false;
        }

        return _npcFaction.IsEntityHostile(uid, target);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<NPCRetaliationComponent, FactionExceptionComponent>();
        while (query.MoveNext(out var uid, out var retaliationComponent, out var factionException))
        {
            // TODO: can probably reuse this allocation and clear it
            foreach (var entity in new ValueList<EntityUid>(retaliationComponent.AttackMemories.Keys))
            {
                if (!TerminatingOrDeleted(entity) && _timing.CurTime < retaliationComponent.AttackMemories[entity])
                    continue;

                _npcFaction.DeAggroEntity((uid, factionException), entity);
                // TODO: should probably remove the AttackMemory, thats the whole point of the ValueList right??
            }
        }
    }
}
