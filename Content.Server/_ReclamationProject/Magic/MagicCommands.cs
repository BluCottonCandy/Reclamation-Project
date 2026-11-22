using Content.Server.Administration;
using Content.Shared._ReclamationProject.Magic;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;

namespace Content.Server._ReclamationProject.Magic;

// #Reclamation Project - admin console commands for Arcane magic. All start with "magic_".

internal static class MagicCommandHelpers
{
    public static bool TryGetTarget(IConsoleShell shell, IEntityManager ents, string[] args, int index, out EntityUid target)
    {
        target = default;
        if (args.Length > index)
        {
            if (NetEntity.TryParse(args[index], out var net) && ents.TryGetEntity(net, out var ent))
            {
                target = ent.Value;
                return true;
            }
            shell.WriteError($"No entity {args[index]}.");
            return false;
        }

        if (shell.Player?.AttachedEntity is { } self)
        {
            target = self;
            return true;
        }
        shell.WriteError("Give an entity id, or run this while in a body.");
        return false;
    }
}

[AdminCommand(AdminFlags.Admin)]
public sealed class MagicGrantAllCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _ents = default!;
    public string Command => "magic_grantall";
    public string Description => "Teaches the Arcane language and every spell (in the mind, no book needed).";
    public string Help => "magic_grantall [entity]";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (!MagicCommandHelpers.TryGetTarget(shell, _ents, args, 0, out var target))
            return;
        _ents.System<MagicSystem>().GrantAll(target);
        shell.WriteLine($"Granted every spell to {_ents.ToPrettyString(target)}.");
    }
}

[AdminCommand(AdminFlags.Admin)]
public sealed class MagicRemoveAllCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _ents = default!;
    public string Command => "magic_removeall";
    public string Description => "Forgets every spell in the mind and un-readies every page in their grimoires.";
    public string Help => "magic_removeall [entity]";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (!MagicCommandHelpers.TryGetTarget(shell, _ents, args, 0, out var target))
            return;
        _ents.System<MagicSystem>().RemoveAll(target);
        shell.WriteLine($"Removed all spells from {_ents.ToPrettyString(target)}.");
    }
}

[AdminCommand(AdminFlags.Admin)]
public sealed class MagicGrantCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _ents = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    public string Command => "magic_grant";
    public string Description => "Teaches one spell (in the mind). Ignores the 8-spell limit.";
    public string Help => "magic_grant <spell id> [entity]";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 1 || !_proto.HasIndex<MagicSpellPrototype>(args[0]))
        {
            shell.WriteError("Unknown spell. " + Help);
            return;
        }
        if (!MagicCommandHelpers.TryGetTarget(shell, _ents, args, 1, out var target))
            return;

        var magic = _ents.System<MagicSystem>();
        if (!magic.KnowsArcane(target))
            magic.TeachArcane(target);
        magic.LearnSpell(target, args[0], ignoreLimit: true);
        shell.WriteLine($"Taught {args[0]} to {_ents.ToPrettyString(target)}.");
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length == 1
            ? CompletionResult.FromHintOptions(CompletionHelper.PrototypeIDs<MagicSpellPrototype>(), "spell")
            : CompletionResult.Empty;
    }
}

[AdminCommand(AdminFlags.Admin)]
public sealed class MagicWordsCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _ents = default!;
    public string Command => "magic_words";
    public string Description => "Shows this round's secret Arcane words and every spell's true (Empowered) order.";
    public string Help => "magic_words";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        shell.WriteLine(_ents.System<MagicItemsSystem>().WordsReport());
    }
}

[AdminCommand(AdminFlags.Admin)]
public sealed class MagicArcaneCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _ents = default!;
    public string Command => "magic_arcane";
    public string Description => "Teaches (or with 'remove', takes away) the Arcane language.";
    public string Help => "magic_arcane [entity] [remove]";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var remove = args.Length > 0 && args[^1] == "remove";
        var idArgs = remove ? args[..^1] : args;
        if (!MagicCommandHelpers.TryGetTarget(shell, _ents, idArgs, 0, out var target))
            return;

        var magic = _ents.System<MagicSystem>();
        if (remove)
            magic.RemoveArcane(target);
        else
            magic.TeachArcane(target);
        shell.WriteLine($"{(remove ? "Removed" : "Taught")} Arcane {(remove ? "from" : "to")} {_ents.ToPrettyString(target)}.");
    }
}

[AdminCommand(AdminFlags.Admin)]
public sealed class MagicChantlessCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _ents = default!;
    public string Command => "magic_chantless";
    public string Description => "Turns instant casting (no charging) on or off for someone.";
    public string Help => "magic_chantless [entity] [true|false]";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (!MagicCommandHelpers.TryGetTarget(shell, _ents, args, 0, out var target))
            return;
        var caster = _ents.EnsureComponent<MagicCasterComponent>(target);
        var on = args.Length > 1 && bool.TryParse(args[1], out var value) ? value : !caster.MagicChantless;
        _ents.System<MagicSystem>().SetChantless((target, caster), on);
        shell.WriteLine($"Instant casting {(on ? "on" : "off")} for {_ents.ToPrettyString(target)}.");
    }
}

[AdminCommand(AdminFlags.Admin)]
public sealed class MagicUnsealCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _ents = default!;
    public string Command => "magic_unseal";
    public string Description => "Unseals every school of magic for someone (only Psychic and Dark are open by default), or seals them again.";
    public string Help => "magic_unseal [entity] [true|false]";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (!MagicCommandHelpers.TryGetTarget(shell, _ents, args, 0, out var target))
            return;
        var caster = _ents.EnsureComponent<MagicCasterComponent>(target);
        var on = args.Length > 1 && bool.TryParse(args[1], out var value) ? value : !caster.MagicAllSchoolsUnlocked;
        _ents.System<MagicSystem>().SetAllSchoolsUnlocked(target, on);
        shell.WriteLine($"Every school {(on ? "unsealed" : "sealed again")} for {_ents.ToPrettyString(target)}.");
    }
}

[AdminCommand(AdminFlags.Admin)]
public sealed class MagicGrantOpenCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _ents = default!;
    public string Command => "magic_grantopen";
    public string Description => "Teaches the Arcane language and every Psychic and Dark spell (the open schools) in the mind.";
    public string Help => "magic_grantopen [entity]";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (!MagicCommandHelpers.TryGetTarget(shell, _ents, args, 0, out var target))
            return;
        _ents.System<MagicSystem>().GrantOpenSchools(target);
        shell.WriteLine($"Granted every Psychic and Dark spell to {_ents.ToPrettyString(target)}.");
    }
}
