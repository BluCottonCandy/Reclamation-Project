using Content.Shared.Eui;
using Robust.Shared.Serialization;

namespace Content.Shared._ReclamationProject.Bunkers;

[Serializable, NetSerializable]
public sealed record AdminBunkerEntry(string Key, string CharacterName, string Ckey, bool Loaded);

[Serializable, NetSerializable]
public sealed class AdminBunkerEuiState(List<AdminBunkerEntry> entries, string status, bool busy) : EuiStateBase
{
    public List<AdminBunkerEntry> Entries = entries;
    public string Status = status;
    public bool Busy = busy;
}

[Serializable, NetSerializable]
public sealed class AdminBunkerRefreshMessage : EuiMessageBase;
[Serializable, NetSerializable]
public sealed class AdminBunkerVisitMessage(string key) : EuiMessageBase
{
    public string Key = key;
}
[Serializable, NetSerializable]
public sealed class AdminBunkerReturnMessage : EuiMessageBase;
