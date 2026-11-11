// #Reclamation Project - Pip-Boy bio-monitor client
using Content.Shared._ReclamationProject.Medical.BioMonitor;

namespace Content.Client._ReclamationProject.Medical.BioMonitor;

/// <summary>
/// Opens the bio-monitor window when the server sends a state, and tells the server when it closes.
/// </summary>
public sealed class BioMonitorUiSystem : EntitySystem
{
    private BioMonitorWindow? _window;
    private NetEntity _target;
    private bool _closingFromServer;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<BioMonitorStateMessage>(OnState);
        SubscribeNetworkEvent<BioMonitorCloseMessage>(OnServerClose);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _window?.Close();
        _window = null;
    }

    private void OnState(BioMonitorStateMessage msg)
    {
        // Switching between the Pip-Boy and Institute look needs a fresh window.
        if (_window != null && _window.Institute != msg.InstituteStyle)
            OnServerClose(new BioMonitorCloseMessage());

        if (_window == null)
        {
            _window = new BioMonitorWindow(msg.InstituteStyle);
            _window.OnClose += OnWindowClosed;
            _window.OnRemove += item => RaiseNetworkEvent(new BioMonitorRemoveRequest { Target = _target, Item = item });
            _window.OpenCentered();
        }

        _target = msg.Target;
        _window.UpdateState(msg);
    }

    private void OnServerClose(BioMonitorCloseMessage msg)
    {
        if (_window == null)
            return;

        _closingFromServer = true;
        _window.Close();
        _closingFromServer = false;
    }

    private void OnWindowClosed()
    {
        _window = null;
        if (!_closingFromServer)
            RaiseNetworkEvent(new BioMonitorCloseMessage());
    }
}
