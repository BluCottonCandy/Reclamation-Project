using Robust.Shared.GameStates;

namespace Content.Shared._ReclamationProject.Hallucinations;

/// <summary>Present while a stabilizing medicine suppresses hallucinations.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class HallucinationSuppressionComponent : Component;
