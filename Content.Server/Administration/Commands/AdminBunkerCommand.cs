using Content.Server.EUI;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server.Administration.Commands;

[AdminCommand(AdminFlags.Admin)]
public sealed class AdminBunkerCommand : IConsoleCommand
{
    public string Command => "adminbunkers";
    public string Description => "Browse and inspect saved personal bunkers.";
    public string Help => "Usage: adminbunkers";
    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player) { shell.WriteLine("Open this panel from your admin client."); return; }
        IoCManager.Resolve<EuiManager>().OpenEui(new AdminBunkerEui(), player);
    }
}
