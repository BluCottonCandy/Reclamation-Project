// #Misfits Add - Client-side EntitySystem for LoreMaster admin tab network events.
// Subscribes to network messages at init (safe, not locked) and exposes C# events
// so the UI control never touches the entity event bus directly.
using Content.Shared._Misfits.Administration;

namespace Content.Client._Misfits.Administration;

public sealed class LoreMasterSystem : EntitySystem
{
    public event Action<LoreMasterFactionInfoEvent>? OnFactionInfo;
    public event Action<LoreMasterObjectiveResultEvent>? OnObjectiveResult;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<LoreMasterFactionInfoEvent>(OnFactionInfoReceived);
        SubscribeNetworkEvent<LoreMasterObjectiveResultEvent>(OnObjectiveResultReceived);
    }

    private void OnFactionInfoReceived(LoreMasterFactionInfoEvent msg)
        => OnFactionInfo?.Invoke(msg);

    private void OnObjectiveResultReceived(LoreMasterObjectiveResultEvent msg)
        => OnObjectiveResult?.Invoke(msg);
}
