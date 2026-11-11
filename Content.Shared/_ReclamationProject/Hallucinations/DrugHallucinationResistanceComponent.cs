using Robust.Shared.GameStates;

namespace Content.Shared._ReclamationProject.Hallucinations;

/// <summary>Prevents hallucinations and acquired instability caused by drugs.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class DrugHallucinationResistanceComponent : Component { }
