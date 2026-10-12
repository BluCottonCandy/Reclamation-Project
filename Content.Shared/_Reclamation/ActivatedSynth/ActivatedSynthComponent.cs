using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Reclamation.ActivatedSynth;

/// <summary>Kept on the owner's private mind so activation is not revealed to nearby clients.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ActivatedSynthComponent : Component;

[Serializable, NetSerializable]
public sealed class RequestSynthReportEvent : EntityEventArgs;

[Serializable, NetSerializable]
public sealed class SynthActivatedEvent : EntityEventArgs;

[Serializable, NetSerializable]
public sealed class SynthReportStateEvent : EntityEventArgs
{
    public string Text = string.Empty;
    public Dictionary<string, bool> RoleplayObjectives = new();
    public bool CanEdit;
}

[Serializable, NetSerializable]
public sealed class SubmitSynthReportEvent : EntityEventArgs
{
    public string Text = string.Empty;
    public HashSet<string> CompletedObjectives = new();
}
