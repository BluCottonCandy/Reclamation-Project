// #Misfits Change - Server system for robot self-repair announcer popups.
using Content.Server.Chat.Systems;
using Content.Shared._Misfits.Silicon;
using Content.Shared.Chat;
using Content.Shared.Damage;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Mobs.Components;
using Robust.Shared.Timing;

namespace Content.Server._Misfits.Silicon;

/// <summary>
/// Shows popup messages when a robot's hull integrity is poor, and when passive self-repair begins.
/// </summary>
public sealed class RobotSelfRepairAnnouncerSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MobThresholdSystem _mobThreshold = default!;
    [Dependency] private readonly ChatSystem _chat = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RobotSelfRepairAnnouncerComponent, DamageChangedEvent>(OnDamageChanged);
    }

    private void OnDamageChanged(EntityUid uid, RobotSelfRepairAnnouncerComponent component, DamageChangedEvent args)
    {
        var curTime = _timing.CurTime;

        if (args.DamageIncreased && args.DamageDelta != null)
        {
            // Only warn when a meaningful hit lands and total damage is at a concerning level
            var delta = args.DamageDelta.GetTotal();
            if (delta < (float) component.DamageDeltaThreshold)
                return;

            if (args.Damageable.TotalDamage < (float) component.MinTotalDamageForHullWarning)
                return;

            if (component.NextHullWarningTime != null && curTime < component.NextHullWarningTime)
                return;

            component.NextHullWarningTime = curTime + component.HullWarningCooldown;
            // Emote broadcast — nearby players hear the hull warning aloud from the robot.
            _chat.TrySendInGameICMessage(uid,
                Loc.GetString("robot-self-repair-hull-warning"),
                InGameICChatType.Emote, ChatTransmitRange.Normal, ignoreActionBlocker: true);
        }
        else if (!args.DamageIncreased)
        {
            // Self-repair announcement: fire when healing while meaningfully damaged
            if (args.Damageable.TotalDamage < (float) component.MinDamageForRepairPopup)
                return;

            if (component.NextRepairAnnounceTime != null && curTime < component.NextRepairAnnounceTime)
                return;

            component.NextRepairAnnounceTime = curTime + component.RepairAnnounceCooldown;
            // Emote broadcast — nearby players hear the self-repair announcement from the robot.
            _chat.TrySendInGameICMessage(uid,
                Loc.GetString("robot-self-repair-initiating"),
                InGameICChatType.Emote, ChatTransmitRange.Normal, ignoreActionBlocker: true);
        }
    }
}
