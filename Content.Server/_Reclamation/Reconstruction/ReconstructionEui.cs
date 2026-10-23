using Content.Server.EUI;
using Content.Shared._Reclamation.Reconstruction;
using Content.Shared.Eui;

namespace Content.Server._Reclamation.Reconstruction;

/// <summary>Asks the patient for consent, then lets them pick their new look.</summary>
public sealed class ReconstructionEui : BaseEui
{
    private readonly ReconstructionPodSystem _system;
    private readonly EntityUid _pod;
    private readonly ReconstructionEuiState _state;

    /// <summary>Set once the patient submits a look or the pod closes this window itself.</summary>
    public bool Finished;

    public ReconstructionEui(ReconstructionPodSystem system, EntityUid pod, ReconstructionEuiState state)
    {
        _system = system;
        _pod = pod;
        _state = state;
    }

    public override void Opened()
    {
        base.Opened();
        StateDirty();
    }

    public override EuiStateBase GetNewState() => _state;

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);

        switch (msg)
        {
            case ReconstructionConsentMessage { Accepted: true }:
                break;
            case ReconstructionConsentMessage:
                Close();
                break;
            case ReconstructionSubmitMessage submit:
                Finished = true;
                _system.OnPatientSubmitted(_pod, submit);
                Close();
                break;
        }
    }

    public override void Closed()
    {
        base.Closed();

        if (!Finished)
            _system.OnPatientDeclined(_pod);
    }
}
