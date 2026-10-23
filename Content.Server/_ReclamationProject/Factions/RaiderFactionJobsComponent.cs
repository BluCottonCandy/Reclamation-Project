namespace Content.Server._ReclamationProject.Factions;

/// <summary>Default public raider slots for N14 maps; explicit map slots take precedence.</summary>
[RegisterComponent]
public sealed partial class RaiderFactionJobsComponent : Component
{
    [DataField(required: true)] public Dictionary<string, int> Jobs = new();
}
