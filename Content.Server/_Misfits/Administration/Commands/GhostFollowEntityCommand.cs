// #Misfits Add - Ghost-follow console command: if the caller is a ghost, follow a target entity by NetEntity ID.
// Admins are still auto-aghoted first so the admin menu keeps its current behavior.
using Content.Server.Administration.Managers;
using Content.Shared.Administration;
using Content.Shared.Follower;
using Content.Shared.Ghost;
using Robust.Server.Console;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;

namespace Content.Server._Misfits.Administration.Commands;

[AnyCommand]
public sealed class GhostFollowEntityCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entManager = default!;
    [Dependency] private readonly IAdminManager _adminManager = default!;
    [Dependency] private readonly IServerConsoleHost _consoleHost = default!;

    public string Command => "ghostfollow";
    public string Description => "Makes your ghost follow the given entity.";
    public string Help => "ghostfollow <net entity id>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var player = shell.Player;
        if (player == null)
        {
            shell.WriteError("Only players can use this command.");
            return;
        }

        if (args.Length != 1)
        {
            shell.WriteError(Help);
            return;
        }

        if (!NetEntity.TryParse(args[0], out var netEnt) || !_entManager.TryGetEntity(netEnt, out var target))
        {
            shell.WriteError($"Could not find entity with ID '{args[0]}'.");
            return;
        }

        if (target is not { } targetUid)
        {
            shell.WriteError($"Could not find entity with ID '{args[0]}'.");
            return;
        }

        if (player.AttachedEntity is not { } followerUid ||
            !_entManager.TryGetComponent<GhostComponent>(followerUid, out var ghost))
        {
            if (_adminManager.HasAdminFlag(player, AdminFlags.Admin))
            {
                _consoleHost.ExecuteCommand(player, "aghost");
                if (player.AttachedEntity is not { } aghostUid ||
                    !_entManager.TryGetComponent<GhostComponent>(aghostUid, out ghost))
                {
                    shell.WriteError("You must be a ghost to use this command.");
                    return;
                }

                followerUid = aghostUid;
            }
            else
            {
                shell.WriteError("You must be a ghost to use this command.");
                return;
            }
        }

        if (_entManager.TrySystem<FollowerSystem>(out var follower))
            follower.StartFollowingEntity(followerUid, targetUid);
        else
            shell.WriteError("Could not start following the target.");
    }
}
