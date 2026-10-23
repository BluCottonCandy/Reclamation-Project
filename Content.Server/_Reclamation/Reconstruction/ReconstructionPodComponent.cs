using Content.Shared._Reclamation.Reconstruction;
using Content.Shared.DoAfter;
using Robust.Shared.Audio;

namespace Content.Server._Reclamation.Reconstruction;

/// <summary>
/// A pod that changes the appearance of whoever is inside. Someone outside has to start it,
/// and the patient has to agree and pick their new look.
/// </summary>
[RegisterComponent, Access(typeof(ReconstructionPodSystem), typeof(ReconstructionEui))]
public sealed partial class ReconstructionPodComponent : Component
{
    [DataField]
    public string ContainerId = "reconstruction-body";

    /// <summary>How long the operator has to keep the pod running.</summary>
    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(30);

    [DataField]
    public SoundSpecifier? FinishSound = new SoundPathSpecifier("/Audio/Machines/microwave_done_beep.ogg");

    [ViewVariables]
    public EntityUid? Operator;

    [ViewVariables]
    public EntityUid? Patient;

    [ViewVariables]
    public ReconstructionEui? Eui;

    [ViewVariables]
    public DoAfterId? DoAfter;

    /// <summary>The validated new look, applied when the operator's do-after finishes.</summary>
    [ViewVariables]
    public ReconstructionSubmitMessage? Pending;
}
