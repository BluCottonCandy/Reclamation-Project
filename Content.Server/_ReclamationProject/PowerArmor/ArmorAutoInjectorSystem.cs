using System.Linq;
using Content.Server.Body.Components;
using Content.Server.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Lock;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Storage;

namespace Content.Server._ReclamationProject.PowerArmor;

[RegisterComponent]
public sealed partial class ActiveArmorAutoInjectorComponent : Component;

public sealed class ArmorAutoInjectorSystem : EntitySystem
{
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly MobStateSystem _mobs = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private readonly HypospraySystem _hypos = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    private float _elapsed;

    // Keep the most potent variant as a last resort.
    private static readonly (string Item, string Drug)[] Stimpaks =
    [
        ("N14Stimpak", "HealingMixture"),
        ("N14SuperStimpak", "RobustHealingMixture"),
        ("N14StimpakDirty", "DirtyStimpak"),
        ("N14UltraStimpak", "UltraRobustHealingMixture"),
    ];

    public override void Initialize()
    {
        SubscribeLocalEvent<ActiveArmorAutoInjectorComponent, MobStateChangedEvent>((Entity<ActiveArmorAutoInjectorComponent> ent, ref MobStateChangedEvent ev) =>
        {
            if (ev.NewMobState == MobState.Critical)
                TryInject(ent);
        });
    }

    public override void Update(float frameTime)
    {
        _elapsed += frameTime;
        if (_elapsed < 1f)
            return;
        _elapsed = 0f;
        var query = EntityQueryEnumerator<ActiveArmorAutoInjectorComponent>();
        while (query.MoveNext(out var uid, out _))
            if (_mobs.IsCritical(uid))
                TryInject(uid);
    }

    private static bool HasStimpak(Solution solution) =>
        Stimpaks.Any(stim => solution.ContainsReagent(stim.Drug, null));

    private void TryInject(EntityUid wearer)
    {
        if (!TryComp<BloodstreamComponent>(wearer, out var blood) ||
            !_solutions.TryGetInjectableSolution(wearer, out _, out var destination) ||
            destination.AvailableVolume <= 0 || HasStimpak(destination) ||
            !_solutions.ResolveSolution(wearer, blood.ChemicalSolutionName, ref blood.ChemicalSolution, out var chemicals) ||
            HasStimpak(chemicals) ||
            _solutions.ResolveSolution(wearer, blood.BloodSolutionName, ref blood.BloodSolution, out var bloodstream) && HasStimpak(bloodstream) ||
            _solutions.ResolveSolution(wearer, blood.BloodTemporarySolutionName, ref blood.TemporarySolution, out var pending) && HasStimpak(pending))
            return;

        Entity<HyposprayComponent>? best = null;
        var priority = Stimpaks.Length;
        var slots = _inventory.GetSlotEnumerator(wearer, SlotFlags.BACK | SlotFlags.BELT | SlotFlags.POCKET);
        while (slots.MoveNext(out var slot))
        {
            if (slot.ContainedEntity is {} item)
                FindDose(item, ref best, ref priority);
            if (priority == 0)
                break;
        }
        if (best is not {} syringe || !_hypos.TryDoInject(syringe, wearer, wearer))
            return;
        if (_solutions.TryGetSolution(syringe.Owner, syringe.Comp.SolutionName, out _, out var contents) && contents.Volume == 0)
            _transform.DropNextTo(syringe.Owner, wearer);
    }

    private void FindDose(EntityUid item, ref Entity<HyposprayComponent>? best, ref int priority)
    {
        if (TryComp<HyposprayComponent>(item, out var hypo))
        {
            var index = Array.FindIndex(Stimpaks, stim => stim.Item == MetaData(item).EntityPrototype?.ID);
            if (index >= 0 && index < priority && _solutions.TryGetSolution(item, hypo.SolutionName, out _, out var contents) &&
                contents.ContainsReagent(Stimpaks[index].Drug, null))
            {
                best = (item, hypo);
                priority = index;
            }
        }
        if (priority == 0 || !TryComp<StorageComponent>(item, out var storage) || TryComp<LockComponent>(item, out var lockComp) && lockComp.Locked)
            return;
        foreach (var child in storage.Container.ContainedEntities)
            FindDose(child, ref best, ref priority);
    }
}
