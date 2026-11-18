// #Reclamation Project - medieval weapons: marks a character who took the Blacksmith trait.
using Robust.Shared.GameStates;

namespace Content.Shared._ReclamationProject.Medieval;

/// <summary>
/// Given by the Blacksmith trait. Lets the character forge the recipes a
/// <see cref="BlacksmithLatheComponent"/> adds to a workbench (medieval weapons and armour).
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class BlacksmithComponent : Component;
