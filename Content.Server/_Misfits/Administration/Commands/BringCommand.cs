// #Misfits Change - Admin command to teleport a player or entity to the admin
using System.Linq;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Server.Player;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;

namespace Content.Server._Misfits.Administration.Commands;

[AdminCommand(AdminFlags.Admin)]
public sealed class BringCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;

    public string Command => "bring";
    public string Description => "Teleports a player or entity to your location by entity ID, username, or character name.";
    public string Help => "bring <entity ID, username, or character name>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 1)
        {
            shell.WriteError(Help);
            return;
        }

        var adminEntity = shell.Player?.AttachedEntity;
        if (adminEntity is not { Valid: true })
        {
            shell.WriteError("You must have an attached entity to use this command.");
            return;
        }

        var name = string.Join(" ", args);
        var targetEntity = FindEntity(name);

        if (targetEntity is not { Valid: true })
        {
            shell.WriteError($"Could not find an entity with ID, username, or character name \"{name}\".");
            return;
        }

        var xformSystem = _entManager.System<SharedTransformSystem>();
        var adminCoords = _entManager.GetComponent<TransformComponent>(adminEntity.Value).Coordinates;
        xformSystem.SetCoordinates(targetEntity.Value, adminCoords);
        xformSystem.AttachToGridOrMap(targetEntity.Value);

        shell.WriteLine($"Teleported {_entManager.ToPrettyString(targetEntity.Value)} to your location.");
    }

    private EntityUid? FindEntity(string name)
    {
        // Try parsing as a numeric entity net ID first.
        if (int.TryParse(name, out var entInt))
        {
            var nent = new NetEntity(entInt);
            if (_entManager.TryGetEntity(nent, out var resolved))
                return resolved;
        }

        // Try exact username match.
        if (_playerManager.TryGetSessionByUsername(name, out var session)
            && session.AttachedEntity is { Valid: true } ent)
        {
            return ent;
        }

        // Fall back to case-insensitive character/entity name match.
        foreach (var playerSession in _playerManager.Sessions)
        {
            if (playerSession.AttachedEntity is not { Valid: true } playerEnt)
                continue;

            var meta = _entManager.GetComponent<MetaDataComponent>(playerEnt);
            if (string.Equals(meta.EntityName, name, StringComparison.OrdinalIgnoreCase))
                return playerEnt;
        }

        return null;
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
            return CompletionResult.FromHintOptions(CompletionHelper.SessionNames(players: _playerManager), "<entity ID, username, or character name>");

        return CompletionResult.Empty;
    }
}
