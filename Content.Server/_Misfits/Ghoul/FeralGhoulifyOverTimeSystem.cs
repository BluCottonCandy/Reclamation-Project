using Content.Server.Chat.Systems;
using Content.Server.GameTicking;
using Content.Server.Polymorph.Systems;
using Content.Shared.Chat;
using Content.Shared.Examine;
using Content.Shared.IdentityManagement;
using Content.Shared.Jittering;
using Content.Shared.Mobs.Systems;
using Content.Shared.Players;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;


namespace Content.Server._Misfits.Ghoul;


/// <summary>
/// Largely a rebuild of <see cref="Content.Server.Ghoul.GhoulifySystem"/>'s
/// <see cref="Content.Server.Ghoul.FeralGhoulifyComponent"/> features. This one moves
/// away from radiation as a catalyst in favor of building threat over time, managed with
/// meds (Promethine > RadAway > Diluted RadAway > Rad-X >= Bitterdrink).
///
/// See also: <see cref="Content.Server._Misfits.EntityEffects.Effects.ModifyFeralization"/>
/// </summary>
public sealed class FeralGhoulifyOverTimeSystem : EntitySystem
{
    [Dependency] private readonly GameTicker _ticker = default!;
    [Dependency] private readonly PolymorphSystem _polymorph = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedAudioSystem _audioSystem = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!;
    [Dependency] private readonly SharedJitteringSystem _jitter = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    
    private static readonly TimeSpan AccumulationInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan WarningInterval = TimeSpan.FromSeconds(120);
    private TimeSpan _nextAccumulationSync;
    
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FeralGhoulifyOverTimeComponent, ExaminedEvent>(OnFeralExamined);
    }
    
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // once per second we run the loop
        if (_timing.CurTime < _nextAccumulationSync)
            return;
        
        _nextAccumulationSync = _timing.CurTime + AccumulationInterval;

        // loop over all feralizing ghouls
        var feralQuery = EntityQueryEnumerator<FeralGhoulifyOverTimeComponent>();
        while (feralQuery.MoveNext(out var uid, out var comp))
        {
            // dead ghouls are ignored
            if (_mobState.IsDead(uid))
                continue;
            
            comp.CurrentFeral += comp.FeralPerSecond;

            // once every 2 minutes we give some warning
            if (_timing.CurTime < comp.NextWarning)
            {
                continue;
            }
            
            if (comp.CurrentFeral >= comp.FeralThreshold)
            {
                HandleFeralThreshold(comp, uid);
            } 
            else if (comp.CurrentFeral >= comp.DangerThreshold)
            {
                DoDangerWarning(uid, comp);
            }
            else if (comp.CurrentFeral >= comp.WarningThreshold)
            {
                _popup.PopupEntity(Loc.GetString("misfits-ghoul-feral-warning"), uid, uid);
            }
            
            comp.NextWarning = _timing.CurTime + WarningInterval;
        }
    }

    private void HandleFeralThreshold(FeralGhoulifyOverTimeComponent comp, EntityUid uid)
    {
        // random chance to become a feral
        if (_random.Prob(comp.FeralChance))
        {
            Feralize(uid);
            return;
        }
        // chance increases by 10%
        comp.FeralChance += 0.1f;
        // make sure the player knows they're in serious danger of turning at any time
        _popup.PopupEntity(Loc.GetString("misfits-ghoul-feral-critical"), uid, uid);
        _stun.TryKnockdown(uid, AccumulationInterval, false);
        _jitter.DoJitter(uid, WarningInterval, false);
        _chat.TrySendInGameICMessage(uid,
            Loc.GetString("misfits-ghoul-feral-critical-others"),
            InGameICChatType.Emote, ChatTransmitRange.Normal, ignoreActionBlocker: true);
        _audioSystem.PlayPvs(comp.DangerSounds, uid);
    }

    /// <summary>
    /// Randomly choose between several disturbing warnings of high ferality.
    /// </summary>
    /// <param name="uid"></param>
    /// <param name="comp"></param>
    private void DoDangerWarning(EntityUid uid, FeralGhoulifyOverTimeComponent comp)
    {
        // could have just replaced the number in each of these, but I liked each of these having different effects
        
        // if adding more danger warning options, make sure to increment this
        switch (_random.Next(1, 3))
        {
            case 1:
                _popup.PopupEntity(Loc.GetString("misfits-ghoul-feral-danger1"), uid, uid);
                _jitter.DoJitter(uid, AccumulationInterval, false);
                _chat.TrySendInGameICMessage(uid,
                    Loc.GetString("misfits-ghoul-feral-danger1-others"),
                    InGameICChatType.Emote, ChatTransmitRange.Normal, ignoreActionBlocker: true);
                break;
            case 2:
                _popup.PopupEntity(Loc.GetString("misfits-ghoul-feral-danger2"), uid, uid);
                _jitter.DoJitter(uid, AccumulationInterval, false);
                _chat.TrySendInGameICMessage(uid,
                    Loc.GetString("misfits-ghoul-feral-danger2-others"),
                    InGameICChatType.Emote, ChatTransmitRange.Normal, ignoreActionBlocker: true);
                _audioSystem.PlayPvs(comp.DangerSounds, uid);
                break;
            case 3:
                _popup.PopupEntity(Loc.GetString("misfits-ghoul-feral-danger3"), uid, uid);
                _jitter.DoJitter(uid, AccumulationInterval, false);
                break;
        }
    }
    
    /// <summary>
    /// Adds a warning about the subject's feralization when they're being examined.
    /// </summary>
    /// <param name="uid"></param>
    /// <param name="comp"></param>
    /// <param name="args"></param>
    private void OnFeralExamined(EntityUid uid, FeralGhoulifyOverTimeComponent comp, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        if (comp.CurrentFeral >= comp.ExamineThreshold)
        {
            args.PushMarkup(Loc.GetString("misfits-ghoul-feral-examine", ("target", Identity.Entity(args.Examined, EntityManager))));
        }
    }

    /// <summary>
    /// Removes the player from the character and turns the character into a feral ghoul NPC,
    /// deleting all carried equipment
    /// </summary>
    /// <param name="uid"></param>
    private void Feralize(EntityUid uid)
    {
        _popup.PopupEntity(Loc.GetString("ghoul-feral-complete"), uid, uid);
        
        // Force the player out of the body and turn it into an AI Feral Ghoul
        if (TryComp<ActorComponent>(uid, out var actor) && actor.PlayerSession.GetMind() is { } mind)
        {
            _ticker.OnGhostAttempt(mind, false);
        }
        var ent = _polymorph.PolymorphEntity(uid, "GhoulFeralPolymorph");
        if (ent != null)
        {
            RemCompDeferred<FeralGhoulifyOverTimeComponent>(uid);
        }
    }
}
