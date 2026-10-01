// #Misfits Add - Client-side hallucination handler.
//   * SpawnPhantomHallucinationEvent: spawns a short-lived client-only entity
//     at the supplied coordinates, visible to this client only.
// Fake local-chat lines are pushed by the server through IChatManager.ChatMessageToOne
// and rendered by the standard chat pipeline; no client handler needed for them.
using Content.Shared._Misfits.Hallucinations;
using Robust.Shared.Timing;

namespace Content.Client._Misfits.Hallucinations;

public sealed class HallucinationsClientSystem : EntitySystem
{
    [Dependency] private readonly IEntityManager _entMan = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    /// <summary>Tracks client-spawned phantoms so we can despawn them after their lifetime.</summary>
    private readonly List<(EntityUid Uid, TimeSpan Expires)> _phantoms = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<SpawnPhantomHallucinationEvent>(OnPhantomSpawn);
    }

    private void OnPhantomSpawn(SpawnPhantomHallucinationEvent ev)
    {
        var coords = _entMan.GetCoordinates(ev.Coordinates);
        if (!coords.IsValid(_entMan))
            return;

        var phantom = _entMan.SpawnAttachedTo(ev.PrototypeId, coords);
        _phantoms.Add((phantom, _timing.CurTime + TimeSpan.FromSeconds(ev.Lifetime)));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_phantoms.Count == 0)
            return;

        var now = _timing.CurTime;
        for (var i = _phantoms.Count - 1; i >= 0; i--)
        {
            if (now < _phantoms[i].Expires)
                continue;
            var uid = _phantoms[i].Uid;
            _phantoms.RemoveAt(i);
            if (_entMan.EntityExists(uid))
                _entMan.DeleteEntity(uid);
        }
    }
}
