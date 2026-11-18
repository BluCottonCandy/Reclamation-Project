using Robust.Shared.GameStates;

namespace Content.Shared._ReclamationProject.Pack;

/// <summary>Enables the bow sling and quiver loop while equipped in outer clothing.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class PackArmorHarnessComponent : Component
{
    public const string BowSlot = "pack_bow";
    public const string QuiverSlot = "pack_quiver";
}
