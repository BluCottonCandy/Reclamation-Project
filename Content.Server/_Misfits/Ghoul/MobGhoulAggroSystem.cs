// #Misfits Change: Keep feral ghoul mobs neutral to player ghouls until a player ghoul attacks.
using Content.Server.GameTicking;
using Content.Shared.CombatMode;
using Content.Shared.Damage;
using Content.Shared.Humanoid;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Robust.Shared.Collections;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Misfits.Ghoul;

public sealed class MobGhoulAggroSystem : EntitySystem
{
    [Dependency] private readonly NpcFactionSystem _npcFaction = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    // #Misfits Fix: Doubled from 5 s — O(mobs × players) sync; at 70 players 10 s is still imperceptible.
    private static readonly TimeSpan NeutralSyncInterval = TimeSpan.FromSeconds(10);
    private TimeSpan _nextNeutralSync;

    public override void Initialize()
    {
        base.Initialize();

        // #Misfits Fix: System defunct — O(mobs × players) sync was a notable spike at 70 players.
        // Feral ghouls will now aggro player ghouls normally (no special neutral treatment).
        // Re-enable by un-commenting the subscriptions here and the Update body below.
        // SubscribeLocalEvent<MobGhoulAggroComponent, ComponentStartup>(OnMobGhoulStartup);
        // SubscribeLocalEvent<MobGhoulAggroComponent, DamageChangedEvent>(OnMobGhoulDamaged);
        // SubscribeLocalEvent<MobGhoulAggroComponent, DisarmedEvent>(OnMobGhoulDisarmed);
        // SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
    }

    public override void Update(float frameTime)
    {
        // #Misfits Fix: Defunct — see Initialize comment above.
        // base.Update(frameTime);
        // if (_timing.CurTime < _nextNeutralSync)
        //     return;
        // _nextNeutralSync = _timing.CurTime + NeutralSyncInterval;
        // SyncNeutralPlayerGhouls();
    }

    private void OnMobGhoulStartup(Entity<MobGhoulAggroComponent> ent, ref ComponentStartup args)
    {
        SyncNeutralPlayerGhouls(ent);
    }

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        if (!IsPlayerGhoul(args.Mob))
            return;

        SyncNeutralPlayerGhoul(args.Mob);
    }

    private void OnMobGhoulDamaged(Entity<MobGhoulAggroComponent> ent, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased || args.Origin is not { } attacker)
            return;

        if (!IsPlayerGhoul(attacker))
            return;

        ProvokeAllMobGhouls(attacker);
    }

    private void OnMobGhoulDisarmed(Entity<MobGhoulAggroComponent> ent, ref DisarmedEvent args)
    {
        if (!IsPlayerGhoul(args.Source))
            return;

        ProvokeAllMobGhouls(args.Source);
    }

    private void SyncNeutralPlayerGhouls(Entity<MobGhoulAggroComponent> ent)
    {
        EnsureComp<FactionExceptionComponent>(ent);
        SyncNeutralPlayerGhouls();
    }

    private void SyncNeutralPlayerGhouls()
    {
        var playerGhouls = new ValueList<EntityUid>();
        var playerQuery = EntityQueryEnumerator<ActorComponent, HumanoidAppearanceComponent>();
        while (playerQuery.MoveNext(out var playerUid, out _, out var humanoid))
        {
            if (IsGhoulSpecies(humanoid))
                playerGhouls.Add(playerUid);
        }

        // Misfits Fix: hoist snapshot buffer outside the NPC loop so we only allocate once
        // instead of once-per-NPC — eliminates inner-loop heap allocations at scale.
        var ignoredBuffer = new ValueList<EntityUid>();

        var ghoulQuery = EntityQueryEnumerator<MobGhoulAggroComponent, FactionExceptionComponent>();
        while (ghoulQuery.MoveNext(out var ghoulUid, out var aggro, out var exception))
        {
            foreach (var playerGhoul in playerGhouls)
            {
                if (!aggro.ProvokedPlayerGhouls.Contains(playerGhoul))
                    _npcFaction.IgnoreEntity((ghoulUid, exception), playerGhoul);
            }

            // Snapshot Ignored into our reused buffer so iteration is safe while UnignoreEntity mutates the set.
            ignoredBuffer.Clear();
            foreach (var ignored in exception.Ignored)
                ignoredBuffer.Add(ignored);

            foreach (var ignored in ignoredBuffer)
            {
                if (aggro.ProvokedPlayerGhouls.Contains(ignored))
                    continue;

                if (!HasComp<ActorComponent>(ignored))
                    continue;

                if (TryComp<HumanoidAppearanceComponent>(ignored, out var ignoredHumanoid) && IsGhoulSpecies(ignoredHumanoid))
                    continue;

                _npcFaction.UnignoreEntity((ghoulUid, exception), ignored);
            }
        }
    }

    private void SyncNeutralPlayerGhoul(EntityUid playerGhoul)
    {
        var ghoulQuery = EntityQueryEnumerator<MobGhoulAggroComponent, FactionExceptionComponent>();
        while (ghoulQuery.MoveNext(out var ghoulUid, out var aggro, out var exception))
        {
            if (aggro.ProvokedPlayerGhouls.Contains(playerGhoul))
                continue;

            _npcFaction.IgnoreEntity((ghoulUid, exception), playerGhoul);
        }
    }

    private void ProvokeAllMobGhouls(EntityUid attacker)
    {
        var ghoulQuery = EntityQueryEnumerator<MobGhoulAggroComponent, FactionExceptionComponent>();
        while (ghoulQuery.MoveNext(out var ghoulUid, out var aggro, out var exception))
        {
            aggro.ProvokedPlayerGhouls.Add(attacker);
            _npcFaction.UnignoreEntity((ghoulUid, exception), attacker);
            _npcFaction.AggroEntity((ghoulUid, exception), attacker);
        }
    }

    private bool IsPlayerGhoul(EntityUid uid)
    {
        return HasComp<ActorComponent>(uid)
            && TryComp<HumanoidAppearanceComponent>(uid, out var humanoid)
            && IsGhoulSpecies(humanoid);
    }

    private static bool IsGhoulSpecies(HumanoidAppearanceComponent humanoid)
    {
        return humanoid.Species == "Ghoul" || humanoid.Species == "GhoulGlowing";
    }
}
