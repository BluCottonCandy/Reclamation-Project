// #Misfits Add - Networked event sent server -> single client to spawn a short-lived
// phantom entity at coordinates only that player sees. Used by HallucinationsSystem
// for intensity 3+ visual hallucinations. Fake local-chat lines are NOT a network
// event — the server pushes them through the existing IChatManager.ChatMessageToOne path.
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._Misfits.Hallucinations;

[Serializable, NetSerializable]
public sealed class SpawnPhantomHallucinationEvent : EntityEventArgs
{
    public string PrototypeId { get; }
    public NetCoordinates Coordinates { get; }
    public float Lifetime { get; }

    public SpawnPhantomHallucinationEvent(string prototypeId, NetCoordinates coordinates, float lifetime)
    {
        PrototypeId = prototypeId;
        Coordinates = coordinates;
        Lifetime = lifetime;
    }
}
