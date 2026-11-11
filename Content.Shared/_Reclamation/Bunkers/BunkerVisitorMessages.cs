using Content.Shared.Eui;
using Robust.Shared.Serialization;

namespace Content.Shared._Reclamation.Bunkers;

[Serializable, NetSerializable]
public sealed class ListBunkerVisitorsMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed record BunkerVisitorEntry(NetEntity Map, string Owner, bool Available);

[Serializable, NetSerializable]
public sealed class BunkerVisitorListMessage(BunkerVisitorEntry[] entries) : BoundUserInterfaceMessage
{
    public BunkerVisitorEntry[] Entries { get; } = entries;
}

[Serializable, NetSerializable]
public sealed class RequestBunkerVisitMessage(NetEntity map) : BoundUserInterfaceMessage
{
    public NetEntity Map { get; } = map;
}

[Serializable, NetSerializable]
public sealed class BunkerVisitPromptState(string requester) : EuiStateBase
{
    public string Requester { get; } = requester;
}

[Serializable, NetSerializable]
public sealed class BunkerVisitAnswerMessage(bool approved) : EuiMessageBase
{
    public bool Approved { get; } = approved;
}
