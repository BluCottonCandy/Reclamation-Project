using Content.Shared._ReclamationProject.Lockpick;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._ReclamationProject.Lockpick;

/// <summary>#Reclamation Project - opens the lockpicking window.</summary>
[UsedImplicitly]
public sealed class LockpickBoundUserInterface : BoundUserInterface
{
    private LockpickWindow? _window;

    public LockpickBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<LockpickWindow>();
        _window.Canvas.OnRelease += (pin, lift, tension) =>
            SendMessage(new LockpickReleaseMessage { Pin = pin, Lift = lift, Tension = tension });
        _window.Canvas.OnSnap += () => SendMessage(new LockpickSnapMessage());
        _window.Canvas.OnSlip += () => SendMessage(new LockpickSlipMessage());
        _window.OnReset += () =>
        {
            _window.Canvas.DropAllLocal();
            SendMessage(new LockpickResetMessage());
        };
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is LockpickBuiState lockState)
            _window?.UpdateState(lockState);
    }
}
