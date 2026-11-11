// #Reclamation Project - Pip-Boy bio-monitor + bullet embedding
using System.Linq;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server.Medical.Components;
using Content.Server.Popups;
using Content.Server._ReclamationProject.Medical;
using Content.Server.Radiation.Components;
using Content.Server.Temperature.Components;
using Content.Shared._Misfits.Special;
using Content.Shared._ReclamationProject.Medical;
using Content.Shared._ReclamationProject.Medical.BioMonitor;
using Content.Shared._Shitmed.Medical.Surgery.Tools;
using Content.Shared._Shitmed.Targeting;
using Content.Shared.Actions;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage;
using Content.Shared.DoAfter;
using Content.Shared.Explosion;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Server._ReclamationProject.Institute.Uplink;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Popups;
using Content.Shared.Projectiles;
using Content.Shared.Tag;
using Content.Shared.Verbs;
using Content.Server.GameTicking;
using Robust.Shared.Containers;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._ReclamationProject.Medical.BioMonitor;

/// <summary>
/// The Pip-Boy bio-monitor: a body-doll health screen.
/// Opened for yourself with a worn/held Pip-Boy or INT 10, or for anyone with a med scanner (health analyzer).
/// Also rolls bullet fragments into limbs and handles pulling them out.
/// </summary>
public sealed partial class BioMonitorSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedBodySystem _body = default!;
    [Dependency] private readonly BloodstreamSystem _bloodstream = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly MobThresholdSystem _thresholds = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly SharedProjectileSystem _projectile = default!;
    [Dependency] private readonly SharedSpecialSystem _special = default!;
    [Dependency] private readonly TagSystem _tag = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly TraumaSystem _trauma = default!;

    public const string ActionProto = "ActionReclamationBioMonitor";
    /// <summary>Shrapnel left in a body by explosions.</summary>
    public const string ShrapnelProto = "ReclamationShrapnelFragment";
    /// <summary>One roll per this much blast damage (at least one, at most <see cref="MaxShrapnelRolls"/>).</summary>
    public const float ShrapnelDamagePerRoll = 25f;
    public const int MaxShrapnelRolls = 3;
    public const float ShrapnelChance = 0.5f;

    /// <summary>Blasts that don't throw metal: fire, cold, energy and the like.</summary>
    private static readonly HashSet<string> NoShrapnelExplosions = new()
    {
        "Cryo", "FireBomb", "IncineratorExplosion", "Misfits40mmIncendiary", "MisfitsPulse",
        "MisfitsPlasma", "MisfitsNukaPlasma", "PowerSink", "Supermatter",
    };

    /// <summary>Hidden container on the body that holds bullet fragments.</summary>
    public const string FragmentContainer = "bio_monitor_fragments";
    private static readonly ProtoId<TagPrototype> PipBoyTag = "MisfitsPipBoy";

    /// <summary>Synths that know their own bodies: they self-med like INT 10.</summary>
    private static readonly HashSet<string> SelfMedicSpecies = new() { "Gen3Synth", "Courser" };

    /// <summary>INT needed to read your own body without a device, and to count as "using tools" when removing objects.</summary>
    public const int SkilledIntelligence = 10;
    /// <summary>Bare hands take this much longer than a tool.</summary>
    public const float HandsOnlyMultiplier = 2f;
    /// <summary>INT needed to pull things out without the extra-bleeding penalty.</summary>
    public const int CarefulIntelligence = 7;
    /// <summary>No perk and INT under 7: rough hands.</summary>
    public const float UnskilledBleedChance = 0.5f;
    /// <summary>INT 7-9.</summary>
    public const float CarefulBleedChance = 0.3f;
    /// <summary>Field Medic / INT 10 / synth working on their own body.</summary>
    public const float SelfMedBleedChance = 0.25f;
    public const float NormalBleedAmount = 2f;
    public const float HeavyBleedAmount = 4f;
    /// <summary>A hemostat in hand halves any bleed chance.</summary>
    public const float HemostatBleedMultiplier = 0.5f;
    public const float ScannerRange = 2.5f;
    /// <summary>How much a stuck fragment hurts a Gen 3 synth / Courser compared to a human.</summary>
    public const float SynthEmbedDamageMultiplier = 0.2f;

    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(1);

    private static readonly TargetBodyPart[] AllParts =
    {
        TargetBodyPart.Head, TargetBodyPart.Torso, TargetBodyPart.Groin,
        TargetBodyPart.LeftArm, TargetBodyPart.LeftHand, TargetBodyPart.RightArm, TargetBodyPart.RightHand,
        TargetBodyPart.LeftLeg, TargetBodyPart.LeftFoot, TargetBodyPart.RightLeg, TargetBodyPart.RightFoot,
    };

    private sealed class Viewing
    {
        public EntityUid Target;
        public BioMonitorSource Source;
    }

    /// <summary>Viewer entity -> what they are looking at.</summary>
    private readonly Dictionary<EntityUid, Viewing> _viewers = new();
    private TimeSpan _nextUpdate;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawn);
        SubscribeLocalEvent<BioMonitorUserComponent, OpenBioMonitorActionEvent>(OnAction);
        SubscribeLocalEvent<BulletEmbedChanceComponent, ProjectileHitEvent>(OnBulletHit);
        SubscribeLocalEvent<BodyComponent, BeforeExplodeEvent>(OnExploded);
        SubscribeLocalEvent<EmbeddableProjectileComponent, BioMonitorRemoveDoAfterEvent>(OnRemoveEmbedDoAfter);
        SubscribeLocalEvent<BulletFragmentComponent, BioMonitorRemoveDoAfterEvent>(OnRemoveFragmentDoAfter);

        SubscribeNetworkEvent<BioMonitorCloseMessage>(OnClientClose);
        SubscribeNetworkEvent<BioMonitorRemoveRequest>(OnRemoveRequest);
    }

    #region Access

    private void OnPlayerSpawn(PlayerSpawnCompleteEvent args)
    {
        if (!HasComp<BodyComponent>(args.Mob))
            return;

        // The button itself only shows up while they can use it (see RefreshActions).
        EnsureComp<BioMonitorUserComponent>(args.Mob);
    }

    /// <summary>Has a Pip-Boy on or in hand, a med scanner in hand, or can self-med.</summary>
    public bool CanSelfScan(EntityUid user) => HasPipBoy(user) || HoldsScanner(user) || IsSkilled(user);

    /// <summary>
    /// Show the Bio-monitor button only while it works: picking up / wearing a Pip-Boy or scanner adds it,
    /// dropping it removes it. INT 10, the Field Medic perk and synths always have it.
    /// </summary>
    private void RefreshActions()
    {
        var query = EntityQueryEnumerator<BioMonitorUserComponent>();
        while (query.MoveNext(out var uid, out var user))
        {
            var want = CanSelfScan(uid);
            if (want && user.Action == null)
            {
                _actions.AddAction(uid, ref user.Action, ActionProto);
            }
            else if (!want && user.Action != null)
            {
                _actions.RemoveAction(uid, user.Action);
                user.Action = null;
            }
        }
    }

    /// <summary>Gen 3 synth or Courser body.</summary>
    public bool IsSynth(EntityUid uid) =>
        TryComp<HumanoidAppearanceComponent>(uid, out var humanoid) && SelfMedicSpecies.Contains(humanoid.Species.Id);

    /// <summary>Institute staff and synths get the Institute-styled screen instead of the Pip-Boy one.</summary>
    public bool IsInstituteViewer(EntityUid viewer) =>
        IsSynth(viewer)
        || _mind.TryGetMind(viewer, out var mindId, out _) && HasComp<InstituteMemberComponent>(mindId);

    public bool HasPipBoy(EntityUid user)
    {
        foreach (var item in _inventory.GetHandOrInventoryEntities(user))
        {
            if (_tag.HasTag(item, PipBoyTag))
                return true;
        }

        return false;
    }

    /// <summary>
    /// "Can self-med": INT 10, the Field Medic perk, or being a Gen 3 synth / Courser.
    /// Lets you open your own bio-monitor with no device, and counts as using tools when pulling things out.
    /// </summary>
    public bool IsSkilled(EntityUid user)
    {
        if (HasComp<SelfMedicComponent>(user))
            return true;

        if (IsSynth(user))
            return true;

        return _special.UsesSpecialStats(user)
               && _special.GetEffective(user, SpecialStat.Intelligence) >= SkilledIntelligence;
    }

    private int Intelligence(EntityUid user) =>
        _special.UsesSpecialStats(user) ? _special.GetEffective(user, SpecialStat.Intelligence) : 0;

    /// <summary>
    /// Chance (and how much) a wound starts bleeding when <paramref name="user"/> pulls something out of <paramref name="patient"/>.
    /// Skilled medics (Field Medic / INT 10 / synth) don't cause bleeding on others, but have a 25% chance on themselves.
    /// INT 7-9: 30% normal bleed. Anyone else: 50% heavy bleed. A hemostat halves the chance.
    /// </summary>
    public (float Chance, float Amount) BleedRisk(EntityUid user, EntityUid patient)
    {
        float chance, amount = NormalBleedAmount;
        if (IsSkilled(user))
            chance = user == patient ? SelfMedBleedChance : 0f;
        else if (Intelligence(user) >= CarefulIntelligence)
            chance = CarefulBleedChance;
        else
        {
            chance = UnskilledBleedChance;
            amount = HeavyBleedAmount;
        }

        if (HoldsHemostat(user))
            chance *= HemostatBleedMultiplier;

        return (chance, amount);
    }

    private bool HoldsScanner(EntityUid user) =>
        _hands.EnumerateHeld(user).Any(HasComp<HealthAnalyzerComponent>);

    private bool HoldsHemostat(EntityUid user) =>
        _hands.EnumerateHeld(user).Any(HasComp<HemostatComponent>);

    private void OnAction(Entity<BioMonitorUserComponent> ent, ref OpenBioMonitorActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        // Toggle: pressing the button again closes it.
        if (_viewers.ContainsKey(ent))
        {
            Close(ent, true);
            return;
        }

        BioMonitorSource source;
        if (HasPipBoy(ent))
            source = BioMonitorSource.PipBoy;
        else if (IsSkilled(ent))
            source = BioMonitorSource.Intelligence;
        else if (HoldsScanner(ent))
            source = BioMonitorSource.Scanner;
        else
        {
            _popup.PopupEntity(Loc.GetString("bio-monitor-no-access"), ent, ent, PopupType.SmallCaution);
            return;
        }

        Open(ent, ent, source);
    }

    private bool StillValid(EntityUid viewer, Viewing view)
    {
        if (Deleted(view.Target) || Deleted(viewer))
            return false;

        if (view.Target == viewer)
        {
            return view.Source switch
            {
                BioMonitorSource.PipBoy => HasPipBoy(viewer) || IsSkilled(viewer) || HoldsScanner(viewer),
                BioMonitorSource.Intelligence => IsSkilled(viewer) || HasPipBoy(viewer) || HoldsScanner(viewer),
                _ => HoldsScanner(viewer) || HasPipBoy(viewer) || IsSkilled(viewer),
            };
        }

        return HoldsScanner(viewer)
               && _transform.InRange(Transform(viewer).Coordinates, Transform(view.Target).Coordinates, ScannerRange);
    }

    #endregion

    #region Window

    public void Open(EntityUid viewer, EntityUid target, BioMonitorSource source)
    {
        if (!TryComp<ActorComponent>(viewer, out var actor))
            return;

        _viewers[viewer] = new Viewing { Target = target, Source = source };
        RaiseNetworkEvent(BuildState(viewer, target, source), actor.PlayerSession);
    }

    private void Close(EntityUid viewer, bool tellClient)
    {
        if (!_viewers.Remove(viewer))
            return;

        if (tellClient && TryComp<ActorComponent>(viewer, out var actor))
            RaiseNetworkEvent(new BioMonitorCloseMessage(), actor.PlayerSession);
    }

    private void OnClientClose(BioMonitorCloseMessage msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is { } viewer)
            _viewers.Remove(viewer);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextUpdate)
            return;

        _nextUpdate = _timing.CurTime + UpdateInterval;
        RefreshActions();

        foreach (var (viewer, view) in _viewers.ToArray())
        {
            if (!TryComp<ActorComponent>(viewer, out var actor))
            {
                _viewers.Remove(viewer);
                continue;
            }

            if (!StillValid(viewer, view))
            {
                Close(viewer, true);
                continue;
            }

            RaiseNetworkEvent(BuildState(viewer, view.Target, view.Source), actor.PlayerSession);
        }
    }

    public BioMonitorStateMessage BuildState(EntityUid viewer, EntityUid target, BioMonitorSource source)
    {
        var hasTool = HoldsHemostat(viewer);
        var skilled = IsSkilled(viewer);
        var (bleedChance, bleedAmount) = BleedRisk(viewer, target);

        var msg = new BioMonitorStateMessage
        {
            Target = GetNetEntity(target),
            TargetName = Name(target),
            Source = source,
            Self = viewer == target,
            Dead = _mobState.IsDead(target),
            Critical = _mobState.IsCritical(target),
            ViewerHasTool = hasTool,
            ViewerSkilled = skilled,
            InstituteStyle = IsInstituteViewer(viewer),
            SyntheticPatient = IsSynth(target),
        };

        // Overall health against the crit threshold.
        if (TryComp<DamageableComponent>(target, out var damageable))
        {
            if (_thresholds.TryGetThresholdForState(target, MobState.Critical, out var crit) && crit > 0)
                msg.Overall = Math.Clamp(100f - damageable.TotalDamage.Float() / crit.Value.Float() * 100f, 0f, 100f);

            msg.Radiation = damageable.Damage.DamageDict.GetValueOrDefault("Radiation").Float();
            msg.Suffocation = damageable.Damage.DamageDict.GetValueOrDefault("Asphyxiation").Float();
            msg.PoisonDamage = damageable.Damage.DamageDict.GetValueOrDefault("Poison").Float();
        }

        if (msg.Dead)
            msg.Overall = 0f;

        if (TryComp<BloodstreamComponent>(target, out var blood))
        {
            msg.Blood = Math.Clamp(_bloodstream.GetBloodLevelPercentage(target, blood) * 100f, 0f, 100f);
            msg.BleedRate = blood.BleedAmount;

            if (blood.ChemicalSolution is { } chems)
            {
                foreach (var reagent in chems.Comp.Solution.Contents)
                {
                    string id = reagent.Reagent.Prototype;
                    if (id.StartsWith("RadAway"))
                        msg.RadAway = true;
                    else if (id == "RadX")
                        msg.RadX = true;

                    var chem = new BioMonitorChem { Name = id, Quantity = MathF.Round(reagent.Quantity.Float(), 1) };
                    if (_proto.TryIndex<ReagentPrototype>(id, out var proto))
                    {
                        chem.Name = proto.LocalizedName;
                        chem.Kind = proto.Group switch
                        {
                            "Toxins" => BioMonitorChemKind.Poison,
                            "Medicine" => BioMonitorChemKind.Medicine,
                            "Narcotics" or "Chems" or "N14Alcohol" => BioMonitorChemKind.Drug,
                            _ => BioMonitorChemKind.Other,
                        };
                    }

                    msg.Chemicals.Add(chem);
                }

                // Worst first: poisons, then drugs, then medicine.
                msg.Chemicals = msg.Chemicals.OrderByDescending(c => c.Kind).ThenByDescending(c => c.Quantity).ToList();
            }
        }

        if (TryComp<RadiationReceiverComponent>(target, out var radiation))
            msg.RadExposure = radiation.CurrentRadiation;

        if (TryComp<TraumaBodyComponent>(target, out var trauma))
            msg.InternalBleeding = trauma.InternalBleeding > 0f;

        if (TryComp<TemperatureComponent>(target, out var temp))
            msg.TemperatureC = temp.CurrentTemperature - 273.15f;

        if (TryComp<HungerComponent>(target, out var hunger))
        {
            HungerThreshold hungerLevel = hunger.CurrentThreshold;
            msg.Hunger = Loc.GetString($"bio-monitor-hunger-{hungerLevel.ToString().ToLowerInvariant()}");
        }

        if (TryComp<ThirstComponent>(target, out var thirst))
        {
            ThirstThreshold thirstLevel = thirst.CurrentThirstThreshold;
            msg.Thirst = Loc.GetString($"bio-monitor-thirst-{thirstLevel.ToString().ToLowerInvariant()}");
        }

        msg.Pulse = msg.Dead ? 0 : (int) MathF.Round(70f + (100f - msg.Overall) * 0.9f + (100f - msg.Blood) * 0.6f);

        // Limbs.
        var parts = new Dictionary<TargetBodyPart, BioMonitorPartState>();
        foreach (var part in AllParts)
            parts[part] = new BioMonitorPartState { Part = (ushort) part, Missing = true, Health = 0f };

        foreach (var (partUid, partComp) in _body.GetBodyChildren(target))
        {
            if (_body.GetTargetBodyPart(partComp) is not { } targetPart || !parts.TryGetValue(targetPart, out var state))
                continue;

            state.Missing = false;
            state.Health = 100f;

            if (TryComp<DamageableComponent>(partUid, out var partDamage))
            {
                var max = partComp.IntegrityThresholds.GetValueOrDefault(TargetIntegrity.CriticallyWounded, 100f);
                if (max <= 0f)
                    max = 100f;
                state.Health = Math.Clamp(100f - partDamage.TotalDamage.Float() / max * 100f, 0f, 100f);

                foreach (var (type, amount) in partDamage.Damage.DamageDict)
                {
                    if (amount > FixedPoint2.Zero)
                        state.Damage[type] = MathF.Round(amount.Float(), 1);
                }
            }

            if (TryComp<FractureComponent>(partUid, out var fracture))
            {
                state.Fracture = true;
                state.Splinted = fracture.Splinted;
                state.FractureHealing = MathF.Round(fracture.HealingProgress);
                // Bones knit on their own: 1% a minute normally, faster with regeneration or asleep on a medical bed.
                var perMinute = MathF.Max(0.01f, _trauma.RecoveryPerMinute(target));
                state.FractureMinutesLeft = (int) MathF.Ceiling((100f - fracture.HealingProgress) / perMinute);
            }
        }

        // Thrown knives, arrows etc. stuck on the outside of the body (vanilla embedding).
        var children = Transform(target).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (!TryComp<EmbeddableProjectileComponent>(child, out var embed) || embed.Target != target)
                continue;

            AddStuck(parts, child, embed.TargetBodyPart, false, embed.RemovalTime ?? 3f, hasTool, skilled, bleedChance, bleedAmount);
        }

        // Bullet and shrapnel fragments: inside the body (hidden container), only reachable through the bio-monitor.
        if (_container.TryGetContainer(target, FragmentContainer, out var inside))
        {
            foreach (var frag in inside.ContainedEntities)
            {
                if (TryComp<BulletFragmentComponent>(frag, out var fragment))
                    AddStuck(parts, frag, fragment.Part, fragment.Deep, fragment.RemovalTime, hasTool, skilled, bleedChance, bleedAmount);
            }
        }

        msg.Parts = parts.Values.ToList();
        return msg;
    }

    private void AddStuck(Dictionary<TargetBodyPart, BioMonitorPartState> parts, EntityUid item, TargetBodyPart? part,
        bool deep, float baseTime, bool hasTool, bool skilled, float bleedChance, float bleedAmount)
    {
        if (!parts.TryGetValue(part ?? TargetBodyPart.Torso, out var state))
            state = parts[TargetBodyPart.Torso];

        state.Embedded.Add(new BioMonitorEmbedState
        {
            Item = GetNetEntity(item),
            Name = Name(item),
            Prototype = MetaData(item).EntityPrototype?.ID,
            Deep = deep,
            NeedsTool = deep && !hasTool,
            Seconds = RemovalSeconds(baseTime, deep, hasTool, skilled),
            BleedRisk = bleedChance,
            HeavyBleed = bleedAmount >= HeavyBleedAmount,
        });
    }

    #endregion

    #region Removing embedded objects

    private static float RemovalSeconds(float baseTime, bool deep, bool hasTool, bool skilled)
    {
        // INT 10 / Field Medic / synths count as using tools, except for deep fragments which need a real one.
        var tooled = hasTool || (skilled && !deep);
        return tooled ? baseTime : baseTime * HandsOnlyMultiplier;
    }

    /// <summary>Is this item stuck in (or on) the target? Gives how deep it is and the base removal time.</summary>
    private bool IsStuckIn(EntityUid item, EntityUid target, out bool deep, out float baseTime)
    {
        deep = false;
        baseTime = 3f;

        if (TryComp<BulletFragmentComponent>(item, out var fragment))
        {
            deep = fragment.Deep;
            baseTime = fragment.RemovalTime;
            return fragment.Target == target;
        }

        if (TryComp<EmbeddableProjectileComponent>(item, out var embed))
        {
            baseTime = embed.RemovalTime ?? 3f;
            return embed.Target == target;
        }

        return false;
    }

    private void OnRemoveRequest(BioMonitorRemoveRequest msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } user
            || !_viewers.TryGetValue(user, out var view)
            || !TryGetEntity(msg.Target, out var target)
            || target != view.Target
            || !TryGetEntity(msg.Item, out var item)
            || !IsStuckIn(item.Value, target.Value, out var deep, out var baseTime))
            return;

        if (!_interaction.InRangeUnobstructed(user, target.Value))
        {
            _popup.PopupEntity(Loc.GetString("bio-monitor-too-far"), user, user);
            return;
        }

        if (!_hands.TryGetEmptyHand(user, out _) && !HoldsHemostat(user))
        {
            _popup.PopupEntity(Loc.GetString("bio-monitor-need-hand"), user, user);
            return;
        }

        var hasTool = HoldsHemostat(user);
        var skilled = IsSkilled(user);

        if (deep && !hasTool)
        {
            _popup.PopupEntity(Loc.GetString("bio-monitor-needs-tool", ("item", item.Value)), user, user, PopupType.SmallCaution);
            return;
        }

        var (bleedChance, bleedAmount) = BleedRisk(user, target.Value);
        var time = RemovalSeconds(baseTime, deep, hasTool, skilled);

        if (user != target)
            _popup.PopupEntity(Loc.GetString("bio-monitor-removing-other", ("user", user), ("item", item.Value)), target.Value, target.Value, PopupType.MediumCaution);

        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user, time,
            new BioMonitorRemoveDoAfterEvent { BleedChance = bleedChance, BleedAmount = bleedAmount }, eventTarget: item, target: target)
        {
            DistanceThreshold = SharedInteractionSystem.InteractionRange,
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        });
    }

    private void OnRemoveEmbedDoAfter(Entity<EmbeddableProjectileComponent> ent, ref BioMonitorRemoveDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || ent.Comp.Target is not { } target)
            return;

        args.Handled = true;
        _projectile.RemoveEmbed(ent, ent.Comp, args.User);
        AfterRemoved(ent, target, args.User, args.BleedChance, args.BleedAmount);
    }

    private void OnRemoveFragmentDoAfter(Entity<BulletFragmentComponent> ent, ref BioMonitorRemoveDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || ent.Comp.Target is not { } target)
            return;

        args.Handled = true;
        RemoveFragment(ent, args.User);
        AfterRemoved(ent, target, args.User, args.BleedChance, args.BleedAmount);
    }

    private void AfterRemoved(EntityUid item, EntityUid target, EntityUid user, float bleedChance, float bleedAmount)
    {
        if (bleedChance > 0f && _random.Prob(Math.Clamp(bleedChance, 0f, 1f)))
        {
            _bloodstream.TryModifyBleedAmount(target, bleedAmount);
            _popup.PopupEntity(Loc.GetString("bio-monitor-removed-bleeding", ("item", item)), user, user, PopupType.SmallCaution);
        }
        else
        {
            _popup.PopupEntity(Loc.GetString("bio-monitor-removed", ("item", item)), user, user);
        }

        // Refresh right away so the list updates without waiting for the next tick.
        if (_viewers.TryGetValue(user, out var view) && TryComp<ActorComponent>(user, out var actor))
            RaiseNetworkEvent(BuildState(user, view.Target, view.Source), actor.PlayerSession);
    }

    /// <summary>Does this body have bullet fragments or shrapnel inside it?</summary>
    public bool HasStuckFragments(EntityUid uid) =>
        _container.TryGetContainer(uid, FragmentContainer, out var inside) && inside.ContainedEntities.Count > 0;

    /// <summary>Take a fragment out of the body: it drops out, then goes into the remover's hand if one is free.</summary>
    public void RemoveFragment(Entity<BulletFragmentComponent> fragment, EntityUid? remover)
    {
        if (fragment.Comp.Target is { } target && _container.TryGetContainer(target, FragmentContainer, out var inside))
            _container.Remove(fragment.Owner, inside);

        fragment.Comp.Target = null;
        fragment.Comp.Part = null;

        // Stops the passive damage tick.
        var ev = new RemoveEmbedEvent(remover);
        RaiseLocalEvent(fragment, ref ev);

        if (remover is { } user)
            _hands.TryPickupAnyHand(user, fragment);
    }

    #endregion

    #region Bullet embedding

    private void OnBulletHit(Entity<BulletEmbedChanceComponent> ent, ref ProjectileHitEvent args)
    {
        var target = args.Target;
        if (!HasComp<BodyComponent>(target) || !HasComp<MobStateComponent>(target) || _mobState.IsDead(target))
            return;

        if (!_random.Prob(Math.Clamp(ent.Comp.Chance, 0f, 1f)))
            return;

        // The same limb the shooter aimed at (Shitmed targeting); otherwise a random one.
        TargetBodyPart? part = null;
        if (args.Shooter is { } shooter && TryComp<TargetingComponent>(shooter, out var targeting))
            part = targeting.Target;
        part ??= _body.GetRandomBodyPart(target);

        var fragment = Spawn(ent.Comp.Fragment, Transform(target).Coordinates);
        if (!EmbedFragment(fragment, target, part ?? TargetBodyPart.Torso, args.Shooter))
            QueueDel(fragment);
    }

    /// <summary>
    /// Bombs, grenades, rockets and the like: a blast that hurts a living body can leave shrapnel in random limbs.
    /// </summary>
    private void OnExploded(Entity<BodyComponent> ent, ref BeforeExplodeEvent args)
    {
        if (NoShrapnelExplosions.Contains(args.Id)
            || !HasComp<MobStateComponent>(ent)
            || _mobState.IsDead(ent))
            return;

        var damage = args.Damage.GetTotal().Float();
        if (damage <= 0f)
            return;

        var rolls = Math.Clamp((int) MathF.Ceiling(damage / ShrapnelDamagePerRoll), 1, MaxShrapnelRolls);
        for (var i = 0; i < rolls; i++)
        {
            if (!_random.Prob(ShrapnelChance))
                continue;

            var part = _random.Pick(AllParts);
            var shrapnel = Spawn(ShrapnelProto, Transform(ent).Coordinates);
            if (!EmbedFragment(shrapnel, ent, part))
                QueueDel(shrapnel);
        }
    }

    /// <summary>
    /// Put a fragment inside the target's body, in the given limb. It sits in a hidden container so nobody can see,
    /// click or grab it: the only way out is the bio-monitor's Remove button.
    /// </summary>
    public bool EmbedFragment(EntityUid fragment, EntityUid target, TargetBodyPart part, EntityUid? shooter = null)
    {
        if (!TryComp<BulletFragmentComponent>(fragment, out var comp))
            return false;

        // Don't put things into a limb that isn't there.
        var (partType, symmetry) = _body.ConvertTargetBodyPart(part);
        if (!_body.GetBodyChildrenOfType(target, partType, symmetry: symmetry).Any())
            part = TargetBodyPart.Torso;

        var inside = _container.EnsureContainer<Container>(target, FragmentContainer);
        if (!_container.Insert(fragment, inside))
            return false;

        comp.Target = target;
        comp.Part = part;

        // Starts the passive damage tick on that limb.
        var ev = new EmbedEvent(shooter, target, part);
        RaiseLocalEvent(fragment, ref ev);

        // Synth tissue barely notices a fragment. Their nanites can't push it out, but it does very little harm.
        if (IsSynth(target) && TryComp<EmbedPassiveDamageComponent>(fragment, out var passive))
        {
            passive.Damage *= SynthEmbedDamageMultiplier;
            Dirty(fragment, passive);
        }

        return true;
    }

    #endregion
}
