using Robust.Shared.Serialization;

namespace Content.Shared.Preferences;

[Serializable, NetSerializable]
public enum ConsentPreference
{
    Unspecified,
    Consents,
    DoesNotConsent,
}

public sealed partial class HumanoidCharacterProfile
{
    [DataField]
    public ConsentPreference TortureConsent { get; set; }

    [DataField]
    public ConsentPreference SlaveryConsent { get; set; }

    public static ConsentPreference ValidateConsent(ConsentPreference value) =>
        Enum.IsDefined(value) ? value : ConsentPreference.Unspecified;

    public HumanoidCharacterProfile WithConsent(ConsentPreference torture, ConsentPreference slavery) =>
        new(this)
        {
            TortureConsent = ValidateConsent(torture),
            SlaveryConsent = ValidateConsent(slavery),
        };
}
