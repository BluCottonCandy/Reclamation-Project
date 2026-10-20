using Robust.Shared.GameStates;
using Robust.Shared.Utility;

namespace Content.Shared._ReclamationProject.Cookbook;

/// <summary>A standalone recipe collection; these pages are not guide-entry prototypes.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CookbookComponent : Component
{
    [DataField(required: true)] public Dictionary<string, CookbookChapter> Chapters = new();
}

[DataDefinition]
public sealed partial class CookbookChapter
{
    [DataField(required: true)] public LocId Name;
    [DataField(required: true)] public ResPath Text;
    [DataField] public int Order;
}
