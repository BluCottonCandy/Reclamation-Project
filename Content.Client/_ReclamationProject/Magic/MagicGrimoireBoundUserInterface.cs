using Content.Shared._ReclamationProject.Magic;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._ReclamationProject.Magic;

/// <summary>#Reclamation Project - opens the grimoire window for its owner.</summary>
[UsedImplicitly]
public sealed class MagicGrimoireBoundUserInterface : BoundUserInterface
{
    private MagicGrimoireWindow? _window;

    public MagicGrimoireBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<MagicGrimoireWindow>();
        _window.OnWrite += (page, text) => SendMessage(new MagicWritePageMessage(page, text));
        _window.OnNote += (page, note) => SendMessage(new MagicSetNoteMessage(page, note));
        _window.OnToggleReady += page => SendMessage(new MagicToggleReadyMessage(page));
        _window.OnCast += page => SendMessage(new MagicCastPageMessage(page));
        _window.OnTearOut += page => SendMessage(new MagicTearOutMessage(page));
        _window.OnEtch += page => SendMessage(new MagicEtchPageMessage(page));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is MagicGrimoireBuiState grimoire)
            _window?.UpdateState(grimoire);
    }
}
