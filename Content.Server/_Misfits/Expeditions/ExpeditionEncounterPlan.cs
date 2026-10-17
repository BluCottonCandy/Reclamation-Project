using System.Linq;
using Content.Shared._Misfits.Expeditions;

namespace Content.Server._Misfits.Expeditions;

/// <summary>One named leader per population, with capped rewards rolled by difficulty.</summary>
public sealed record ExpeditionEncounterPlan(string Family, string[] Enemies, string? AdvancedWeapon, string? AdvancedArmor)
{
    public static readonly string[] NamedEnemies =
    {
        "ReclamationExpeditionMutantLeader", "ReclamationExpeditionRaiderLeader", "ReclamationExpeditionGlowingLeader"
    };

    public static readonly string[] AdvancedWeapons =
    {
        "N14WeaponRifle762Marksman", "N14WeaponSMG12mmAdvanced", "N14WeaponSMGGreaseGun",
        "N14WeaponLaserRifle", "N14WeaponLaserRifleAuto", "N14WeaponLauncherMissile",
        "N14WeaponMinigun", "N14WeaponLaserGatling", "N14WeaponRifleGrenade"
    };

    public static ExpeditionEncounterPlan Create(N14ExpeditionCombatTier tier, int seed, N14ExpeditionEnemyFamily? familyOverride = null)
    {
        var random = new Random(seed ^ 0x7717);
        if (familyOverride == N14ExpeditionEnemyFamily.Ghouls && tier != N14ExpeditionCombatTier.Easy)
            throw new ArgumentException("Ghouls are restricted to Easy expeditions.", nameof(familyOverride));
        var family = familyOverride.HasValue ? (int) familyOverride.Value : random.Next(tier == N14ExpeditionCombatTier.Easy ? 3 : 2);
        var count = tier switch
        {
            N14ExpeditionCombatTier.Medium => 30,
            N14ExpeditionCombatTier.Hard => 40,
            _ => 20
        };
        string[] pool;
        var enemies = new List<string> { NamedEnemies[family] };
        if (family == 0)
        {
            pool = tier == N14ExpeditionCombatTier.Easy
                ? new[] { "ReclamationExpeditionSuperMutantNPC", "ReclamationExpeditionSuperMutantMelee", "ReclamationExpeditionSuperMutantRanged", "ReclamationExpeditionSuperMutantArmored" }
                : new[] { "ReclamationExpeditionSuperMutantRanged" };
            if (tier == N14ExpeditionCombatTier.Hard)
                enemies.AddRange(new[] { "ReclamationExpeditionNightkinVeteran", "ReclamationExpeditionNightkinElite" });
        }
        else if (family == 1)
        {
            pool = tier switch
            {
                N14ExpeditionCombatTier.Medium => new[] { "N14MobRaiderHunter", "N14MobRaiderSkrimisher" },
                N14ExpeditionCombatTier.Hard => new[] { "N14MobRaiderHunter" },
                _ => new[] { "N14MobRaiderPsycho", "N14MobRaiderPsychoRanged", "N14MobRaiderHunter", "N14MobRaiderSkrimisher" }
            };
            if (tier == N14ExpeditionCombatTier.Hard)
                enemies.AddRange(Enumerable.Repeat("N14MobRaiderBoss", random.Next(3, 6) - 1));
        }
        else
            pool = new[] { "N14MobGhoulFeral", "N14MobGhoulFeralReaver" };

        // Balanced variants with shuffled order; elites count toward the total.
        pool = pool.OrderBy(_ => random.Next()).ToArray();
        for (var i = 0; enemies.Count < count; i++)
            enemies.Add(pool[i % pool.Length]);
        var population = enemies.OrderBy(_ => random.Next()).ToArray();

        // Separate stream: changing population composition cannot change loot odds.
        var loot = new Random(seed ^ 0x191C5);
        string? weapon = null;
        string? armor = null;
        if (tier != N14ExpeditionCombatTier.Easy)
        {
            var weaponRoll = tier == N14ExpeditionCombatTier.Hard || loot.NextDouble() < 0.05;
            var armorRoll = tier == N14ExpeditionCombatTier.Hard || loot.NextDouble() < 0.05;
            if (weaponRoll)
                weapon = AdvancedWeapons[loot.Next(AdvancedWeapons.Length)];
            if (armorRoll)
                armor = "N14ClothingOuterCombatArmorMK2";
        }
        return new ExpeditionEncounterPlan(new[] { "Super mutants", "Raiders", "Ghouls" }[family], population, weapon, armor);
    }
}
