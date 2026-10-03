using Content.Shared.Radio;

namespace Content.Shared.Radio.Components;

/// <summary>
/// Removes selected channels from an encryption key or key holder without deleting the item.
/// </summary>
[RegisterComponent]
public sealed partial class DisabledEncryptionChannelsComponent : Component
{
    [DataField("channels")]
    public HashSet<string> Channels = new();
}
