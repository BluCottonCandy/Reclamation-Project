// #Misfits Removed - The faction database is now resolved per-viewer via ID access tags.
// Every terminal exposes the DATABASE tab automatically through HolotapeDataComponent.
// This component is preserved (commented) per the no-delete policy in case we ever
// reintroduce a per-terminal pin (e.g. a terminal that locks to one faction's DB
// regardless of who reads it).

/*
using Robust.Shared.GameObjects;

namespace Content.Shared._Misfits.Holotape;

[RegisterComponent]
public sealed partial class TerminalDatabaseComponent : Component
{
    [DataField("databaseId")]
    public string DatabaseId { get; set; } = string.Empty;
}
*/
