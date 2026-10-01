// #Misfits Change

using Content.Server._Misfits.Ghoul;
using Content.Server.Chat.Managers;
using Content.Server.Chat.Systems;
using Content.Server.Humanoid;
using Content.Shared._N14.Radiation.Components;
using Content.Shared.Chat;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Humanoid;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Random;


namespace Content.Server._Misfits.GhoulReversal;

/// <summary>
/// When a humanoid with GhoulifyOnRadiationDeathComponent dies to radiation damage,
/// they transform into the Ghoul player species and are revived at low health,
/// instead of fully dying. The Promethine chemistry reagent can reverse this
/// within the first 12 real hours.
/// </summary>
public sealed class GhoulifyOnRadiationDeathSystem : EntitySystem
{
    [Dependency] private readonly HumanoidAppearanceSystem _humanoid = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly MobThresholdSystem _mobThreshold = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IChatManager _chatManager = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    
    private const int FallbackCritThreshold = 100;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GhoulifyOnRadiationDeathComponent, MobStateChangedEvent>(OnMobStateDeath);
    }

    private void OnMobStateDeath(EntityUid uid, GhoulifyOnRadiationDeathComponent component, MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead)
            return;

        // Must be a humanoid player character
        if (!TryComp<HumanoidAppearanceComponent>(uid, out var appearance))
            return;

        // Don't re-ghoulify someone already a ghoul or super mutant
        if (appearance.Species == "Ghoul" || appearance.Species == "GhoulGlowing" || appearance.Species == "SuperMutant")
            return;

        // Check that a meaningful amount of radiation damage was accumulated
        if (!TryComp<DamageableComponent>(uid, out var damageable))
            return;

        var radDamage = 0f;
        if (damageable.Damage.DamageDict.TryGetValue("Radiation", out var rad))
            radDamage = rad.Float();

        if (radDamage < component.MinimumRadiationDamage)
            return;

        // --- Transform into Ghoul player species ---
        _humanoid.SetSpecies(uid, component.GhoulSpecies);
        damageable.DamageModifierSets.Add("Ghoul");
        
        // if no phoenix or dermal armor, 20% chance for gamma shield 
        if (!damageable.DamageModifierSets.Contains("N14PhoenixArmor") 
            && !damageable.DamageModifierSets.Contains("N14DermalArmor") 
            && _random.Prob(component.GammaShieldChance))
        {
            damageable.DamageModifierSets.Add("N14GammaShield");
        }
        EnsureComp<RadiationHealingComponent>(uid);

        // Revive just outside of crit instead of full heal.
        // Heal all damage first to leave Dead state, then re-apply enough damage
        // to land just outside of crit.
        if (TryComp<MobThresholdsComponent>(uid, out var thresholds))
        {
            _mobThreshold.SetAllowRevives(uid, true, thresholds);
            _damageable.SetAllDamage(uid, damageable, FixedPoint2.Zero);

            // Read the crit threshold; fall back to 100 if not defined
            if (!_mobThreshold.TryGetThresholdForState(uid, MobState.Critical, out var critThreshold, thresholds))
                critThreshold = FixedPoint2.New(FallbackCritThreshold);

            // Apply airloss damage equal to crit threshold minus a little breathing room
            // Changed from blunt to prevent Bloody Mess characters from getting all their limbs gibbed
            var critDamage = new DamageSpecifier();
            critDamage.DamageDict["Asphyxiation"] = critThreshold.Value - component.Recovery;
            _damageable.TryChangeDamage(uid, critDamage, ignoreResistances: true);

            _mobThreshold.SetAllowRevives(uid, false, thresholds);
        }
        else
        {
            // No thresholds component — heal and apply a default crit amount
            _damageable.SetAllDamage(uid, damageable, FixedPoint2.Zero);
            var critDamage = new DamageSpecifier();
            critDamage.DamageDict["Asphyxiation"] = FixedPoint2.New(FallbackCritThreshold);
            _damageable.TryChangeDamage(uid, critDamage, ignoreResistances: true);
        }

        // Stamp the time component so Promethine chemistry can gatekeep reversal
        EnsureComp<GhoulificationTimeComponent>(uid);

        // 10% chance the victim is already going feral
        if (_random.Prob(component.FeralChance))
        {
            EnsureComp<FeralGhoulifyOverTimeComponent>(uid); 
        }

        // Private message to the transforming player only.
        if (_playerManager.TryGetSessionByEntity(uid, out var session)
            && session.Status == SessionStatus.InGame)
        {
            var selfMsg = Loc.GetString("ghoulify-on-death-self");
            _chatManager.ChatMessageToOne(ChatChannel.Local, selfMsg, selfMsg,
                EntityUid.Invalid, false, session.Channel);
        }

        // Emote broadcast to nearby bystanders — emote system prefixes the entity name.
        _chat.TrySendInGameICMessage(uid,
            Loc.GetString("ghoulify-on-death-others"),
            InGameICChatType.Emote, ChatTransmitRange.Normal, ignoreActionBlocker: true);
    }
}
