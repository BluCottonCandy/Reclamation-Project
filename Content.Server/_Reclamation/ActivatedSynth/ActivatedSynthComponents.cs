namespace Content.Server._Reclamation.ActivatedSynth;

[RegisterComponent]
public sealed partial class ActivatedSynthRuleComponent : Component;

/// <summary>Stored on the mind, preserving the report after death or a body change.</summary>
[RegisterComponent]
public sealed partial class SynthReportComponent : Component
{
    public string Text = string.Empty;
}

[RegisterComponent]
public sealed partial class SynthObjectiveComponent : Component
{
    [DataField(required: true)] public SynthTask Task;
    public bool Completed;
}

public enum SynthTask : byte
{
    Holodisks,
    Rifle,
    Keys,
    Railroad,
    DiamondCity,
    Mayor,
}
