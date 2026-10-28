using Content.Server.Stack;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Stacks;

namespace Content.Server.Chemistry.EntitySystems;

/// <summary>Consumes reagent-bearing stack items separately, preserving leftovers and container capacity.</summary>
public sealed class StackSolutionTransferSystem : EntitySystem
{
    [Dependency] private readonly StackSystem _stacks = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private readonly SolutionTransferSystem _transfer = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<SolutionTransferComponent, StackSolutionTransferEvent>(OnAfterInteract);
    }

    private void OnAfterInteract(EntityUid uid, SolutionTransferComponent comp, ref StackSolutionTransferEvent ev)
    {
        var args = ev.Interaction;
        if (args.Handled || !args.CanReach || args.Target is not { } target || target == uid)
            return;

        var source = uid;
        var destination = target;
        if (!comp.StackAware)
        {
            if (!comp.CanReceive || !TryComp<SolutionTransferComponent>(target, out var from) || !from.StackAware || !from.CanSend)
                return;
            source = target;
            destination = uid;
        }
        else if (!comp.CanSend)
            return;

        if (!_solutions.TryGetRefillableSolution(destination, out var to, out var contents) ||
            !_solutions.TryGetDrainableSolution(source, out var fromSolution, out var sourceContents))
            return;

        args.Handled = true;
        if (contents.AvailableVolume == 0 || sourceContents.Volume == 0)
        {
            _transfer.Transfer(args.User, source, fromSolution.Value, destination, to.Value, sourceContents.Volume);
            return;
        }

        var allowance = contents.AvailableVolume;
        if (TryComp<RefillableSolutionComponent>(destination, out var refill) && refill.MaxRefill is { } maxRefill)
            allowance = FixedPoint2.Min(allowance, maxRefill);

        var count = TryComp<StackComponent>(source, out var stack) ? stack.Count : 1;
        for (var i = 0; i < count && contents.AvailableVolume > 0 && allowance > 0; i++)
        {
            var unit = stack is { Count: > 1 }
                ? _stacks.Split(source, 1, Transform(source).Coordinates, stack)
                : source;
            if (unit == null || !_solutions.TryGetDrainableSolution(unit.Value, out var solution, out var liquid))
                break;

            var amount = _transfer.Transfer(args.User, unit.Value, solution.Value, destination, to.Value,
                FixedPoint2.Min(liquid.Volume, FixedPoint2.Min(contents.AvailableVolume, allowance)));
            allowance -= amount;

            // A partly dissolved piece remains usable but cannot merge into a fresh stack and lose or duplicate its liquid.
            if (liquid.Volume > 0 && amount > 0)
                RemComp<StackComponent>(unit.Value);
            if (amount == 0)
                break;
        }
    }
}
