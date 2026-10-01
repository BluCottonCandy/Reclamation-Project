using Content.Server.Administration.Logs;
using Content.Server.EUI;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server.Administration.Commands;

[AdminCommand(AdminFlags.Logs)]
public sealed class OpenAdminLogsCommand : IConsoleCommand
{
    public string Command => "adminlogs";
    public string Description => "Opens the admin logs panel.";
    // #Misfits Tweak - Optional username argument pre-filters the logs search field,
    // used by the bwoink panel "Logs" quick-action button.
    public string Help => $"Usage: {Command} [username]";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteLine("This does not work from the server console.");
            return;
        }

        var eui = IoCManager.Resolve<EuiManager>();
        var ui = new AdminLogsEui();
        eui.OpenEui(ui, player);

        // #Misfits Add - If a username was supplied, pre-fill the search filter so the
        // panel opens focused on that player's activity.
        if (args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
            ui.SetLogFilter(search: args[0]);
    }
}
