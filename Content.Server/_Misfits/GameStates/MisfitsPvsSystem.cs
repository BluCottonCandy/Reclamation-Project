using Robust.Server.GameStates;
using Robust.Shared.Player;

namespace Content.Server._Misfits.GameStates;

/// <summary>
/// Convenience façade over <see cref="PvsOverrideSystem"/> for Misfits server systems.
/// Inject this instead of PvsOverrideSystem directly so that our code does not
/// couple tightly to engine internals and remains easy to update.
///
/// PVS (Potentially Visible Set) controls which entities each player receives state
/// updates for. Overriding it lets us force-send important entities (e.g. HUD actors,
/// tactical map icons) regardless of where the player is looking.
/// </summary>
public sealed class MisfitsPvsSystem : EntitySystem
{
    [Dependency] private readonly PvsOverrideSystem _pvsOverride = default!;

    // ── Global overrides (sent to every connected client) ──────────────────────

    /// <summary>
    /// Forces <paramref name="ent"/> and its hierarchy to be sent to all players,
    /// ignoring the normal PVS range. Still respects visibility masks.
    /// Use for globally-important entities like round-critical structures.
    /// </summary>
    public void AddGlobalOverride(EntityUid ent) =>
        _pvsOverride.AddGlobalOverride(ent);

    /// <summary>Removes a previously added global PVS override.</summary>
    public void RemoveGlobalOverride(EntityUid ent) =>
        _pvsOverride.RemoveGlobalOverride(ent);

    // ── Force-send (bypasses budget AND visibility masks) ──────────────────────

    /// <summary>
    /// Unconditionally sends <paramref name="ent"/> to every player, bypassing the
    /// PVS budget and visibility masks. Use sparingly — this ignores all culling.
    /// </summary>
    public void AddForceSend(EntityUid ent) =>
        _pvsOverride.AddForceSend(ent);

    /// <summary>Removes a global force-send.</summary>
    public void RemoveForceSend(EntityUid ent) =>
        _pvsOverride.RemoveForceSend(ent);

    // ── Session-scoped overrides ───────────────────────────────────────────────

    /// <summary>
    /// Forces <paramref name="ent"/> to be sent to <paramref name="session"/> regardless
    /// of PVS range. Useful for player-specific HUD entities.
    /// </summary>
    public void AddSessionOverride(EntityUid ent, ICommonSession session) =>
        _pvsOverride.AddSessionOverride(ent, session);

    /// <summary>Removes a session-scoped PVS override.</summary>
    public void RemoveSessionOverride(EntityUid ent, ICommonSession session) =>
        _pvsOverride.RemoveSessionOverride(ent, session);

    /// <summary>
    /// Force-sends <paramref name="ent"/> to <paramref name="session"/>, bypassing
    /// the session PVS budget and visibility masks.
    /// </summary>
    public void AddForceSend(EntityUid ent, ICommonSession session) =>
        _pvsOverride.AddForceSend(ent, session);

    /// <summary>Removes a session-scoped force-send.</summary>
    public void RemoveForceSend(EntityUid ent, ICommonSession session) =>
        _pvsOverride.RemoveForceSend(ent, session);
}
