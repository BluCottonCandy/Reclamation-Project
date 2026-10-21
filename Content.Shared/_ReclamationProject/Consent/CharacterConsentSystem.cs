using Content.Shared.Examine;
using Content.Shared.Preferences;
using Content.Shared.Verbs;
using Robust.Shared.Network;
using Robust.Shared.Utility;

namespace Content.Shared._ReclamationProject.Consent;

public sealed class CharacterConsentSystem : EntitySystem
{
    [Dependency] private readonly ExamineSystemShared _examine = default!;
    [Dependency] private readonly INetManager _net = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<CharacterConsentComponent, GetVerbsEvent<ExamineVerb>>(OnExamine);
    }

    public void ApplyPreferences(EntityUid uid, HumanoidCharacterProfile profile)
    {
        var consent = EnsureComp<CharacterConsentComponent>(uid);
        consent.Torture = HumanoidCharacterProfile.ValidateConsent(profile.TortureConsent);
        consent.Slavery = HumanoidCharacterProfile.ValidateConsent(profile.SlaveryConsent);
        Dirty(uid, consent);
    }

    public static FormattedMessage Describe(CharacterConsentComponent consent)
    {
        var message = new FormattedMessage();
        Append(consent.Torture, "consent-torture-yes", "consent-torture-no");
        Append(consent.Slavery, "consent-slavery-yes", "consent-slavery-no");
        return message;

        void Append(ConsentPreference value, string yes, string no)
        {
            value = HumanoidCharacterProfile.ValidateConsent(value);
            if (value == ConsentPreference.Unspecified) return;
            if (!message.IsEmpty) message.PushNewline();
            message.PushColor(value == ConsentPreference.Consents ? Color.Yellow : Color.Red);
            message.AddText("[x] " + Robust.Shared.Localization.Loc.GetString(value == ConsentPreference.Consents ? yes : no));
            message.Pop();
        }
    }

    private void OnExamine(EntityUid uid, CharacterConsentComponent consent, GetVerbsEvent<ExamineVerb> args)
    {
        if (!_net.IsServer || !args.CanAccess) return;
        var message = Describe(consent);
        if (message.IsEmpty) return;
        _examine.AddDetailedExamineVerb(args, consent, message, Loc.GetString("consent-tab-title"));
    }
}
