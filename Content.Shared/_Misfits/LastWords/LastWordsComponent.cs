// #Misfits Add - Tracks the last spoken words of a player before death.
// Ported and adapted from Goob-Station (Content.Goobstation.Common.LastWords).
// Component lives on the mind entity, not the body.

namespace Content.Shared._Misfits.LastWords;

/// <summary>
/// Stores the last words spoken by the mind's owning entity.
/// Automatically added to every mind entity when it starts up.
/// </summary>
[RegisterComponent]
public sealed partial class LastWordsComponent : Component
{
    [DataField]
    public string? LastWords;
}
