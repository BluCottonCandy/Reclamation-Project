using Content.Server.Chemistry.Components;
using Content.Server.Chemistry.Containers.EntitySystems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Vapor;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Map;

namespace Content.Server.Weapons.Ranged.Systems;

public sealed partial class GunSystem
{
    [Dependency] private readonly SolutionContainerSystem _solutionContainer = default!;

    protected override void InitializeSolution()
    {
        base.InitializeSolution();

        SubscribeLocalEvent<SolutionAmmoProviderComponent, MapInitEvent>(OnSolutionMapInit);
        SubscribeLocalEvent<SolutionAmmoProviderComponent, SolutionContainerChangedEvent>(OnSolutionChanged);
    }

    private void OnSolutionMapInit(Entity<SolutionAmmoProviderComponent> entity, ref MapInitEvent args)
    {
        UpdateSolutionShots(entity.Owner, entity.Comp);
    }

    private void OnSolutionChanged(Entity<SolutionAmmoProviderComponent> entity, ref SolutionContainerChangedEvent args)
    {
        if (args.Solution.Name == entity.Comp.SolutionId)
            UpdateSolutionShots(entity.Owner, entity.Comp, args.Solution);
    }

    protected override void UpdateSolutionShots(EntityUid uid, SolutionAmmoProviderComponent component, Solution? solution = null)
    {
        var shots = 0;
        var maxShots = 0;
        if (solution == null && !_solutionContainer.TryGetSolution(uid, component.SolutionId, out _, out solution))
        {
            component.Shots = shots;
            component.MaxShots = maxShots;
            Dirty(uid, component);
            return;
        }

        var ammoVolume = component.RequiredReagent is { } reagent
            ? solution.GetReagentQuantity(new(reagent, null))
            : solution.Volume;
        shots = (int) (ammoVolume / component.FireCost);
        maxShots = (int) (solution.MaxVolume / component.FireCost);

        component.Shots = shots;
        component.MaxShots = maxShots;
        Dirty(uid, component);

        UpdateSolutionAppearance(uid, component);
    }

    protected override (EntityUid Entity, IShootable) GetSolutionShot(EntityUid uid, SolutionAmmoProviderComponent component, EntityCoordinates position)
    {
        var (ent, shootable) = base.GetSolutionShot(uid, component, position);

        if (!_solutionContainer.TryGetSolution(uid, component.SolutionId, out var solution, out _))
            return (ent, shootable);

        Solution newSolution;
        if (component.RequiredReagent is { } reagent)
        {
            var cost = FixedPoint2.New(component.FireCost);
            if (!_solutionContainer.RemoveReagent(solution.Value, reagent, cost))
                return (ent, shootable);
            newSolution = new Solution(reagent, cost);
        }
        else
        {
            newSolution = _solutionContainer.SplitSolution(solution.Value, component.FireCost);
        }

        if (newSolution.Volume <= FixedPoint2.Zero)
            return (ent, shootable);

        if (TryComp<AppearanceComponent>(ent, out var appearance))
        {
            Appearance.SetData(ent, VaporVisuals.Color, newSolution.GetColor(ProtoManager).WithAlpha(1f), appearance);
            Appearance.SetData(ent, VaporVisuals.State, true, appearance);
        }

        // Add the solution to the vapor and actually send the thing
        if (_solutionContainer.TryGetSolution(ent, VaporComponent.SolutionName, out var vaporSolution, out _))
        {
            _solutionContainer.TryAddSolution(vaporSolution.Value, newSolution);
        }
        return (ent, shootable);
    }
}
