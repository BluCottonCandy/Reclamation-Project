namespace Content.Server._ReclamationProject.Symptoms;

/// <summary>Per-character limits, shared by all automatic symptom speech sources.</summary>
[RegisterComponent]
public sealed partial class SymptomSpeechComponent : Component
{
    public TimeSpan NextSpeechAt;
    public readonly Dictionary<string, TimeSpan> NextBySymptom = new();
    public readonly Dictionary<string, string> LastLineBySymptom = new();
}
