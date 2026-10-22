using Content.Client.Eui;
using Content.Shared._Reclamation.Reconstruction;
using Content.Shared.Eui;
using JetBrains.Annotations;
using Robust.Client.Graphics;

namespace Content.Client._Reclamation.Reconstruction;

[UsedImplicitly]
public sealed class ReconstructionEui : BaseEui
{
    private readonly ReconstructionWindow _window;

    /// <summary>True once the server already knows the outcome, so closing the window sends nothing.</summary>
    private bool _answered;

    public ReconstructionEui()
    {
        _window = new ReconstructionWindow();

        _window.OnConsent += accepted =>
        {
            SendMessage(new ReconstructionConsentMessage(accepted));
            if (accepted)
                return;

            _answered = true;
            _window.Close();
        };

        _window.OnSubmit += submit =>
        {
            _answered = true;
            SendMessage(submit);
            _window.Close();
        };

        _window.OnClose += () =>
        {
            if (_answered)
                return;

            _answered = true;
            SendMessage(new ReconstructionConsentMessage(false));
        };
    }

    public override void Opened()
    {
        IoCManager.Resolve<IClyde>().RequestWindowAttention();
        _window.OpenCentered();
    }

    public override void HandleState(EuiStateBase state)
    {
        if (state is ReconstructionEuiState reconstruction)
            _window.SetState(reconstruction);
    }

    public override void Closed()
    {
        _answered = true;
        _window.Close();
    }
}
