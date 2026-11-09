using Robust.Shared.GameStates;

namespace Content.Shared._ReclamationProject.Hallucinations;

/// <summary>Presence of a metabolized high; only the marker is networked, never the hallucinations.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class DrugHighComponent : Component
{
    public TimeSpan Expires;
}
