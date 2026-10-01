// #Misfits Add - Persistent player SPECIAL stats, kill/death/round statistics, and character history log.
using Content.Shared._Misfits.Special;
using Robust.Shared.GameStates;

namespace Content.Shared._Misfits.PlayerData.Components;

/// <summary>
/// Carries a player's persistent data that survives across rounds:
/// mirrored SPECIAL base stats, lifetime kill/death/round counters, and a character history log.
/// Data is keyed by UserId + CharacterName in the database.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class PersistentPlayerDataComponent : Component
{
    // ── SPECIAL base stats ────────────────────────────────────────────────────

    /// <summary>Persistent base Strength (1–10). Starts at 1; player allocates 10 points on first spawn.</summary>
    [DataField, AutoNetworkedField]
    public int Strength = SpecialProfile.DefaultValue;

    [DataField, AutoNetworkedField]
    public int Perception = SpecialProfile.DefaultValue;

    [DataField, AutoNetworkedField]
    public int Endurance = SpecialProfile.DefaultValue;

    [DataField, AutoNetworkedField]
    public int Charisma = SpecialProfile.DefaultValue;

    [DataField, AutoNetworkedField]
    public int Agility = SpecialProfile.DefaultValue;

    [DataField, AutoNetworkedField]
    public int Intelligence = SpecialProfile.DefaultValue;

    [DataField, AutoNetworkedField]
    public int Luck = SpecialProfile.DefaultValue;

    // ── Lifetime statistics ───────────────────────────────────────────────────

    /// <summary>Total non-player mob kills across all rounds.</summary>
    [DataField, AutoNetworkedField]
    public int MobKills;

    /// <summary>Total times this character has died.</summary>
    [DataField, AutoNetworkedField]
    public int Deaths;

    /// <summary>Total rounds this character has participated in.</summary>
    [DataField, AutoNetworkedField]
    public int RoundsPlayed;

    // ── Character history log ─────────────────────────────────────────────────

    /// <summary>Append-only log of notable in-game events (capped at 50 entries).</summary>
    [DataField, AutoNetworkedField]
    public List<string> HistoryLog = new();

    // ── Internal persistence bookkeeping ──────────────────────────────────────

    /// <summary>Whether the player has confirmed their S.P.E.C.I.A.L. allocation. Auto-set true on load from existing save.</summary>
    [DataField, AutoNetworkedField]
    public bool StatsConfirmed;

    /// <summary>Whether data has been loaded from disk this round.</summary>
    [DataField]
    public bool Loaded;

    /// <summary>Character name used as part of the persistence key.</summary>
    [DataField]
    public string CharacterName = string.Empty;

    /// <summary>Player user-id string used as part of the persistence key.</summary>
    [DataField]
    public string UserId = string.Empty;

    /// <summary>Prevents double-counting the death stat within a single round.</summary>
    [DataField]
    public bool DiedThisRound;

    /// <summary>Prevents double-incrementing RoundsPlayed within a single spawn.</summary>
    [DataField]
    public bool RoundCountedThisRound;
}
