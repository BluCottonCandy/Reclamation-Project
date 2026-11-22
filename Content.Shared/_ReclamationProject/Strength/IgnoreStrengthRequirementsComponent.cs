using Robust.Shared.GameStates;

namespace Content.Shared._ReclamationProject.Strength;

/// <summary>
/// #Reclamation Project - passes every Strength requirement on weapons and gear (one-handing heavy weapons, dual
/// wielding, heavy guns' recoil, melee Strength minimums, heavy-carry slowdown) without changing their Strength
/// itself. The Augmented Human has it.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class IgnoreStrengthRequirementsComponent : Component;
