using System.Linq;
using Content.Shared._ReclamationProject.Pack;
using Content.Shared.Alert;
using Content.Shared.Damage;
using Content.Shared.Movement.Systems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Robust.Shared.Network;

namespace Content.Shared._ReclamationProject.Nutrition;

/// <summary>Apply NPC nutrition policy once on startup, without a repeating scan of every NPC.</summary>
public sealed class NpcNutritionSystem : EntitySystem
{
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly AlertsSystem _alerts = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;

    private readonly HashSet<EntityUid> _pending = new();
    private const float AnimalHungerDecay = 0.00925925925926f;
    private const float AnimalThirstDecay = 0.04f;

    public override void Initialize()
    {
        base.Initialize();
        UpdatesBefore.Add(typeof(HungerSystem));
        UpdatesBefore.Add(typeof(ThirstSystem));
        SubscribeLocalEvent<NpcNutritionComponent, ComponentStartup>(OnNutritionStartup);
        SubscribeLocalEvent<PackAnimalComponent, ComponentStartup>(OnPackStartup);
    }

    private void OnNutritionStartup(Entity<NpcNutritionComponent> ent, ref ComponentStartup args)
    {
        if (_net.IsServer) _pending.Add(ent.Owner);
    }

    private void OnPackStartup(Entity<PackAnimalComponent> ent, ref ComponentStartup args)
    {
        if (_net.IsServer) _pending.Add(ent.Owner);
    }

    public override void Update(float frameTime)
    {
        if (!_net.IsServer || _pending.Count == 0) return;
        // Wait until the entity's components have all initialized, including inherited policy
        // overrides. Removing components inside their startup event is not safe.
        var pending = _pending.ToArray();
        _pending.Clear();
        foreach (var uid in pending)
        {
            if (TerminatingOrDeleted(uid)) continue;
            var packAnimal = HasComp<PackAnimalComponent>(uid);
            if (!TryComp<NpcNutritionComponent>(uid, out var policy) && !packAnimal) continue;
            if (packAnimal || policy!.NeedsFood)
                EnableAnimalNeeds(uid);
            else
                RemoveNpcNeeds(uid);
        }
    }

    private void EnableAnimalNeeds(EntityUid uid)
    {
        var newHunger = !HasComp<HungerComponent>(uid);
        var hunger = EnsureComp<HungerComponent>(uid);
        if (newHunger || hunger.BaseDecayRate <= 0)
        {
            // Several organic pet/hostile bases used zero decay. Keep their thresholds and
            // current food level, but use the existing SimpleMobBase animal rate.
            hunger.BaseDecayRate = AnimalHungerDecay;
            hunger.ActualDecayRate = hunger.BaseDecayRate
                * hunger.HungerThresholdDecayModifiers.GetValueOrDefault(hunger.CurrentThreshold, 1f);
            Dirty(uid, hunger);
        }

        var newThirst = !HasComp<ThirstComponent>(uid);
        var thirst = EnsureComp<ThirstComponent>(uid);
        if (newThirst || thirst.BaseDecayRate <= 0)
        {
            thirst.BaseDecayRate = AnimalThirstDecay;
            thirst.ActualDecayRate = thirst.BaseDecayRate * (thirst.CurrentThirstThreshold switch
            {
                ThirstThreshold.OverHydrated => 1.2f,
                ThirstThreshold.Okay => 1f,
                ThirstThreshold.Thirsty => 0.8f,
                _ => 0.6f,
            });
            Dirty(uid, thirst);
        }
    }

    private void RemoveNpcNeeds(EntityUid uid)
    {
        if (TryComp<ThirstComponent>(uid, out var thirst))
            _alerts.ClearAlertCategory(uid, thirst.ThirstyCategory);
        RemComp<HungerComponent>(uid);
        RemComp<ThirstComponent>(uid);
        _movement.RefreshMovementSpeedModifiers(uid);

        // Old maps can contain accumulated nutrition damage. Clear only that damage;
        // preserve injuries and the existing independent PassiveDamage regeneration.
        if (!TryComp<DamageableComponent>(uid, out var damageable)) return;
        var recovery = new DamageSpecifier();
        foreach (var type in new[] { "Starvation", "Dehydration" })
            if (damageable.Damage.DamageDict.TryGetValue(type, out var value) && value > 0)
                recovery.DamageDict[type] = -value;
        if (!recovery.Empty)
            _damageable.TryChangeDamage(uid, recovery, ignoreResistances: true,
                interruptsDoAfters: false, damageable: damageable, doPartDamage: false);
    }
}
