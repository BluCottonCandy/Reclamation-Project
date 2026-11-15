using Content.Server.Body.Components;
using Content.Server.Chemistry.Containers.EntitySystems;
using Content.Shared.Body.Organ;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Robust.Shared.Timing;
using Robust.Shared.Random;

namespace Content.Server.Body.Systems
{
    public sealed class StomachSystem : EntitySystem
    {
        [Dependency] private readonly IGameTiming _gameTiming = default!;
        [Dependency] private readonly SolutionContainerSystem _solutionContainerSystem = default!;
        [Dependency] private readonly IRobustRandom _random = default!;

        public const string DefaultSolutionName = "stomach";

        private readonly HashSet<EntityUid> _active = new();
        private readonly List<EntityUid> _workBuffer = new();
        private TimeSpan _lastUpdate;

        public override void Initialize()
        {
            SubscribeLocalEvent<StomachComponent, ComponentStartup>(OnStartup);
            SubscribeLocalEvent<StomachComponent, ComponentShutdown>(OnShutdown);
            SubscribeLocalEvent<StomachComponent, SolutionContainerChangedEvent>(OnSolutionChanged);
            SubscribeLocalEvent<StomachComponent, MapInitEvent>(OnMapInit);
            SubscribeLocalEvent<StomachComponent, EntityPausedEvent>(OnPaused);
            SubscribeLocalEvent<StomachComponent, EntityUnpausedEvent>(OnUnpaused);
            SubscribeLocalEvent<StomachComponent, ApplyMetabolicMultiplierEvent>(OnApplyMetabolicMultiplier);
        }

        private void OnMapInit(Entity<StomachComponent> ent, ref MapInitEvent args)
        {
            ent.Comp.NextUpdate = _gameTiming.CurTime + ent.Comp.UpdateInterval * (1+_random.NextFloat());
            _active.Add(ent.Owner);
        }

        private void OnStartup(Entity<StomachComponent> ent, ref ComponentStartup args)
        {
            // Inspect once after initialization; restored stomach contents must also resume.
            _active.Add(ent.Owner);
        }

        private void OnShutdown(Entity<StomachComponent> ent, ref ComponentShutdown args)
        {
            _active.Remove(ent.Owner);
        }

        private void OnSolutionChanged(Entity<StomachComponent> ent, ref SolutionContainerChangedEvent args)
        {
            if (args.SolutionId != DefaultSolutionName)
                return;

            if (args.Solution.Contents.Count > 0 || ent.Comp.ReagentDeltas.Count > 0)
                WakeForDigestion(ent.Owner, ent.Comp);
            else
                _active.Remove(ent.Owner);
        }

        private void WakeForDigestion(EntityUid uid, StomachComponent stomach)
        {
            if (_active.Contains(uid))
                return;

            if (!MetaData(uid).EntityPaused)
                AdvanceIdleClock(stomach);
            _active.Add(uid);
        }

        private void AdvanceIdleClock(StomachComponent stomach)
        {
            // Empty stomachs used to advance this timer every interval. Fast-forward only
            // those empty intervals, keeping the same phase without digesting new food in a
            // burst of overdue ticks. Use the last system update so a meal received before
            // this frame's update can still be processed on its normally due tick.
            var interval = stomach.UpdateInterval.Ticks;
            if (interval <= 0 || stomach.NextUpdate > _lastUpdate)
                return;

            var intervals = (_lastUpdate - stomach.NextUpdate).Ticks / interval + 1;
            stomach.NextUpdate += TimeSpan.FromTicks(intervals * interval);
        }

        private void OnPaused(Entity<StomachComponent> ent, ref EntityPausedEvent args)
        {
            // Finish the empty intervals before pausing. Food added while paused must
            // not count the pause as digestion time or shift the timer a second time.
            if (!_active.Contains(ent.Owner))
                AdvanceIdleClock(ent.Comp);
        }

        private void OnUnpaused(Entity<StomachComponent> ent, ref EntityUnpausedEvent args)
        {
            ent.Comp.NextUpdate += args.PausedTime;
        }

        public override void Update(float frameTime)
        {
            var now = _gameTiming.CurTime;
            _lastUpdate = now;
            _workBuffer.Clear();
            _workBuffer.AddRange(_active);
            foreach (var uid in _workBuffer)
            {
                if (TerminatingOrDeleted(uid) || !TryComp<StomachComponent>(uid, out var stomach))
                {
                    _active.Remove(uid);
                    continue;
                }

                if (MetaData(uid).EntityPaused)
                    continue;

                if (now < stomach.NextUpdate || !TryComp(uid, out OrganComponent? organ)
                    || !TryComp(uid, out SolutionContainerManagerComponent? sol))
                    continue;

                stomach.NextUpdate += stomach.UpdateInterval;

                // Get our solutions
                if (!_solutionContainerSystem.ResolveSolution((uid, sol), DefaultSolutionName, ref stomach.Solution, out var stomachSolution))
                    continue;

                if (stomach.ReagentDeltas.Count == 0 && stomachSolution.Contents.Count == 0)
                {
                    _active.Remove(uid);
                    continue;
                }

                if (organ.Body is not { } body || !_solutionContainerSystem.TryGetSolution(body, stomach.BodySolutionName, out var bodySolution))
                    continue;

                Solution? transferSolution = null;

                for (var i = stomach.ReagentDeltas.Count - 1; i >= 0; i--)
                {
                    var delta = stomach.ReagentDeltas[i];
                    delta.Increment(stomach.UpdateInterval);
                    if (delta.Lifetime > stomach.DigestionDelay)
                    {
                        if (stomachSolution.TryGetReagent(delta.ReagentQuantity.Reagent, out var reagent))
                        {
                            if (reagent.Quantity > delta.ReagentQuantity.Quantity)
                                reagent = new(reagent.Reagent, delta.ReagentQuantity.Quantity);

                            stomachSolution.RemoveReagent(reagent);
                            transferSolution ??= new Solution();
                            transferSolution.AddReagent(reagent);
                        }

                        stomach.ReagentDeltas.RemoveAt(i);
                    }
                }

                _solutionContainerSystem.UpdateChemicals(stomach.Solution.Value);

                // Transfer everything to the body solution!
                if (transferSolution != null)
                    _solutionContainerSystem.TryAddSolution(bodySolution.Value, transferSolution);
            }
        }

        private void OnApplyMetabolicMultiplier(
            Entity<StomachComponent> ent,
            ref ApplyMetabolicMultiplierEvent args)
        {
            // Preserve the next due time under the old rate before changing the interval.
            if (!_active.Contains(ent.Owner) && !MetaData(ent.Owner).EntityPaused)
                AdvanceIdleClock(ent.Comp);

            if (args.Apply)
            {
                ent.Comp.UpdateInterval *= args.Multiplier;
                return;
            }

            // This way we don't have to worry about it breaking if the stasis bed component is destroyed
            ent.Comp.UpdateInterval /= args.Multiplier;
        }

        public bool CanTransferSolution(
            EntityUid uid,
            Solution solution,
            StomachComponent? stomach = null,
            SolutionContainerManagerComponent? solutions = null)
        {
            return Resolve(uid, ref stomach, ref solutions, logMissing: false)
                && _solutionContainerSystem.ResolveSolution((uid, solutions), DefaultSolutionName, ref stomach.Solution, out var stomachSolution)
                // TODO: For now no partial transfers. Potentially change by design
                && stomachSolution.CanAddSolution(solution);
        }

        public bool TryTransferSolution(
            EntityUid uid,
            Solution solution,
            StomachComponent? stomach = null,
            SolutionContainerManagerComponent? solutions = null)
        {
            if (!Resolve(uid, ref stomach, ref solutions, logMissing: false)
                || !_solutionContainerSystem.ResolveSolution((uid, solutions), DefaultSolutionName, ref stomach.Solution)
                || !CanTransferSolution(uid, solution, stomach, solutions))
            {
                return false;
            }

            _solutionContainerSystem.TryAddSolution(stomach.Solution.Value, solution);
            // Add each reagent to ReagentDeltas. Used to track how long each reagent has been in the stomach
            foreach (var reagent in solution.Contents)
            {
                stomach.ReagentDeltas.Add(new StomachComponent.ReagentDelta(reagent));
            }

            if (stomach.ReagentDeltas.Count > 0)
                WakeForDigestion(uid, stomach);

            return true;
        }
    }
}
