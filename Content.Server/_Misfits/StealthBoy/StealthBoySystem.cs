// #Misfits Add - Server-side Stealth Boy psychological side effects.
// Hooks the activation event to dose the addiction system, surfaces tier
// transitions to the player, and asks the shared HallucinationsSystem to
// (re)compute the user's effective hallucination intensity.
//
// Performance:
//  * No per-tick query lives here \u2014 hallucinations are owned by HallucinationsSystem,
//    which only iterates entities with HallucinationsComponent. Untouched players cost zero.
//  * Tier transitions are the only events we care about; SharedStealthBoySystem
//    fires OnTierChanged exactly when the cached tier flips.
using Content.Shared._Misfits.Addictions;
using Content.Shared._Misfits.Hallucinations;
using Content.Shared._Misfits.StealthBoy;
using Content.Shared.Popups;
using Content.Server._Misfits.Hallucinations;

namespace Content.Server._Misfits.StealthBoy;

public sealed class StealthBoySystem : SharedStealthBoySystem
{
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedAddictionSystem _addiction = default!;
    [Dependency] private readonly HallucinationsSystem _hallucinations = default!;

    /// <summary>How long an addiction "dose" lasts (seconds) before withdrawal can clear.</summary>
    private const float AddictionDuration = 600f;

    /// <summary>Display name surfaced through the addiction system.</summary>
    private const string StealthBoyDrugName = "Stealth Boy";

    /// <summary>
    /// Server hook: dose the user with the addiction system so withdrawal can hit
    /// after enough activations, and re-evaluate hallucination intensity.
    /// </summary>
    protected override void OnStealthBoyActivated(EntityUid user, StealthBoyExposureComponent exposure)
    {
        // Threshold 3 = withdrawal jitters start after the third activation.
        _addiction.TryApplyAddiction(user, AddictionDuration, drugName: StealthBoyDrugName, addictionThreshold: 3);
        _hallucinations.RefreshIntensity(user);

        // Loud onset popup if the user just tipped into a Paracusia breakdown so
        // they understand why their screen is suddenly screaming at them.
        if (HasComp<Content.Shared._Misfits.Hallucinations.MisfitsParacusiaComponent>(user))
        {
            _popup.PopupEntity(
                "The hum of the Stealth Boy MELTS into the voices in your head. EVERYTHING IS WRONG.",
                user, user, PopupType.LargeCaution);
        }
    }

    /// <summary>
    /// Cloak ended — re-derive hallucination intensity so the breakdown bonus drops away.
    /// </summary>
    protected override void OnStealthBoyDeactivated(EntityUid user)
    {
        _hallucinations.RefreshIntensity(user);
    }

    /// <summary>
    /// Surface tier transitions to the user only and update the hallucination
    /// engine so it pulls the new effective intensity.
    /// </summary>
    protected override void OnTierChanged(EntityUid user, int oldTier, int newTier)
    {
        _hallucinations.RefreshIntensity(user);

        if (newTier <= oldTier)
            return;

        var msg = newTier switch
        {
            1 => "A faint static fizzles at the edge of your hearing.",
            2 => "Something feels... watched. Footsteps that aren't there.",
            3 => "Voices. Whispers. Distant gunfire that nobody else flinches at.",
            4 => "Your skull is pounding. The walls won't sit still.",
            _ => null,
        };

        if (msg != null)
            _popup.PopupEntity(msg, user, user, PopupType.MediumCaution);
    }
}
