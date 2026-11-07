using Robust.Shared.Prototypes;

namespace Content.Shared._ReclamationProject.Symptoms;

/// <summary>Localized bodily notifications and the first-person lines that replace them.</summary>
[Prototype("symptomSpeech")]
public sealed partial class SymptomSpeechPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;
    [DataField] public List<string> SourceMessages = new();
    [DataField(required: true)] public List<string> SpeechMessages = new();
    [DataField] public float Cooldown = 45f;
    [DataField] public float CooldownVariation = 15f;
}
