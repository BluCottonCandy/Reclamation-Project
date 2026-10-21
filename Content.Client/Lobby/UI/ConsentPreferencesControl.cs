using System;
using Content.Client.Administration.UI.CustomControls;
using Robust.Shared.Utility;
using Content.Shared.Preferences;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.Lobby.UI;

public sealed class ConsentPreferencesControl : BoxContainer
{
    public CheckBox TortureYes { get; } = new() { Text = Loc.GetString("consent-torture-yes") };
    public CheckBox TortureNo { get; } = new() { Text = Loc.GetString("consent-torture-no") };
    public CheckBox SlaveryYes { get; } = new() { Text = Loc.GetString("consent-slavery-yes") };
    public CheckBox SlaveryNo { get; } = new() { Text = Loc.GetString("consent-slavery-no") };
    public ConsentPreference Torture { get; private set; }
    public ConsentPreference Slavery { get; private set; }
    public event Action<ConsentPreference, ConsentPreference>? PreferencesChanged;

    public ConsentPreferencesControl()
    {
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        Margin = new Thickness(12);
        SeparationOverride = 10;
        AddChild(new Label { Text = Loc.GetString("consent-tab-title"), StyleClasses = { "LabelHeading" } });
        var description = new RichTextLabel { HorizontalExpand = true };
        description.SetMessage(FormattedMessage.FromUnformatted(Loc.GetString("consent-tab-description")));
        AddChild(description);
        TortureYes.Label.FontColorOverride = Color.Yellow;
        SlaveryYes.Label.FontColorOverride = Color.Yellow;
        TortureNo.Label.FontColorOverride = Color.Red;
        SlaveryNo.Label.FontColorOverride = Color.Red;
        AddChild(TortureYes);
        AddChild(TortureNo);
        AddChild(new HSeparator());
        AddChild(SlaveryYes);
        AddChild(SlaveryNo);
        TortureYes.OnToggled += args => Select(true, ConsentPreference.Consents, args.Pressed);
        TortureNo.OnToggled += args => Select(true, ConsentPreference.DoesNotConsent, args.Pressed);
        SlaveryYes.OnToggled += args => Select(false, ConsentPreference.Consents, args.Pressed);
        SlaveryNo.OnToggled += args => Select(false, ConsentPreference.DoesNotConsent, args.Pressed);
    }

    public void SetPreferences(ConsentPreference torture, ConsentPreference slavery)
    {
        Torture = HumanoidCharacterProfile.ValidateConsent(torture);
        Slavery = HumanoidCharacterProfile.ValidateConsent(slavery);
        TortureYes.Pressed = Torture == ConsentPreference.Consents;
        TortureNo.Pressed = Torture == ConsentPreference.DoesNotConsent;
        SlaveryYes.Pressed = Slavery == ConsentPreference.Consents;
        SlaveryNo.Pressed = Slavery == ConsentPreference.DoesNotConsent;
    }

    public void Select(bool torture, ConsentPreference value, bool selected)
    {
        var current = torture ? Torture : Slavery;
        var next = selected ? value : current == value ? ConsentPreference.Unspecified : current;
        SetPreferences(torture ? next : Torture, torture ? Slavery : next);
        PreferencesChanged?.Invoke(Torture, Slavery);
    }
}
