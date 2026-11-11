using Content.Server.EUI;
using Content.Shared._Reclamation.Bunkers;
using Content.Shared.Eui;

namespace Content.Server._Reclamation.Bunkers;

public sealed class BunkerVisitPromptEui(string requester, Action<bool> answer) : BaseEui
{
    private bool _approved;

    public override void Opened() => StateDirty();
    public override EuiStateBase GetNewState() => new BunkerVisitPromptState(requester);

    public override void HandleMessage(EuiMessageBase msg)
    {
        if (msg is BunkerVisitAnswerMessage choice)
        {
            _approved = choice.Approved;
            Close();
            return;
        }

        base.HandleMessage(msg);
    }

    public override void Closed() => answer(_approved);
}
