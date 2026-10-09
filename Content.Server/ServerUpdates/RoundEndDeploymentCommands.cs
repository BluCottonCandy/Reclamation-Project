using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server.ServerUpdates;

[AdminCommand(AdminFlags.Server)]
public sealed class QueueRoundEndDeploymentCommand : IConsoleCommand
{
    [Dependency] private readonly ServerUpdateManager _updates = default!;
    public string Command => "reclamation_update_queue";
    public string Description => "Queue or renew an OXY deployment without ending the current round.";
    public string Help => "reclamation_update_queue <32-character hexadecimal ticket>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError(Help);
            return;
        }
        try
        {
            _updates.QueueDeployment(args[0]);
            shell.WriteLine("Round-end deployment request acknowledged.");
        }
        catch (Exception)
        {
            shell.WriteError("Deployment request rejected. Check its ticket, existing requests, and data directory access.");
        }
    }
}

[AdminCommand(AdminFlags.Server)]
public sealed class CancelRoundEndDeploymentCommand : IConsoleCommand
{
    [Dependency] private readonly ServerUpdateManager _updates = default!;
    public string Command => "reclamation_update_cancel";
    public string Description => "Cancel the matching queued OXY deployment.";
    public string Help => "reclamation_update_cancel <ticket>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError(Help);
            return;
        }
        _updates.CancelDeployment(args[0]);
        shell.WriteLine("Matching deployment request cancelled, if present.");
    }
}
