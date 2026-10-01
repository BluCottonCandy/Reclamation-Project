using Content.Server.SuperMutant.Systems;

namespace Content.Server.SuperMutant.Components;

/// <summary>
/// Applied to an injectable solution that permanently transforms a character into a Super Mutant.
/// When injected, this will transform the target into the Super Mutant species in-game
/// and update their database profile so the change persists across rounds.
/// </summary>
[RegisterComponent, Access(typeof(SuperMutantTransformationSystem))]
public sealed partial class SuperMutantTransformationComponent : Component
{
    /// <summary>
    /// Whether to update the character's database profile to be a Super Mutant permanently.
    /// If false, this is only an in-game transformation that won't persist.
    /// </summary>
    [DataField]
    public bool UpdateDatabaseProfile = true;

    /// <summary>
    /// The target species ID to transform into.
    /// </summary>
    [DataField]
    public string TargetSpecies = "SuperMutant";

    /// <summary>
    /// Whether to update the job to SuperMutant job when transforming.
    /// </summary>
    [DataField]
    public bool UpdateJob = true;

    /// <summary>
    /// The job to assign if UpdateJob is true.
    /// </summary>
    [DataField]
    public string TargetJob = "SuperMutant";

    /// <summary>
    /// Popup message shown to the injected entity.
    /// </summary>
    [DataField]
    public string TransformationMessage = "supermutant-transform-self";

    /// <summary>
    /// Popup message shown to others nearby.
    /// </summary>
    [DataField]
    public string TransformationOthersMessage = "supermutant-transform-others";
}
