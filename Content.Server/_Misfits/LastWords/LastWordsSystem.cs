// #Misfits Add - Records the last spoken words on a player's mind entity.
// Ported and adapted from Goob-Station (Content.Goobstation.Server.LastWords).
// Every message the entity says is saved; upon death the last recorded value persists.

using Content.Server.Chat.Systems;
using Content.Shared._Misfits.LastWords;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Components;

namespace Content.Server._Misfits.LastWords;

public sealed class LastWordsSystem : EntitySystem
{
    [Dependency] private readonly SharedMindSystem _mind = default!;

    public override void Initialize()
    {
        base.Initialize();

        // Track what the body/mob says and store it on the mind.
        // #Misfits Fix - ComponentStartup is exclusive (one subscriber per comp+event); attach
        // LastWordsComponent lazily on first speech instead to avoid duplicate-subscription crash.
        SubscribeLocalEvent<MobStateComponent, EntitySpokeEvent>(OnEntitySpoke);
    }

    private void OnEntitySpoke(EntityUid uid, MobStateComponent _mob, EntitySpokeEvent args)
    {
        if (!_mind.TryGetMind(uid, out var mindId, out _))
            return;

        var lastWordsComp = EnsureComp<LastWordsComponent>(mindId);
        lastWordsComp.LastWords = args.Message;
    }
}
