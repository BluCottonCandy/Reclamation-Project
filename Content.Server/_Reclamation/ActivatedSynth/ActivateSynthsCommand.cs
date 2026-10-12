using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._Reclamation.ActivatedSynth;

/// <summary>Lets administrators exercise the ordinary selection path without waiting an hour.</summary>
[AdminCommand(AdminFlags.Fun)]
public sealed class ActivateSynthsCommand : IConsoleCommand
{
    public string Command => "synthactivate";
    public string Description => "Activate up to two eligible opted-in synth agents now.";
    public string Help => "synthactivate — requires an active round, opt-in, and 10 hours of playtime.";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 0)
        {
            shell.WriteError(Help);
            return;
        }
        var count = EntitySystem.Get<ActivatedSynthSystem>().ActivateBatch();
        shell.WriteLine($"Activated {count} eligible player(s). The regular hourly schedule is unchanged.");
    }
}
