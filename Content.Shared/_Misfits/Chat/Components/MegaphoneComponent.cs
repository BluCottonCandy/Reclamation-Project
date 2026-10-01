using Content.Shared.Speech;
using Robust.Shared.Prototypes;

namespace Content.Shared._Misfits.Chat.Components;

/// <summary>
/// Marks an item as a megaphone source and defines the speech verb to use while wielded.
/// Wielding the megaphone overwrites the holder's SpeechComponent.SpeechVerb so that
/// GetSpeechVerb() — which drives font size in WrapMessage — picks up the megaphone style.
/// </summary>
[RegisterComponent]
public sealed partial class MegaphoneComponent : Component
{
    /// <summary>
    /// The speech verb override applied to the holder while the item is wielded.
    /// </summary>
    [DataField]
    public ProtoId<SpeechVerbPrototype> SpeechVerbOverride = "MisfitsMegaphone";

    /// <summary>
    /// The holder's original speech verb, stored when the megaphone is wielded so it can
    /// be restored when the item is unwielded or leaves the hand.
    /// </summary>
    [DataField]
    public ProtoId<SpeechVerbPrototype>? PreviousSpeechVerb;
}
