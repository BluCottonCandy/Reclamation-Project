using System.Linq;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Shared._ReclamationProject.Magic;
using Content.Shared.Administration.Logs;
using Content.Shared.Alert;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Database;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._ReclamationProject.Magic;

/// <summary>
/// #Reclamation Project - Dark magic (Corruption) and Blood magic (paid with blood instead of mana).
/// </summary>
public sealed class MagicDarkBloodSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly AlertsSystem _alerts = default!;
    [Dependency] private readonly MagicSystem _magic = default!;
    [Dependency] private readonly BloodstreamSystem _bloodstream = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly DamageableSystem _damage = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly ISharedAdminLogManager _adminLog = default!;
    [Dependency] private readonly Content.Server.Atmos.EntitySystems.FlammableSystem _flammable = default!;

    public static readonly ProtoId<AlertPrototype> CorruptionAlert = "MagicCorruption";
    private const int WhisperCount = 6;

    private float _accumulator;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MagicCasterComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<MagicDarkPactEvent>(OnDarkPact);
        SubscribeLocalEvent<MagicClotEvent>(OnClot);
        SubscribeLocalEvent<MagicBloodBoilEvent>(OnBloodBoil);
        SubscribeLocalEvent<MagicIgniteSpellEvent>(OnIgnite);
    }

    #region Corruption

    /// <summary>
    /// Adds Corruption for a dark cast. Returns false if the spell backfired (only possible at a full bar).
    /// </summary>
    public bool AddCorruption(Entity<MagicCasterComponent> ent, float amount)
    {
        var wasFull = ent.Comp.MagicStrain >= 100f;
        var before = ent.Comp.MagicStrain;
        ent.Comp.MagicStrain = MathF.Min(100f, ent.Comp.MagicStrain + amount);
        Dirty(ent);
        UpdateAlert(ent);

        if (before < 25f && ent.Comp.MagicStrain >= 25f)
            _magic.Notify(ent, Loc.GetString("magic-corruption-eyes"), PopupType.MediumCaution);

        if (wasFull && _random.Prob(0.25f))
        {
            var damage = new DamageSpecifier(_proto.Index<DamageTypePrototype>("Cold"), 15f);
            _damage.TryChangeDamage(ent, damage, ignoreResistances: true);
            _magic.Notify(ent, Loc.GetString("magic-corruption-backfire"), PopupType.LargeCaution);
            _adminLog.Add(LogType.Action, LogImpact.Low, $"{ToPrettyString(ent.Owner):player}'s spell backfired at full Arcane Strain");
            return false;
        }
        return true;
    }

    public void UpdateAlert(Entity<MagicCasterComponent> ent)
    {
        if (ent.Comp.MagicStrain <= 0.01f)
        {
            _alerts.ClearAlert(ent, CorruptionAlert);
            return;
        }
        var severity = (short) Math.Clamp((int) MathF.Ceiling(ent.Comp.MagicStrain / 10f), 1, 10);
        _alerts.ShowAlert(ent, CorruptionAlert, severity);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _accumulator += frameTime;
        if (_accumulator < 1f)
            return;
        var dt = _accumulator;
        _accumulator = 0f;
        var now = _timing.CurTime;

        var query = EntityQueryEnumerator<MagicCasterComponent>();
        while (query.MoveNext(out var uid, out var caster))
        {
            if (caster.MagicStrain <= 0f)
                continue;

            caster.MagicStrain = MathF.Max(0f, caster.MagicStrain - caster.StrainDecayPerSecond * dt);
            Dirty(uid, caster);
            UpdateAlert((uid, caster));

            if (_mobState.IsDead(uid))
                continue;

            // 50%+: whispers only you can hear.
            if (caster.MagicStrain >= 50f && now >= caster.NextWhisper)
            {
                caster.NextWhisper = now + TimeSpan.FromSeconds(_random.Next(40, 90));
                _magic.Notify(uid, Loc.GetString($"magic-whisper-{_random.Next(1, WhisperCount + 1)}"), PopupType.Medium);
            }

            // 75%+: nosebleeds.
            if (caster.MagicStrain >= 75f && now >= caster.NextNosebleed)
            {
                caster.NextNosebleed = now + TimeSpan.FromSeconds(_random.Next(60, 120));
                _bloodstream.TryModifyBleedAmount(uid, 1f);
                _magic.Notify(uid, Loc.GetString("magic-corruption-nosebleed"), PopupType.MediumCaution);
            }
        }
    }

    private void OnExamined(Entity<MagicCasterComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.MagicStrain >= 25f)
            args.PushMarkup(Loc.GetString("magic-corruption-examine", ("target", ent.Owner)));
    }

    #endregion

    #region Blood

    /// <summary>Pays blood for a blood spell: from a held blood container first, then from your veins.</summary>
    public bool TryPayBlood(EntityUid uid, float amount)
    {
        foreach (var held in _hands.EnumerateHeld(uid))
        {
            if (!_solutions.TryGetDrainableSolution(held, out var soln, out var solution))
                continue;
            if (solution.GetTotalPrototypeQuantity("Blood") < amount)
                continue;
            _solutions.SplitSolution(soln.Value, amount);
            return true;
        }

        if (!TryComp<BloodstreamComponent>(uid, out var blood)
            || !_solutions.ResolveSolution(uid, blood.BloodSolutionName, ref blood.BloodSolution, out var bloodSolution))
            return false;

        // Don't let one spell drain you completely.
        if (bloodSolution.Volume < amount + 10)
            return false;
        return _bloodstream.TryModifyBloodLevel(uid, -amount, blood);
    }

    private void OnClot(MagicClotEvent args)
    {
        if (args.Handled || !_magic.TryPayForSpell(args.Action, args.Performer))
            return;
        args.Handled = true;
        _bloodstream.TryModifyBleedAmount(args.Performer, -100f);
        _bloodstream.TryModifyBloodLevel(args.Performer, args.BloodRestored);
        _magic.Notify(args.Performer, Loc.GetString("magic-clot-done"));
    }

    private void OnBloodBoil(MagicBloodBoilEvent args)
    {
        if (args.Handled || !HasComp<BloodstreamComponent>(args.Target))
            return;
        if (!_magic.TryPayForSpell(args.Action, args.Performer))
            return;
        args.Handled = true;
        var damage = new DamageSpecifier(_proto.Index<DamageTypePrototype>("Heat"), args.Heat);
        _damage.TryChangeDamage(args.Target, damage, ignoreResistances: true, origin: args.Performer);
        _magic.Flash(args.Target, _magic.ClassOf(args.Action), "Hit");
        _magic.Notify(args.Target, Loc.GetString("magic-blood-boil-target"), PopupType.LargeCaution);
    }

    #endregion

    private void OnIgnite(MagicIgniteSpellEvent args)
    {
        if (args.Handled)
            return;
        // A Misfits campfire has its own fire: light it.
        if (TryComp<Content.Shared._Misfits.Campfire.CampfireComponent>(args.Target, out var campfire))
        {
            if (campfire.Lit || !_magic.TryPayForSpell(args.Action, args.Performer))
                return;
            args.Handled = true;
            EntityManager.System<Content.Shared._Misfits.Campfire.SharedCampfireSystem>().SetLit((args.Target, campfire), true, args.Performer);
            _magic.Flash(args.Target, _magic.ClassOf(args.Action), "Hit");
            return;
        }
        if (!TryComp<Content.Server.Atmos.Components.FlammableComponent>(args.Target, out var flammable))
            return;
        if (!_magic.TryPayForSpell(args.Action, args.Performer))
            return;
        args.Handled = true;
        _flammable.AdjustFireStacks(args.Target, args.FireStacks, flammable);
        if (args.Heat > 0)
            _damage.TryChangeDamage(args.Target, new Content.Shared.Damage.DamageSpecifier(_proto.Index<Content.Shared.Damage.Prototypes.DamageTypePrototype>("Heat"), args.Heat), origin: args.Performer);
        _flammable.Ignite(args.Target, args.Performer, flammable);
        _magic.Flash(args.Target, _magic.ClassOf(args.Action), "Hit");
    }

    private void OnDarkPact(MagicDarkPactEvent args)
    {
        if (args.Handled || !_magic.TryPayForSpell(args.Action, args.Performer))
            return;
        args.Handled = true;
        var damage = new DamageSpecifier(_proto.Index<DamageTypePrototype>("Cellular"), args.HealthCost);
        _damage.TryChangeDamage(args.Performer, damage, ignoreResistances: true, interruptsDoAfters: false);
        _magic.RestoreMana(args.Performer, args.ManaGain);
        _magic.Notify(args.Performer, Loc.GetString("magic-dark-pact"), PopupType.Medium);
    }
}
