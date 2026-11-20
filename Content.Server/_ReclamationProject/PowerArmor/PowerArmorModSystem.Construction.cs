using System.Linq;
using Content.Shared._ReclamationProject.PowerArmor;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Stacks;
using Content.Shared.Tools.Components;
using Content.Shared.Tools.Systems;
using Content.Shared.Verbs;
using Robust.Shared.Containers;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._ReclamationProject.PowerArmor;

public sealed partial class PowerArmorModSystem
{
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedToolSystem _tools = default!;
    [Dependency] private readonly SharedStackSystem _stacks = default!;
    [Dependency] private readonly ISharedPlayerManager _players = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly IComponentFactory _factory = default!;

    private sealed record Step(string Input, int Amount, int Seconds);
    private static readonly Step[] Recipe =
    [
        new("Anchoring", 0, 2), new("Steel", 10, 3), new("Welding", 0, 4),
        new("N14Scrap", 20, 4), new("Screwing", 0, 2), new("Gold", 2, 3), new("Cutting", 0, 2)
    ];

    private static string Kit(ArmorModKind kind) => "AtomCatsMod" + kind;

    private void DescribeBuild(Entity<PowerArmorPlateComponent> plate, ExaminedEvent ev)
    {
        if (plate.Comp.Project is {} kind)
            ev.PushText(Loc.GetString("pa-mod-progress", ("mod", _prototypes.Index<EntityPrototype>(Kit(kind)).Name),
                ("step", Loc.GetString("pa-mod-step-" + plate.Comp.BuildStep))));
        else if (plate.Comp.Slot != ArmorPlateSlot.Pauldrons)
            ev.PushText(Loc.GetString("pa-mod-wrench-hint"));
    }

    private bool CanFitProject(Entity<PowerArmorPlateComponent> plate, ArmorModKind kind)
    {
        var proto = _prototypes.Index<EntityPrototype>(Kit(kind));
        if (!proto.TryGetComponent<PowerArmorModComponent>(out var mod, _factory) || mod.Slot != plate.Comp.Slot) return false;
        var fitted = Installed(plate);
        return fitted.Count < 2 && !fitted.Any(uid => Comp<PowerArmorModComponent>(uid).Kind == kind);
    }

    private void AddBuildVerbs(Entity<PowerArmorPlateComponent> plate, GetVerbsEvent<AlternativeVerb> ev)
    {
        if (plate.Comp.Project != null || _hands.GetActiveItem(ev.User) is not {} tool ||
            !_tools.HasQuality(tool, Recipe[0].Input) || HasComp<ModularPowerArmorComponent>(Transform(plate).ParentUid)) return;
        var user = ev.User;
        foreach (var kind in Enum.GetValues<ArmorModKind>())
        {
            if (!CanFitProject(plate, kind)) continue;
            ev.Verbs.Add(new AlternativeVerb {
                Text = Loc.GetString("pa-mod-build", ("mod", _prototypes.Index<EntityPrototype>(Kit(kind)).Name)),
                Act = () => StartStep(plate, kind, user, tool)
            });
        }
    }

    private void BuildInteraction(Entity<PowerArmorPlateComponent> plate, InteractUsingEvent ev)
    {
        if (plate.Comp.Slot == ArmorPlateSlot.Pauldrons) return;
        if (plate.Comp.Project is {} kind)
        {
            ev.Handled = true;
            StartStep(plate, kind, ev.User, ev.Used);
            return;
        }
        if (!_tools.HasQuality(ev.Used, Recipe[0].Input)) return;
        ev.Handled = true;
        if (!IsMechanic(ev.User)) { Notice(ev.User, "pa-mod-mechanic-only"); return; }
        if (_players.TryGetSessionByEntity(ev.User, out var session))
            RaiseNetworkEvent(new ChooseArmorModEvent(GetNetEntity(plate)), session);
    }

    public bool StartStep(Entity<PowerArmorPlateComponent> plate, ArmorModKind kind, EntityUid user, EntityUid used)
    {
        if (!IsMechanic(user)) { Notice(user, "pa-mod-mechanic-only"); return false; }
        if (plate.Comp.Project is {} pending && pending != kind || !CanFitProject(plate, kind) ||
            HasComp<ModularPowerArmorComponent>(Transform(plate).ParentUid) || _hands.GetActiveItem(user) != used ||
            !_interaction.InRangeAndAccessible(user, plate.Owner)) return false;
        var step = Recipe[plate.Comp.BuildStep];
        if (!ValidInput(used, user, step))
        { Notice(user, "pa-mod-step-" + plate.Comp.BuildStep); return false; }
        var ev = new BuildArmorModEvent { Kind = kind, Version = plate.Comp.BuildVersion };
        return _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user, TimeSpan.FromSeconds(step.Seconds), ev, plate, target: plate, used: used)
        {
            NeedHand = true, BreakOnMove = true, BreakOnDamage = true, BreakOnHandChange = true,
            DistanceThreshold = 1.5f, MultiplyDelay = false
        });
    }

    private bool ValidInput(EntityUid used, EntityUid user, Step step)
    {
        if (step.Amount > 0)
            return TryComp<StackComponent>(used, out var stack) && stack.StackTypeId.Id == step.Input && stack.Count >= step.Amount;
        if (!_tools.HasQuality(used, step.Input)) return false;
        var attempt = new ToolUseAttemptEvent(user, step.Input == "Welding" ? 1f : 0f);
        RaiseLocalEvent(used, attempt);
        return !attempt.Cancelled;
    }

    private void FinishStep(Entity<PowerArmorPlateComponent> plate, ref BuildArmorModEvent ev)
    {
        if (ev.Cancelled || ev.Handled || ev.Version != plate.Comp.BuildVersion || ev.Used is not {} used ||
            ev.Target != plate.Owner || !IsMechanic(ev.User) || _hands.GetActiveItem(ev.User) != used ||
            HasComp<ModularPowerArmorComponent>(Transform(plate).ParentUid) || !_interaction.InRangeAndAccessible(ev.User, plate.Owner) ||
            plate.Comp.Project is {} pending && pending != ev.Kind || !CanFitProject(plate, ev.Kind)) return;
        var step = Recipe[plate.Comp.BuildStep];
        if (!ValidInput(used, ev.User, step)) return;
        if (step.Amount > 0)
        {
            if (!_stacks.Use(used, step.Amount)) return;
        }
        else if (TryComp<ToolComponent>(used, out var tool)) _tools.PlayToolSound(used, tool, ev.User);
        ev.Handled = true;
        plate.Comp.BuildVersion++;
        if (++plate.Comp.BuildStep < Recipe.Length)
        {
            plate.Comp.Project = ev.Kind;
            Notice(ev.User, "pa-mod-step-" + plate.Comp.BuildStep);
            return;
        }
        var module = Spawn(Kit(ev.Kind), Transform(plate).Coordinates);
        if (_containers.Insert(module, _containers.EnsureContainer<Container>(plate, Sockets)))
            _popup.PopupEntity(Loc.GetString("pa-mod-installed", ("mod", module), ("plate", plate.Owner)), ev.User, ev.User);
        plate.Comp.Project = null;
        plate.Comp.BuildStep = 0;
    }
}
