using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server.GameTicking;
using Content.Server.Language;
using Content.Server.Speech.Components;
using Content.Server.Traits;
using Content.Shared.Customization.Systems;
using Content.Shared.Traits;
using Robust.Shared.Prototypes;

namespace Content.Server._ReclamationProject.Triggermen;

/// <summary>Free faction language and a default accent that yields to selected speech traits.</summary>
public sealed class TriggermenItalianSystem : EntitySystem
{
    [Dependency] private readonly LanguageSystem _languages = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnSpawn, after: new[] { typeof(TraitSystem) });
    }

    private void OnSpawn(PlayerSpawnCompleteEvent args)
        => ApplyJobDefaults(args.Mob, args.JobId, args.Profile.TraitPreferences);

    public void ApplyJobDefaults(EntityUid mob, string? job, IEnumerable<string> selectedTraits)
    {
        if (job == null || !(job == "Triggerman" || job.StartsWith("Triggermen", StringComparison.Ordinal))) return;
        // Preserve the old language ID so saved selections, manuals and NPC knowledge remain compatible.
        _languages.AddLanguage(mob, "Latin");
        if (HasSelectedAccent(selectedTraits) || HasComp<ReplacementAccentComponent>(mob)) return;
        var accent = AddComp<ReplacementAccentComponent>(mob);
        accent.Accent = "italian";
    }

    public bool HasSelectedAccent(IEnumerable<string> selectedTraits)
    {
        foreach (var id in selectedTraits)
        {
            if (!_prototypes.TryIndex<TraitPrototype>(id, out var trait)) continue;
            if (trait.Requirements.OfType<CharacterItemGroupRequirement>().Any(r =>
                    r.Group.Id is "N14SpeechAccents" or "TraitsAccents")) return true;
            if (trait.Functions.OfType<TraitAddComponent>().Any(f => f.Components.Values.Any(c =>
                    c.Component.GetType().Name.EndsWith("AccentComponent", StringComparison.Ordinal)))) return true;
            if (trait.Functions.OfType<TraitReplaceComponent>().Any(f => f.Components.Values.Any(c =>
                    c.Component.GetType().Name.EndsWith("AccentComponent", StringComparison.Ordinal)))) return true;
        }
        return false;
    }
}
