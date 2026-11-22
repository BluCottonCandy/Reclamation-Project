using System.Linq;
using System.Text.Json;
using Content.Server.Administration;
using Content.Server.Body.Components;
using Content.Server.Medical;
using Content.Shared._ReclamationProject.Magic;
using Content.Shared.Abilities.Psionics;
using Content.Shared.Actions;
using Content.Shared.Administration;
using Content.Shared.Administration.Logs;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Database;
using Content.Shared.EntityEffects;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Robust.Shared.Console;
using Robust.Shared.ContentPack;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Utility;

namespace Content.Server._ReclamationProject.Magic;

/// <summary>
/// #Reclamation Project - FEV Psi-Serum. Every 15u that passes through your blood is one dose.
/// Only players on the FEV whitelist can be awakened; for everyone else every dose is just sickness.
/// Success (60% / 45% / 30% for doses 1-3): a random Psychic or Dark spell in your mind, Telepathy,
/// and on the first success the Arcane language plus a one-time "Manifest Grimoire".
/// More than 20u in your blood at once is an overdose. Synths and robots (no blood) are unaffected.
/// </summary>
public sealed class MagicFEVSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IResourceManager _resources = default!;
    [Dependency] private readonly MagicSystem _magic = default!;
    [Dependency] private readonly Content.Server.Administration.Systems.AdminSystem _adminSystem = default!;
    [Dependency] private readonly MagicGrimoireSystem _grimoire = default!;
    [Dependency] private readonly ActionContainerSystem _actionContainer = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly DamageableSystem _damage = default!;
    [Dependency] private readonly VomitSystem _vomit = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly ISharedAdminLogManager _adminLog = default!;

    public const float DoseSize = 15f;
    public const float OverdoseThreshold = 20f;
    public const int MaxDoses = 3;
    private static readonly ResPath WhitelistPath = new("/reclamation_fev_whitelist.json");

    /// <summary>Whitelisted player ids → last known username.</summary>
    private Dictionary<Guid, string> _whitelist = new();

    public override void Initialize()
    {
        base.Initialize();
        LoadWhitelist();
        SubscribeLocalEvent<MagicManifestGrimoireEvent>(OnManifest);
    }

    #region Whitelist

    private void LoadWhitelist()
    {
        try
        {
            if (!_resources.UserData.Exists(WhitelistPath))
                return;
            var text = _resources.UserData.ReadAllText(WhitelistPath);
            _whitelist = JsonSerializer.Deserialize<Dictionary<Guid, string>>(text) ?? new();
        }
        catch (Exception e)
        {
            Log.Error($"Failed to load the FEV whitelist: {e}");
        }
    }

    private void SaveWhitelist()
    {
        try
        {
            _resources.UserData.WriteAllText(WhitelistPath, JsonSerializer.Serialize(_whitelist));
        }
        catch (Exception e)
        {
            Log.Error($"Failed to save the FEV whitelist: {e}");
        }
    }

    public bool IsWhitelisted(NetUserId user) => _whitelist.ContainsKey(user.UserId);

    public bool IsWhitelisted(EntityUid uid) => TryComp<ActorComponent>(uid, out var actor) && IsWhitelisted(actor.PlayerSession.UserId);

    public void SetWhitelisted(NetUserId user, string name, bool whitelisted)
    {
        if (whitelisted)
            _whitelist[user.UserId] = name;
        else
            _whitelist.Remove(user.UserId);
        SaveWhitelist();
    }

    public IReadOnlyDictionary<Guid, string> Whitelist => _whitelist;

    #endregion

    #region Doses

    /// <summary>Called every metabolism tick while FEV Psi-Serum is in someone's blood.</summary>
    public void Exposure(EntityUid uid, float inBlood, float metabolized)
    {
        if (!HasComp<BloodstreamComponent>(uid))
            return; // synths and robots: no effect

        var fev = EnsureComp<MagicFEVExposureComponent>(uid);

        if (inBlood > OverdoseThreshold)
        {
            _damage.TryChangeDamage(uid, new DamageSpecifier(_proto.Index<DamageTypePrototype>("Cellular"), 3f), ignoreResistances: true, interruptsDoAfters: false);
            if (!fev.Overdosing)
            {
                fev.Overdosing = true;
                _stun.TryKnockdown(uid, TimeSpan.FromSeconds(5), true);
                _magic.Notify(uid, Loc.GetString("magic-fev-overdose"), PopupType.LargeCaution);
            }
        }
        else
        {
            fev.Overdosing = false;
        }

        fev.Metabolized += metabolized;
        while (fev.Metabolized >= DoseSize)
        {
            fev.Metabolized -= DoseSize;
            ResolveDose(uid, fev);
        }
    }

    /// <summary>One full dose has passed through: roll for awakening or sickness.</summary>
    public bool ResolveDose(EntityUid uid, MagicFEVExposureComponent fev)
    {
        fev.Doses++;
        var whitelisted = IsWhitelisted(uid);
        var chance = 0.60f - 0.15f * (fev.Doses - 1);
        var success = whitelisted && fev.Doses <= MaxDoses && _random.Prob(chance);

        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(uid):player} took FEV dose {fev.Doses} (whitelisted: {whitelisted}, success: {success})");

        if (success)
            Awaken(uid, fev);
        else
            Sickness(uid, fev.Doses);
        return success;
    }

    public void Awaken(EntityUid uid, MagicFEVExposureComponent fev)
    {
        if (!fev.Awakened)
        {
            fev.Awakened = true;
            EnsureComp<MagicFEVAwakenedComponent>(uid);
            // Admins' Players list and name overlay pick up the new "FEV" tag.
            if (TryComp<ActorComponent>(uid, out var actor))
                _adminSystem.UpdatePlayerList(actor.PlayerSession);
            if (!_magic.KnowsArcane(uid))
                _magic.TeachArcane(uid);

            var action = _actionContainer.AddAction(uid, "ActionMagicManifestGrimoire");
            if (action != null)
                _actions.GrantContainedAction(uid, uid, action.Value);
        }

        EnsureComp<TelepathyComponent>(uid);
        EnsureComp<MagicPermanentTelepathyComponent>(uid);
        _magic.OpenMind(uid, permanent: true);

        // A random Psychic or Dark spell they don't know yet, held in the mind.
        var caster = EnsureComp<MagicCasterComponent>(uid);
        var options = _proto.EnumeratePrototypes<MagicSpellPrototype>()
            .Where(s => s.Class is MagicClass.Psychic or MagicClass.Dark && !caster.MagicKnownSpells.Contains(s.ID))
            .ToList();

        if (options.Count > 0)
        {
            var spell = _random.Pick(options);
            if (_magic.LearnSpell(uid, spell.ID))
                _adminLog.Add(LogType.Action, LogImpact.Medium, $"{ToPrettyString(uid):player} was awakened by FEV and gained {spell.ID}");
            else
                _magic.Notify(uid, Loc.GetString("magic-limit-reached"));
        }

        _magic.Notify(uid, Loc.GetString("magic-fev-awaken"), PopupType.Large);
    }

    private void Sickness(EntityUid uid, int dose)
    {
        var damage = new DamageSpecifier(_proto.Index<DamageTypePrototype>("Radiation"), 8f * dose)
                     + new DamageSpecifier(_proto.Index<DamageTypePrototype>("Cellular"), 4f * dose);
        _damage.TryChangeDamage(uid, damage, ignoreResistances: true, interruptsDoAfters: false);
        _vomit.Vomit(uid);
        _magic.Notify(uid, Loc.GetString("magic-fev-sick"), PopupType.MediumCaution);
    }

    private void OnManifest(MagicManifestGrimoireEvent args)
    {
        if (args.Handled)
            return;
        args.Handled = true;
        // The book comes with the open schools' codex leaves (Psychic and Dark) and the codex key already bound in.
        var book = _grimoire.GiveBoundGrimoire(args.Performer);
        if (TryComp<MagicGrimoireComponent>(book, out var grimoire))
        {
            grimoire.MagicCodexClasses.Add(MagicClass.Psychic);
            grimoire.MagicCodexClasses.Add(MagicClass.Dark);
            grimoire.MagicHasKey = true;
            Dirty(book, grimoire);
            _grimoire.UpdateUi((book, grimoire));
        }
        _magic.Notify(args.Performer, Loc.GetString("magic-fev-manifest"), PopupType.Medium);
        _actions.RemoveAction(args.Performer, args.Action.Owner);
        QueueDel(args.Action.Owner);
    }

    #endregion
}

/// <summary>Reagent effect on FEV Psi-Serum: hands each metabolism tick to <see cref="MagicFEVSystem"/>.</summary>
public sealed partial class MagicFEVExposure : EntityEffect
{
    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("magic-fev-guidebook");

    public override void Effect(EntityEffectBaseArgs args)
    {
        if (args is not EntityEffectReagentArgs reagentArgs || reagentArgs.Reagent == null)
            return;

        // Metabolized this tick = the reagent's metabolism rate scaled by the tick.
        var metabolized = 1f * (float) reagentArgs.Scale;
        args.EntityManager.System<MagicFEVSystem>().Exposure(args.TargetEntity, (float) reagentArgs.Quantity, metabolized);
    }
}

[AdminCommand(AdminFlags.Admin)]
public sealed class MagicFEVWhitelistCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _ents = default!;
    [Dependency] private readonly IPlayerLocator _locator = default!;

    public string Command => "magic_fevwhitelist";
    public string Description => "FEV whitelist: only these players can be awakened by FEV Psi-Serum. Saved between rounds.";
    public string Help => "magic_fevwhitelist add <username>\nmagic_fevwhitelist remove <username>\nmagic_fevwhitelist list";

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var fev = _ents.System<MagicFEVSystem>();
        if (args.Length >= 1 && args[0] == "list")
        {
            shell.WriteLine(fev.Whitelist.Count == 0
                ? "The FEV whitelist is empty."
                : "FEV whitelist:\n" + string.Join("\n", fev.Whitelist.Select(p => $"  {p.Value} ({p.Key})")));
            return;
        }

        if (args.Length < 2 || args[0] is not ("add" or "remove"))
        {
            shell.WriteError(Help);
            return;
        }

        var located = await _locator.LookupIdByNameOrIdAsync(args[1]);
        if (located == null)
        {
            shell.WriteError($"No player called {args[1]}.");
            return;
        }

        var add = args[0] == "add";
        fev.SetWhitelisted(located.UserId, located.Username, add);
        shell.WriteLine($"{located.Username} {(add ? "added to" : "removed from")} the FEV whitelist.");
    }
}
