using Content.Shared.Speech.Muting;
using System.Linq;
using Content.Server.Chat.Managers;
using Content.Server.Language;
using Content.Shared.Chat;
using Robust.Shared.Utility;
using Content.Shared._ReclamationProject.Magic;
using Content.Shared.Actions;
using Content.Shared.Administration.Logs;
using Content.Shared.Alert;
using Content.Shared.Bed.Sleep;
using Content.Shared.Buckle.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Database;
using Content.Shared.Magic.Events;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Standing;
using Content.Shared.DoAfter;
using Content.Shared._Misfits.PowerArmor;
using Content.Shared.Clothing;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._ReclamationProject.Magic;

/// <summary>
/// #Reclamation Project - mana, the Arcane language check, paying for spells, and spells held in the mind.
/// </summary>
public sealed class MagicSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly AlertsSystem _alerts = default!;
    [Dependency] private readonly DamageableSystem _damage = default!;
    [Dependency] private readonly LanguageSystem _language = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly ActionContainerSystem _actionContainer = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly StandingStateSystem _standing = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly ISharedAdminLogManager _adminLog = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MetaDataSystem _meta = default!;
    [Dependency] private readonly MagicDarkBloodSystem _darkBlood = default!;
    [Dependency] private readonly Content.Shared.Inventory.InventorySystem _inventory = default!;

    public static readonly ProtoId<Shared.Language.LanguagePrototype> Arcane = "Arcane";
    public static readonly ProtoId<AlertPrototype> ManaAlert = "MagicMana";
    private static readonly ProtoId<DamageTypePrototype> OvercastDamage = "Cellular";

    private float _accumulator;
    private readonly Dictionary<EntityUid, (EntityCoordinates? Coords, EntityUid? Target)> _pendingTargets = new();

    /// <summary>True while a typed (chat) cast is running, so the cost gets the typed-casting surcharge.</summary>
    public bool TypedCastInProgress;

    /// <summary>True while a caster is saying a spell's words aloud, so chat casting doesn't cast it again.</summary>
    public bool SpeakingIncantation;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MagicCasterComponent, ComponentStartup>(OnCasterStartup);
        SubscribeLocalEvent<MagicCasterComponent, ComponentShutdown>(OnCasterShutdown);
        SubscribeLocalEvent<MagicSpellActionComponent, BeforeCastSpellEvent>(OnBeforeCast);
        SubscribeLocalEvent<MagicHealSpellEvent>(OnHealSpell);
        SubscribeLocalEvent<MagicCasterComponent, MagicAdminGrantAllEvent>(OnAdminGrantAll);
        SubscribeLocalEvent<MagicCasterComponent, MagicAdminRemoveAllEvent>(OnAdminRemoveAll);
        SubscribeLocalEvent<MagicCasterComponent, MagicReadDoAfterEvent>(OnReadDone);
        SubscribeLocalEvent<MagicCasterComponent, Content.Shared.Rejuvenate.RejuvenateEvent>(OnRejuvenate);
        SubscribeNetworkEvent<MagicStartChargeEvent>(OnStartCharge);
    }

    private void OnCasterStartup(Entity<MagicCasterComponent> ent, ref ComponentStartup args)
    {
        UpdateAlert(ent);
        foreach (var spell in ent.Comp.MagicKnownSpells.ToList())
            GrantKnownAction(ent, spell);
    }

    private void OnCasterShutdown(Entity<MagicCasterComponent> ent, ref ComponentShutdown args)
    {
        _alerts.ClearAlert(ent, ManaAlert);
    }

    #region Arcane language

    public bool KnowsArcane(EntityUid uid) => _language.CanUnderstand(uid, Arcane);

    /// <summary>Teaches Arcane and gives the mana bar.</summary>
    public void TeachArcane(EntityUid uid)
    {
        _language.AddLanguage(uid, Arcane);
        EnsureComp<MagicCasterComponent>(uid);
        _adminLog.Add(LogType.Action, LogImpact.Medium, $"{ToPrettyString(uid):player} learned the Arcane language");
    }

    public void RemoveArcane(EntityUid uid)
    {
        _language.RemoveLanguage(uid, Arcane);
    }

    #endregion

    #region Mana

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _accumulator += frameTime;
        if (_accumulator >= 1f)
            UpdateHeld();
        if (_accumulator < 1f)
            return;

        var dt = _accumulator;
        _accumulator = 0f;

        var query = EntityQueryEnumerator<MagicCasterComponent>();
        while (query.MoveNext(out var uid, out var caster))
        {
            if (caster.MagicMana >= caster.MagicMaxMana || _mobState.IsDead(uid))
                continue;
            // No refilling while a Phantom cloak is up: it lives on your mana (1 a second) until you drop it.
            if (HasComp<MagicPhantomComponent>(uid))
                continue;

            var rate = caster.MagicRegenPerSecond;
            if (HasComp<SleepingComponent>(uid))
                rate *= caster.SleepingMultiplier;
            else if (_standing.IsDown(uid) || (TryComp<BuckleComponent>(uid, out var buckle) && buckle.Buckled))
                rate *= caster.RestingMultiplier;

            caster.MagicMana = MathF.Min(caster.MagicMaxMana, caster.MagicMana + rate * dt);
            Dirty(uid, caster);
            UpdateAlert((uid, caster));
        }
    }

    public void UpdateAlert(Entity<MagicCasterComponent> ent)
    {
        var max = MathF.Max(1f, ent.Comp.MagicMaxMana);
        var severity = (short) Math.Clamp((int) MathF.Floor(ent.Comp.MagicMana / max * 10f), 0, 10);
        _alerts.ShowAlert(ent, ManaAlert, severity);
    }

    /// <summary>Admin rejuvenate: mana back to full.</summary>
    private void OnRejuvenate(Entity<MagicCasterComponent> ent, ref Content.Shared.Rejuvenate.RejuvenateEvent args)
    {
        ent.Comp.MagicMana = ent.Comp.MagicMaxMana;
        Dirty(ent);
        UpdateAlert(ent);
    }

    /// <summary>Admin: mana cap raised to the top and the bar filled.</summary>
    public void MaxOutMana(EntityUid uid)
    {
        var caster = EnsureComp<MagicCasterComponent>(uid);
        caster.MagicMaxMana = caster.MagicMaxManaCap;
        caster.MagicMana = caster.MagicMaxMana;
        Dirty(uid, caster);
        UpdateAlert((uid, caster));
        Notify(uid, Loc.GetString("magic-mana-maxed", ("max", (int) caster.MagicMaxMana)), PopupType.Medium);
        _adminLog.Add(LogType.Action, LogImpact.Medium, $"Magic: mana maxed for {ToPrettyString(uid):player}");
    }

    /// <summary>Says a spell's words (this round's runes, in their true order) aloud in the Arcane language.</summary>
    public void SpeakIncantation(EntityUid uid, MagicSpellPrototype spell)
    {
        var text = EntityManager.System<MagicWordsSystem>().GetTrueText(spell);
        if (string.IsNullOrWhiteSpace(text) || !_proto.TryIndex(Arcane, out var arcane))
            return;
        SpeakingIncantation = true;
        try
        {
            EntityManager.System<Content.Server.Chat.Systems.ChatSystem>().TrySendInGameICMessage(uid, text,
                Content.Shared.Chat.InGameICChatType.Speak, Content.Shared.Chat.ChatTransmitRange.Normal,
                checkRadioPrefix: false, ignoreActionBlocker: true, languageOverride: arcane);
        }
        finally
        {
            SpeakingIncantation = false;
        }
    }

    public void RestoreMana(EntityUid uid, float amount)
    {
        if (!TryComp<MagicCasterComponent>(uid, out var caster))
            return;
        caster.MagicMana = MathF.Min(caster.MagicMaxMana, caster.MagicMana + amount);
        Dirty(uid, caster);
        UpdateAlert((uid, caster));
    }

    /// <summary>
    /// Takes the mana for a cast. Without enough mana the spell still works, but the missing mana
    /// turns into internal damage. Spending mana slowly raises max mana.
    /// </summary>
    public void PayMana(Entity<MagicCasterComponent> ent, float cost)
    {
        var caster = ent.Comp;
        caster.LastOvercast = 0f;
        if (caster.MagicInfiniteMana || cost <= 0)
            return;

        var missing = MathF.Max(0f, cost - caster.MagicMana);
        caster.LastOvercast = missing / cost;
        caster.MagicMana = MathF.Max(0f, caster.MagicMana - cost);
        // Show it straight away (the mana orb otherwise only updated when mana refilled).
        Dirty(ent);
        UpdateAlert(ent);

        caster.SpentTowardGrowth += cost;
        while (caster.SpentTowardGrowth >= caster.ManaPerGrowth && caster.MagicMaxMana < caster.MagicMaxManaCap)
        {
            caster.SpentTowardGrowth -= caster.ManaPerGrowth;
            caster.MagicMaxMana = MathF.Min(caster.MagicMaxManaCap, caster.MagicMaxMana + 1f);
        }

        if (missing > 0)
        {
            var damage = new DamageSpecifier(_proto.Index(OvercastDamage), missing * caster.OvercastDamagePerMana);
            _damage.TryChangeDamage(ent, damage, ignoreResistances: true, interruptsDoAfters: false);
            Notify(ent, Loc.GetString("magic-overcast"), PopupType.MediumCaution);
        }

        Dirty(ent);
        UpdateAlert(ent);
    }

    #endregion

    #region Casting

    /// <summary>
    /// Checks everything a cast needs and pays for it. Used by every spell action.
    /// The grimoire's own checks (owner, held) happen because its actions are only granted to the owner.
    /// </summary>
    public bool TryPayForSpell(EntityUid action, EntityUid performer)
    {
        if (!TryComp<MagicSpellActionComponent>(action, out var spellAction) || spellAction.Spell is not { } spellId)
            return true; // not one of ours

        if (!TryComp<MagicCasterComponent>(performer, out var caster) || !KnowsArcane(performer))
        {
            Notify(performer, Loc.GetString("magic-cant-cast"));
            return false;
        }

        if (!_proto.TryIndex(spellId, out var spell))
            return false;

        // Metal blocks magic: no casting in power armour at all.
        if (WearingPowerArmor(performer))
        {
            Notify(performer, Loc.GetString("magic-metal-blocks"));
            return false;
        }

        // Mind Lock (muted) stops all casting. A Hex makes the next spells fizzle.
        if (HasComp<MutedComponent>(performer))
        {
            Notify(performer, Loc.GetString("magic-muted-cant-cast"));
            return false;
        }

        TryComp<MagicConditionsComponent>(performer, out var conditions);
        if (conditions != null && conditions.Active.TryGetValue(MagicCondition.Hex, out var hex))
        {
            hex.Power -= 1f;
            if (hex.Power <= 0f)
                conditions.Active.Remove(MagicCondition.Hex);
            Notify(performer, Loc.GetString("magic-hex-fail"));
            return false;
        }

        // Echo: the next spell skips reading and costs no mana.
        var echo = conditions != null && conditions.Active.ContainsKey(MagicCondition.Echo) && spell.ID != "Echo";
        if (echo)
        {
            conditions!.Active.Remove(MagicCondition.Echo);
            caster.ReadyToFire = action;
        }

        // Transmutation: needs a big mana pool and empties it.
        if (spell.RequiredMaxMana > 0f && caster.MagicMaxMana < spell.RequiredMaxMana)
        {
            Notify(performer, Loc.GetString("magic-needs-max-mana", ("mana", (int) spell.RequiredMaxMana)));
            return false;
        }

        // Charging: a spell charges first (you can walk), then fires. A charged, held spell fires at once
        // (it is let go only once the cast really goes through). Instant casters skip the charge entirely.
        var firingHeld = caster.HeldSpell == action;
        if (caster.ReadyToFire == action)
        {
            caster.ReadyToFire = null;
        }
        else if (firingHeld)
        {
        }
        else if (spell.ReadSeconds > 0f && !IsChantless(caster))
        {
            StartReading((performer, caster), action, spell);
            return false;
        }

        // Blood magic pays with blood, not mana. No blood (synths, robots) = no blood magic.
        if (spell.BloodCost > 0f && !_darkBlood.TryPayBlood(performer, spell.BloodCost * (spellAction.Empowered ? 0.75f : 1f)))
        {
            Notify(performer, Loc.GetString("magic-blood-cant-pay"));
            return false;
        }

        // Every spell builds Arcane Strain (bigger spells more, Dark spells extra). At a full bar it can backfire.
        var strain = spell.Rank switch
        {
            MagicRank.Beginner => 4f,
            MagicRank.Intermediate => 7f,
            MagicRank.Saint => 12f,
            _ => 20f,
        } + spell.CorruptionCost;
        if (!_darkBlood.AddCorruption((performer, caster), strain))
            return false;

        var cost = spell.ManaCost * (spellAction.Empowered ? 0.75f : 1f);
        if (spell.Class == MagicClass.Elemental && conditions != null && conditions.Active.ContainsKey(MagicCondition.Avatar))
            cost *= 0.5f;
        if (echo)
            cost = 0f;
        if (spell.UsesAllMana)
            cost = MathF.Max(cost, caster.MagicMana);
        caster.LastPower = spellAction.Empowered ? 1.5f : 1f;
        if (TypedCastInProgress)
            cost *= caster.TypedManaMultiplier;
        if (spellAction.Scroll)
            cost *= 1.5f;
        // Known by heart: 10% cheaper when cast from the mind.
        var fromMind = caster.KnownSpellActions.TryGetValue(spellId, out var mindAction) && mindAction == action;
        if (fromMind && caster.MagicMemorisedSpells.Contains(spellId))
            cost *= 0.9f;
        PayMana((performer, caster), cost);
        caster.CastThisRound.Add(spellId);
        CountCast((performer, caster), spellId, action);

        // The caster says the spell's words aloud in Arcane: mages hear the runes, everyone else hears gibberish.
        // Instant casters cast silently; a spell typed into chat was already spoken.
        if (!IsChantless(caster) && !TypedCastInProgress && !_mobState.IsDead(performer)) // the dead cast in silence
            SpeakIncantation(performer, spell);

        if (spellAction.Scroll && _container.TryGetContainingContainer((action, null, null), out var scrollContainer))
        {
            QueueDel(scrollContainer.Owner);
            Notify(performer, Loc.GetString("magic-scroll-crumbles"));
        }
        _adminLog.Add(LogType.Action, LogImpact.Low, $"{ToPrettyString(performer):player} cast magic spell {spellId}{(spellAction.Empowered ? " (empowered)" : "")}");
        if (firingHeld)
            ReleaseHeld((performer, caster));
        // Everyone nearby sees the spell go off: a pulse in the class colour around the caster.
        Flash(performer, spell.Class, "Cast");
        return true;
    }

    #region Telling the player (popup + chat)

    [Dependency] private readonly IChatManager _chatManager = default!;

    private readonly Dictionary<EntityUid, (string Text, TimeSpan Until)> _lastNotice = new();
    private static readonly TimeSpan RepeatGuard = TimeSpan.FromSeconds(4);

    /// <summary>
    /// Tells someone what magic just did to them: the usual popup, and the same line in their chat
    /// (Notifications), so it can't be missed in a busy fight. The exact same line within 4 s is skipped
    /// (standing in fire, a storm cloud ticking).
    /// </summary>
    public void Notify(EntityUid who, string message, PopupType type = PopupType.Small)
    {
        if (string.IsNullOrWhiteSpace(message) || TerminatingOrDeleted(who))
            return;
        var now = _timing.CurTime;
        if (_lastNotice.TryGetValue(who, out var last) && last.Text == message && last.Until > now)
            return;
        _lastNotice[who] = (message, now + RepeatGuard);

        _popup.PopupEntity(message, who, who, type);
        if (!TryComp<ActorComponent>(who, out var actor))
            return;
        var wrapped = Loc.GetString("magic-chat-wrap", ("message", FormattedMessage.EscapeText(message)));
        _chatManager.ChatMessageToOne(ChatChannel.Notifications, message, wrapped, EntityUid.Invalid, false,
            actor.PlayerSession.Channel, Color.FromHex("#c9a0ff"));
    }

    #endregion

    #region Flashes (what people see)

    private static readonly int[] RingSizes = { 3, 6, 10, 20 };

    /// <summary>The class of the spell behind an action, if it is one of ours.</summary>
    public MagicClass? ClassOf(EntityUid action)
    {
        if (TryComp<MagicSpellActionComponent>(action, out var spellAction) && spellAction.Spell is { } id
            && _proto.TryIndex(id, out var spell))
            return spell.Class;
        return null;
    }

    /// <summary>A short pulse in the class colour on someone. Kind = "Cast" (caster) or "Hit" (target).</summary>
    public void Flash(EntityUid at, MagicClass cls, string kind)
    {
        if (Deleted(at))
            return;
        var id = string.Concat("MagicFlash", kind, cls.ToString());
        if (_proto.HasIndex<EntityPrototype>(id))
            Spawn(id, Transform(at).Coordinates);
    }

    public void Flash(EntityUid at, MagicClass? cls, string kind)
    {
        if (cls is { } c)
            Flash(at, c, kind);
    }

    /// <summary>An area burst in the class colour, about as wide as the spell reaches.</summary>
    public void FlashRing(EntityCoordinates where, MagicClass cls, float radius)
    {
        if (radius <= 0f)
            return;
        var size = RingSizes[^1];
        foreach (var s in RingSizes)
        {
            if (s >= radius * 2f - 0.5f)
            {
                size = s;
                break;
            }
        }
        var id = string.Concat("MagicFlashRing", size.ToString(), cls.ToString());
        if (_proto.HasIndex<EntityPrototype>(id))
            Spawn(id, where);
    }

    #endregion

    /// <summary>
    /// Counts a cast toward memorising. After <see cref="MagicCasterComponent.MemoriseCasts"/> casts of the same
    /// spell the caster is told they know it by heart, and can memorise it by right-clicking themselves.
    /// </summary>
    private void CountCast(Entity<MagicCasterComponent> caster, ProtoId<MagicSpellPrototype> spellId, EntityUid action)
    {
        PractiseChantless(caster);
        if (caster.Comp.MagicMemorisedSpells.Contains(spellId))
            return;

        caster.Comp.MagicCastCounts.TryGetValue(spellId, out var count);
        count++;
        caster.Comp.MagicCastCounts[spellId] = count;

        // Remember the owner's own name for it (the page's note) so the memorised spell keeps it.
        var name = MetaData(action).EntityName;
        if (!caster.Comp.MagicKnownSpellNotes.ContainsKey(spellId) && !string.IsNullOrWhiteSpace(name) && name != "· · ·"
            && HasComp<MagicSpellActionComponent>(action) && name != MetaData(action).EntityPrototype?.Name)
            caster.Comp.MagicKnownSpellNotes[spellId] = name;

        if (count == MagicCasterComponent.MemoriseCasts)
            Notify(caster, Loc.GetString("magic-memorise-ready"), PopupType.Medium);
    }

    /// <summary>Learns a spell by heart (needs 10 casts this round). Uses a slot unless it's already etched.</summary>
    public bool Memorise(EntityUid uid, ProtoId<MagicSpellPrototype> spell)
    {
        if (!TryComp<MagicCasterComponent>(uid, out var caster) || caster.MagicMemorisedSpells.Contains(spell))
            return false;
        if (!caster.MagicCastCounts.TryGetValue(spell, out var count) || count < MagicCasterComponent.MemoriseCasts)
            return false;
        if (!LearnSpell(uid, spell))
        {
            Notify(uid, Loc.GetString("magic-limit-reached"));
            return false;
        }

        caster.MagicMemorisedSpells.Add(spell);
        Notify(uid, Loc.GetString("magic-memorise-done"), PopupType.Medium);
        _adminLog.Add(LogType.Action, LogImpact.Low, $"{ToPrettyString(uid):player} memorised magic spell {spell}");
        return true;
    }

    #region Teleport mishaps

    [Dependency] private readonly Content.Shared.Body.Systems.SharedBodySystem _body = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;
    [Dependency] private readonly Robust.Shared.Random.IRobustRandom _random = default!;

    private static readonly string[] VitalOrgans = { "Brain", "Heart", "Lung" };

    /// <summary>
    /// Teleporting without enough mana can leave part of you behind at the spot you left: a limb (arm, hand,
    /// leg or foot) or a non-vital organ (eyes, tongue, liver, kidneys, stomach...). The chance is 20%, plus
    /// up to 50% more the more mana was missing. Call it right after the caster has moved.
    /// </summary>
    public void TeleportMishap(EntityUid uid, EntityCoordinates origin)
    {
        if (!TryComp<MagicCasterComponent>(uid, out var caster) || caster.LastOvercast <= 0f)
            return;
        var chance = MathF.Min(0.7f, 0.2f + 0.5f * caster.LastOvercast);
        caster.LastOvercast = 0f;
        if (!_random.Prob(chance))
            return;

        // A limb 60% of the time, otherwise an organ; if there's nothing of one kind left, the other.
        if (_random.Prob(0.6f))
        {
            if (!LoseLimb(uid, origin))
                LoseOrgan(uid, origin);
        }
        else if (!LoseOrgan(uid, origin))
        {
            LoseLimb(uid, origin);
        }
    }

    private bool LoseLimb(EntityUid uid, EntityCoordinates origin)
    {
        var limbs = _body.GetBodyChildren(uid)
            .Where(p => p.Component.PartType is Content.Shared.Body.Part.BodyPartType.Arm or Content.Shared.Body.Part.BodyPartType.Hand
                or Content.Shared.Body.Part.BodyPartType.Leg or Content.Shared.Body.Part.BodyPartType.Foot)
            .ToList();
        if (limbs.Count == 0)
            return false;
        var (limb, _) = _random.Pick(limbs);
        if (!_container.TryGetContainingContainer((limb, null, null), out var slot) || !_container.Remove(limb, slot))
            return false;
        _xform.SetCoordinates(limb, origin);
        Notify(uid, Loc.GetString("magic-mishap-limb", ("part", Name(limb))), PopupType.LargeCaution);
        _adminLog.Add(LogType.Action, LogImpact.Medium, $"{ToPrettyString(uid):player} overcast a teleport and left {ToPrettyString(limb):entity} behind");
        return true;
    }

    private bool LoseOrgan(EntityUid uid, EntityCoordinates origin)
    {
        var organs = _body.GetBodyOrgans(uid)
            .Where(o => MetaData(o.Id).EntityPrototype is { } proto && !VitalOrgans.Any(v => proto.ID.Contains(v, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (organs.Count == 0)
            return false;
        var (organ, organComp) = _random.Pick(organs);
        if (!_body.RemoveOrgan(organ, organComp))
            return false;
        _xform.SetCoordinates(organ, origin);
        Notify(uid, Loc.GetString("magic-mishap-organ", ("part", Name(organ))), PopupType.LargeCaution);
        _adminLog.Add(LogType.Action, LogImpact.Medium, $"{ToPrettyString(uid):player} overcast a teleport and left {ToPrettyString(organ):entity} behind");
        return true;
    }

    #endregion

    #region Metal

    /// <summary>True if any worn item is power armour.</summary>
    public bool WearingPowerArmor(EntityUid uid)
    {
        var slots = _inventory.GetSlotEnumerator(uid);
        while (slots.NextItem(out var item))
        {
            if (HasComp<N14PowerArmorComponent>(item))
                return true;
        }
        return false;
    }

    /// <summary>True if any worn item is heavy armour (slows walking to 85% or less).</summary>
    public bool WearingHeavyArmor(EntityUid uid)
    {
        var slots = _inventory.GetSlotEnumerator(uid);
        while (slots.NextItem(out var item))
        {
            // MOD suits are built for wearers who work: they never slow spells down.
            if (HasComp<Content.Shared._Goobstation.Clothing.Components.SealableClothingComponent>(item))
                continue;
            if (TryComp<ClothingSpeedModifierComponent>(item, out var speed) && speed.WalkModifier <= 0.85f)
                return true;
        }
        return false;
    }

    #endregion

    #region Reading

    /// <summary>Starts the reading do-after. Others see the caster reading and can Dispel them.</summary>
    private void StartReading(Entity<MagicCasterComponent> caster, EntityUid action, MagicSpellPrototype spell)
    {
        if (caster.Comp.Reading is { } current && _doAfter.GetStatus(current) == DoAfterStatus.Running)
            return; // already reading something

        // Remember where it was aimed, since the action's event gets reused.
        EntityCoordinates? coords = null;
        EntityUid? target = null;
        if (_actions.TryGetActionData(action, out var data))
        {
            if (data is WorldTargetActionComponent { Event: { } world })
                coords = world.Target;
            else if (data is EntityTargetActionComponent { Event: { } entity })
                target = entity.Target;
        }
        _pendingTargets[action] = (coords, target);
        if (!BeginCharge(caster, action, spell))
            _pendingTargets.Remove(action);
    }

    /// <summary>The charge itself: you can keep walking, but getting hurt breaks it. Heavy armour doubles it.</summary>
    private bool BeginCharge(Entity<MagicCasterComponent> caster, EntityUid action, MagicSpellPrototype spell)
    {
        var seconds = spell.ReadSeconds * (WearingHeavyArmor(caster) ? 2f : 1f);
        var args = new DoAfterArgs(EntityManager, caster, TimeSpan.FromSeconds(seconds), new MagicReadDoAfterEvent(), caster, used: action)
        {
            BreakOnMove = false,
            BreakOnDamage = true,
            BlockDuplicate = true,
        };
        if (!_doAfter.TryStartDoAfter(args, out var id))
            return false;

        caster.Comp.Reading = id;
        caster.Comp.ReadingAction = action;
        caster.Comp.ChargingAction = action;
        Dirty(caster);
        _popup.PopupEntity(Loc.GetString("magic-reading-others", ("caster", caster.Owner)), caster, Filter.PvsExcept(caster), true);
        if (ClassOf(action) is { } cls)
            Flash(caster, cls, "Hit");
        return true;
    }

    private void OnReadDone(Entity<MagicCasterComponent> ent, ref MagicReadDoAfterEvent args)
    {
        ent.Comp.Reading = null;
        ent.Comp.ReadingAction = null;
        ent.Comp.ChargingAction = null;
        Dirty(ent);

        // Charged from the hotbar (no target yet): hold it in the hand and let the player aim.
        if (args.Used is { } held && _chargeToHold.Remove(held))
        {
            if (args.Cancelled || args.Handled || TerminatingOrDeleted(held))
                return;
            args.Handled = true;
            Hold(ent, held);
            return;
        }

        if (args.Used is not { } action || !_pendingTargets.Remove(action, out var aim))
            return;
        if (args.Cancelled || args.Handled)
            return;
        args.Handled = true;

        if (!TryComp<ActionsComponent>(ent, out var actions) || !_actions.TryGetActionData(action, out var data) || data.AttachedEntity != ent.Owner)
            return;

        ent.Comp.ReadyToFire = action;
        switch (data)
        {
            case WorldTargetActionComponent { Event: { } world } when aim.Coords is { } coords:
                world.Target = coords;
                _actions.PerformAction(ent, actions, action, data, world, _timing.CurTime, predicted: false);
                break;
            case EntityTargetActionComponent { Event: { } entity } when aim.Target is { } target && !TerminatingOrDeleted(target):
                entity.Target = target;
                _actions.PerformAction(ent, actions, action, data, entity, _timing.CurTime, predicted: false);
                break;
            case InstantActionComponent { Event: { } instant }:
                _actions.PerformAction(ent, actions, action, data, instant, _timing.CurTime, predicted: false);
                break;
        }
        ent.Comp.ReadyToFire = null;
    }

    #region Charge, hold and aim

    private readonly HashSet<EntityUid> _chargeToHold = new();
    private static readonly TimeSpan HoldTime = TimeSpan.FromSeconds(30);

    /// <summary>Instant casting: studied and practised, or at the max mana cap, or set by an admin.</summary>
    public bool IsChantless(MagicCasterComponent caster) =>
        caster.MagicChantless || caster.MagicMaxMana >= caster.MagicMaxManaCap;

    /// <summary>The player pressed a targeted spell on the hotbar that isn't charged: start charging it.</summary>
    private void OnStartCharge(MagicStartChargeEvent msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } user || !TryGetEntity(msg.Action, out var maybeAction)
            || maybeAction is not { } action)
            return;
        if (!_actions.TryGetActionData(action, out var data) || data.AttachedEntity != user
            || !TryComp<MagicSpellActionComponent>(action, out var spellAction) || spellAction.Spell is not { } spellId
            || !_proto.TryIndex(spellId, out var spell) || !TryComp<MagicCasterComponent>(user, out var caster))
            return;
        if (data.Cooldown is { } cooldown && cooldown.End > _timing.CurTime)
            return;
        if (!KnowsArcane(user))
        {
            Notify(user, Loc.GetString("magic-cant-cast"));
            return;
        }
        if (WearingPowerArmor(user))
        {
            Notify(user, Loc.GetString("magic-metal-blocks"));
            return;
        }
        if (HasComp<MutedComponent>(user))
        {
            Notify(user, Loc.GetString("magic-muted-cant-cast"));
            return;
        }
        if (caster.Reading is { } current && _doAfter.GetStatus(current) == DoAfterStatus.Running)
            return; // already charging something

        // Already held, instant caster, or nothing to charge: ready to aim straight away.
        if (caster.HeldSpell == action || IsChantless(caster) || spell.ReadSeconds <= 0f)
        {
            Hold((user, caster), action);
            return;
        }

        if (BeginCharge((user, caster), action, spell))
        {
            _chargeToHold.Add(action);
            Notify(user, Loc.GetString("magic-charging", ("seconds", (int) MathF.Ceiling(spell.ReadSeconds))));
        }
    }

    /// <summary>Holds a charged spell in the hand (glowing) and tells the client to start aiming it.</summary>
    private void Hold(Entity<MagicCasterComponent> ent, EntityUid action)
    {
        if (ent.Comp.HeldSpell != action)
            ReleaseHeld(ent);
        ent.Comp.HeldSpell = action;
        ent.Comp.HeldUntil = _timing.CurTime + HoldTime;
        if (ent.Comp.HeldGlow == null && ClassOf(action) is { } cls)
        {
            var id = string.Concat("MagicFlashHeld", cls.ToString());
            if (_proto.HasIndex<EntityPrototype>(id))
            {
                var glow = Spawn(id, Transform(ent).Coordinates);
                _xform.SetParent(glow, ent);
                ent.Comp.HeldGlow = glow;
            }
        }
        Dirty(ent);
        if (TryComp<ActorComponent>(ent, out var actor))
            RaiseNetworkEvent(new MagicSpellChargedEvent(GetNetEntity(action)), actor.PlayerSession);
    }

    /// <summary>Lets go of a held spell (it fired, or it fizzled).</summary>
    public void ReleaseHeld(Entity<MagicCasterComponent> ent)
    {
        if (ent.Comp.HeldGlow is { } glow && !TerminatingOrDeleted(glow))
            QueueDel(glow);
        ent.Comp.HeldGlow = null;
        if (ent.Comp.HeldSpell == null)
            return;
        ent.Comp.HeldSpell = null;
        Dirty(ent);
    }

    private void UpdateHeld()
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<MagicCasterComponent>();
        while (query.MoveNext(out var uid, out var caster))
        {
            if (caster.HeldSpell == null || caster.HeldUntil > now)
                continue;
            ReleaseHeld((uid, caster));
            Notify(uid, Loc.GetString("magic-held-fizzles"));
        }
    }

    /// <summary>Practising after studying the treatise: enough casts and spells become instant.</summary>
    private void PractiseChantless(Entity<MagicCasterComponent> caster)
    {
        if (!caster.Comp.MagicChantlessStudied || caster.Comp.MagicChantless)
            return;
        caster.Comp.MagicChantlessPractice++;
        if (caster.Comp.MagicChantlessPractice < MagicCasterComponent.ChantlessPracticeNeeded)
            return;
        SetChantless(caster, true);
    }

    public void SetChantless(Entity<MagicCasterComponent> caster, bool on)
    {
        caster.Comp.MagicChantless = on;
        Dirty(caster);
        Notify(caster, Loc.GetString(on ? "magic-chantless-gained" : "magic-chantless-lost"), PopupType.Large);
        _adminLog.Add(LogType.Action, LogImpact.Medium, $"Magic: instant casting {(on ? "gained" : "removed")} for {ToPrettyString(caster):player}");
    }

    #endregion

    /// <summary>Breaks a spell the target is reading and puts it on cooldown. True if there was one.</summary>
    public bool Dispel(EntityUid target, float cooldown)
    {
        // Dispel breaks Arcane Shackles.
        if (TryComp<Content.Shared.Cuffs.Components.CuffableComponent>(target, out var cuffable))
        {
            foreach (var cuff in cuffable.Container.ContainedEntities.ToList())
            {
                if (HasComp<MagicShacklesComponent>(cuff))
                    QueueDel(cuff);
            }
        }

        if (!TryComp<MagicCasterComponent>(target, out var caster))
            return false;

        // A charged spell held in the hand is broken too.
        if (caster.HeldSpell is { } held)
        {
            _actions.SetCooldown(held, TimeSpan.FromSeconds(cooldown));
            ReleaseHeld((target, caster));
            if (caster.Reading is not { } stillReading || _doAfter.GetStatus(stillReading) != DoAfterStatus.Running)
            {
                Notify(target, Loc.GetString("magic-dispelled"), PopupType.MediumCaution);
                return true;
            }
        }

        if (caster.Reading is not { } reading || _doAfter.GetStatus(reading) != DoAfterStatus.Running)
            return false;

        if (caster.ReadingAction is { } action)
        {
            _pendingTargets.Remove(action);
            _chargeToHold.Remove(action);
            _actions.SetCooldown(action, TimeSpan.FromSeconds(cooldown));
        }

        _doAfter.Cancel(reading);
        caster.Reading = null;
        caster.ReadingAction = null;
        caster.ChargingAction = null;
        Dirty(target, caster);
        Notify(target, Loc.GetString("magic-dispelled"), PopupType.MediumCaution);
        return true;
    }

    #endregion

    private void OnBeforeCast(Entity<MagicSpellActionComponent> ent, ref BeforeCastSpellEvent args)
    {
        if (args.Cancelled)
            return;
        if (!TryPayForSpell(ent, args.Performer))
            args.Cancelled = true;
    }

    private void OnHealSpell(MagicHealSpellEvent args)
    {
        if (args.Handled || !TryPayForSpell(args.Action, args.Performer))
            return;

        args.Handled = true;
        var empowered = TryComp<MagicSpellActionComponent>(args.Action, out var spellAction) && spellAction.Empowered;
        var mult = empowered ? 1.5f : 1f;
        // Heavy Arcane Strain makes healing magic on you weaker.
        if (TryComp<MagicCasterComponent>(args.Target, out var targetCaster) && targetCaster.MagicStrain >= 50f)
            mult *= 0.75f;
        var heal = new DamageSpecifier();
        if (args.Brute > 0)
            heal += new DamageSpecifier(_proto.Index<DamageGroupPrototype>("Brute"), -args.Brute * mult);
        if (args.Burn > 0)
            heal += new DamageSpecifier(_proto.Index<DamageGroupPrototype>("Burn"), -args.Burn * mult);

        _damage.TryChangeDamage(args.Target, heal, ignoreResistances: true, interruptsDoAfters: false, origin: args.Performer, canSever: false);
        Flash(args.Target, ClassOf(args.Action), "Hit");
        Notify(args.Target, Loc.GetString("magic-heal-target"));
    }

    #endregion

    #region Spells held in the mind

    /// <summary>How many active spells someone has: readied pages in their own grimoires + spells in their mind.</summary>
    public int CountActive(EntityUid uid)
    {
        var count = 0;
        if (TryComp<MagicCasterComponent>(uid, out var caster))
            count += caster.MagicKnownSpells.Count;

        var books = EntityQueryEnumerator<MagicGrimoireComponent>();
        while (books.MoveNext(out _, out var book))
        {
            if (book.MagicOwner == uid)
                count += book.MagicPages.Count(p => p.Readied);
        }

        // Torn pages count while their spell is on this person's hotbar.
        var torn = EntityQueryEnumerator<MagicTornPageComponent>();
        while (torn.MoveNext(out _, out var page))
        {
            if (page.MagicOwner == uid && page.Action is { } action
                && _actions.TryGetActionData(action, out var data) && data.AttachedEntity == uid)
                count++;
        }
        return count;
    }

    public bool LearnSpell(EntityUid uid, ProtoId<MagicSpellPrototype> spell, bool ignoreLimit = false)
    {
        var caster = EnsureComp<MagicCasterComponent>(uid);
        if (caster.MagicKnownSpells.Contains(spell))
            return true;
        if (!ignoreLimit && !caster.MagicIgnoreActiveLimit && CountActive(uid) >= MagicCasterComponent.ActiveLimit)
            return false;

        caster.MagicKnownSpells.Add(spell);
        // Knowing a spell means knowing its words: the Codex and the book's Words tab name and spell them out.
        if (_proto.TryIndex(spell, out var learned))
        {
            foreach (var word in learned.Words)
                caster.MagicSolvedWords.Add(word);
            GrantPsychicTelepathy(uid, learned.Class);
        }
        Dirty(uid, caster);
        GrantKnownAction((uid, caster), spell);
        return true;
    }

    /// <summary>
    /// Psychic powers (Psychic or Dark spells, which sit under Psychic) come with the telepathic chat for good.
    /// </summary>
    public void GrantPsychicTelepathy(EntityUid uid, MagicClass spellClass)
    {
        if (spellClass is not (MagicClass.Psychic or MagicClass.Dark))
            return;
        // Marked as permanent so a timed Telepathy condition (Psychic Link, Hivemind) never removes it when it ends.
        EnsureComp<MagicPermanentTelepathyComponent>(uid);
        OpenMind(uid, permanent: true);
        if (!HasComp<Content.Shared.Abilities.Psionics.TelepathyComponent>(uid))
        {
            AddComp<Content.Shared.Abilities.Psionics.TelepathyComponent>(uid);
            Notify(uid, Loc.GetString("magic-telepathy-awakened"), PopupType.Medium);
        }
    }

    /// <summary>
    /// Every human on this server carries psionic insulation (the old psionics stay off), and insulation blocks
    /// the telepathic chat. Magic telepathy lifts it: for good for psychic mages, or only while a Psychic Link or
    /// Hivemind lasts (then <see cref="CloseMind"/> puts it back).
    /// </summary>
    public void OpenMind(EntityUid uid, bool permanent)
    {
        if (permanent)
        {
            RemComp<MagicLiftedInsulationComponent>(uid);
            RemComp<Content.Shared.Abilities.Psionics.PsionicInsulationComponent>(uid);
            return;
        }
        if (RemComp<Content.Shared.Abilities.Psionics.PsionicInsulationComponent>(uid))
            EnsureComp<MagicLiftedInsulationComponent>(uid);
    }

    /// <summary>Puts back insulation that a timed telepathy spell lifted.</summary>
    public void CloseMind(EntityUid uid)
    {
        if (RemComp<MagicLiftedInsulationComponent>(uid))
            EnsureComp<Content.Shared.Abilities.Psionics.PsionicInsulationComponent>(uid);
    }

    public void ForgetSpell(EntityUid uid, ProtoId<MagicSpellPrototype> spell)
    {
        if (!TryComp<MagicCasterComponent>(uid, out var caster) || !caster.MagicKnownSpells.Remove(spell))
            return;

        // Forgetting a memorised spell means 10 more casts to learn it again.
        if (caster.MagicMemorisedSpells.Remove(spell))
            caster.MagicCastCounts.Remove(spell);

        if (caster.KnownSpellActions.Remove(spell, out var action))
        {
            _actions.RemoveAction(uid, action);
            QueueDel(action);
        }
        Dirty(uid, caster);
    }

    private void GrantKnownAction(Entity<MagicCasterComponent> ent, ProtoId<MagicSpellPrototype> spellId)
    {
        if (ent.Comp.KnownSpellActions.ContainsKey(spellId) || !_proto.TryIndex(spellId, out var spell))
            return;

        var action = _actionContainer.AddAction(ent, spell.Action);
        if (action == null)
            return;

        var spellAction = EnsureComp<MagicSpellActionComponent>(action.Value);
        spellAction.Spell = spellId;
        Dirty(action.Value, spellAction);
        if (ent.Comp.MagicKnownSpellNotes.TryGetValue(spellId, out var note) && !string.IsNullOrWhiteSpace(note))
            _meta.SetEntityName(action.Value, note);
        else
            _meta.SetEntityName(action.Value, Loc.GetString(spell.Name));
        _meta.SetEntityDescription(action.Value, spell.Summary ?? string.Empty);
        // Spells in the mind are not pushed onto the hotbar: they wait in the Actions menu, and the player drags
        // the ones they want onto the bar (readied grimoire pages still go straight onto it).
        if (_actions.TryGetActionData(action.Value, out var actionData))
        {
            actionData.AutoPopulate = false;
            Dirty(action.Value, actionData);
        }
        ent.Comp.KnownSpellActions[spellId] = action.Value;
        _actions.GrantContainedAction(ent.Owner, ent.Owner, action.Value);
    }

    private void OnAdminGrantAll(Entity<MagicCasterComponent> ent, ref MagicAdminGrantAllEvent args)
    {
        GrantAll(ent);
    }

    private void OnAdminRemoveAll(Entity<MagicCasterComponent> ent, ref MagicAdminRemoveAllEvent args)
    {
        RemoveAll(ent);
    }

    public void GrantAll(EntityUid uid)
    {
        if (!KnowsArcane(uid))
            TeachArcane(uid);
        SetAllSchoolsUnlocked(uid, true); // an admin handing out everything means every school
        foreach (var spell in _proto.EnumeratePrototypes<MagicSpellPrototype>())
            LearnSpell(uid, spell.ID, ignoreLimit: true);
        // Their grimoires fill in too: every spell written in, every word known, every class tab open.
        var fill = new MagicFillOwnedBooksEvent(uid);
        RaiseLocalEvent(ref fill);
        _adminLog.Add(LogType.Action, LogImpact.High, $"Magic: all spells granted to {ToPrettyString(uid):player}");
    }

    /// <summary>Psychic and Dark (which sits under Psychic) are open to every mage.</summary>
    public static bool IsOpenSchool(MagicClass cls) => cls is MagicClass.Psychic or MagicClass.Dark;

    /// <summary>True if this person may use spells of that school.</summary>
    public bool CanUseSchool(EntityUid uid, MagicClass cls) =>
        IsOpenSchool(cls) || TryComp<MagicCasterComponent>(uid, out var caster) && caster.MagicAllSchoolsUnlocked;

    /// <summary>Unseals (or seals again) every school for someone. Their books' sealed pages wake (or go quiet).</summary>
    public void SetAllSchoolsUnlocked(EntityUid uid, bool unlocked)
    {
        var caster = EnsureComp<MagicCasterComponent>(uid);
        if (caster.MagicAllSchoolsUnlocked == unlocked)
            return;
        caster.MagicAllSchoolsUnlocked = unlocked;
        Dirty(uid, caster);
        var ev = new MagicSchoolsChangedEvent(uid);
        RaiseLocalEvent(ref ev);
        Notify(uid, Loc.GetString(unlocked ? "magic-schools-unsealed" : "magic-schools-sealed"), PopupType.Large);
        _adminLog.Add(LogType.Action, LogImpact.High, $"Magic: every school {(unlocked ? "unsealed" : "sealed again")} for {ToPrettyString(uid):player}");
    }

    /// <summary>
    /// Admin: every Psychic and Dark spell (the open schools), in the mind, words taught. The sealed schools stay
    /// sealed and their books are left alone.
    /// </summary>
    public void GrantOpenSchools(EntityUid uid)
    {
        if (!KnowsArcane(uid))
            TeachArcane(uid);
        foreach (var spell in _proto.EnumeratePrototypes<MagicSpellPrototype>())
        {
            if (IsOpenSchool(spell.Class))
                LearnSpell(uid, spell.ID, ignoreLimit: true);
        }
        _adminLog.Add(LogType.Action, LogImpact.High, $"Magic: every Psychic and Dark spell granted to {ToPrettyString(uid):player}");
    }

    /// <summary>Forgets every spell in the mind and un-readies every page in their grimoires.</summary>
    public void RemoveAll(EntityUid uid)
    {
        if (TryComp<MagicCasterComponent>(uid, out var caster))
        {
            foreach (var spell in caster.MagicKnownSpells.ToList())
                ForgetSpell(uid, spell);
        }

        var ev = new MagicUnreadyAllEvent(uid);
        RaiseLocalEvent(ref ev);
        _adminLog.Add(LogType.Action, LogImpact.High, $"Magic: all spells removed from {ToPrettyString(uid):player}");
    }

    #endregion
}

/// <summary>Raised (broadcast) when someone's sealed schools change: their books re-read every page.</summary>
[ByRefEvent]
public record struct MagicSchoolsChangedEvent(EntityUid Owner);

/// <summary>Raised (broadcast) by Grant all spells: fill every grimoire <see cref="Owner"/> owns.</summary>
[ByRefEvent]
public record struct MagicFillOwnedBooksEvent(EntityUid Owner);

/// <summary>Raised (broadcast) to un-ready every page owned by <see cref="Owner"/>.</summary>
[ByRefEvent]
public record struct MagicUnreadyAllEvent(EntityUid Owner);
