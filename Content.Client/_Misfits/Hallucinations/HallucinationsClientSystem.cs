// #Misfits Add - Client-side hallucination handler.
//   * SpawnPhantomHallucinationEvent: spawns a short-lived client-only entity
//     at the supplied coordinates, visible to this client only.
// Fake local-chat lines are pushed by the server through IChatManager.ChatMessageToOne
// and rendered by the standard chat pipeline; no client handler needed for them.
using Content.Shared._Misfits.Hallucinations;
using Content.Shared._ReclamationProject.Hallucinations;
using Robust.Client.Player;
using Robust.Shared.Timing;

namespace Content.Client._Misfits.Hallucinations;

public sealed class HallucinationsClientSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _player = default!;
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
        if (HasComp<HallucinationSuppressionComponent>(_player.LocalEntity))
            return;

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

        var suppressed = HasComp<HallucinationSuppressionComponent>(_player.LocalEntity);
        var now = _timing.CurTime;
        for (var i = _phantoms.Count - 1; i >= 0; i--)
        {
            if (!suppressed && now < _phantoms[i].Expires)
                continue;
            var uid = _phantoms[i].Uid;
            _phantoms.RemoveAt(i);
            if (_entMan.EntityExists(uid))
                _entMan.DeleteEntity(uid);
        }
    }
}
