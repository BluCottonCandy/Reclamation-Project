using System.Linq;
using Content.Shared._Misfits.Expeditions;

namespace Content.Server._Misfits.Expeditions;

/// <summary>Named leaders on Hard only, with capped rewards rolled by difficulty.</summary>
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
        if (familyOverride == N14ExpeditionEnemyFamily.SuperMutants && tier == N14ExpeditionCombatTier.Easy)
            throw new ArgumentException("Super mutants cannot appear in Easy expeditions.", nameof(familyOverride));
        var allowedFamilies = tier == N14ExpeditionCombatTier.Easy
            ? new[] { N14ExpeditionEnemyFamily.Raiders, N14ExpeditionEnemyFamily.Ghouls }
            : new[] { N14ExpeditionEnemyFamily.SuperMutants, N14ExpeditionEnemyFamily.Raiders };
        var family = (int) (familyOverride ?? allowedFamilies[random.Next(allowedFamilies.Length)]);
        var count = tier switch
        {
            N14ExpeditionCombatTier.Medium => 30,
            N14ExpeditionCombatTier.Hard => 40,
            _ => 20
        };
        var enemies = new List<string>();
        void Fill(int amount, params string[] variants)
        {
            var shuffled = variants.OrderBy(_ => random.Next()).ToArray();
            for (var i = 0; i < amount; i++)
                enemies.Add(shuffled[i % shuffled.Length]);
        }

        if (tier == N14ExpeditionCombatTier.Hard)
        {
            enemies.Add(NamedEnemies[family]);
            if (family == (int) N14ExpeditionEnemyFamily.SuperMutants)
            {
                enemies.AddRange(new[] { "ReclamationExpeditionNightkinVeteran", "ReclamationExpeditionNightkinElite" });
                Fill(count - enemies.Count, "ReclamationExpeditionSuperMutantMarksman");
            }
            else
            {
                // The named leader counts toward the existing three-to-five boss quota.
                Fill(random.Next(3, 6) - 1, "N14MobRaiderBoss");
                Fill(count - enemies.Count, "N14MobRaiderHunter");
            }
        }
        else if (family == (int) N14ExpeditionEnemyFamily.Ghouls)
        {
            Fill(count, "N14MobGhoulFeral", "N14MobGhoulFeralReaver");
        }
        else
        {
            var meleeCount = tier == N14ExpeditionCombatTier.Easy ? 14 : 15;
            var marksmen = random.Next(1, 3);
            if (family == (int) N14ExpeditionEnemyFamily.SuperMutants)
            {
                Fill(meleeCount, "ReclamationExpeditionSuperMutantNPC", "ReclamationExpeditionSuperMutantMelee", "ReclamationExpeditionSuperMutantArmored");
                Fill(marksmen, "ReclamationExpeditionSuperMutantMarksman");
                Fill(count - enemies.Count, "ReclamationExpeditionSuperMutantRanged", "ReclamationExpeditionSuperMutantRifleman", "ReclamationExpeditionSuperMutantShotgunner");
            }
            else
            {
                Fill(meleeCount, "N14MobRaiderPsycho", "ReclamationExpeditionRaiderBruiser", "ReclamationExpeditionRaiderBaton");
                Fill(marksmen, "N14MobRaiderHunter");
                if (tier == N14ExpeditionCombatTier.Easy)
                    Fill(count - enemies.Count, "N14MobRaiderPsychoRanged", "N14MobRaiderSkrimisher");
                else
                    Fill(count - enemies.Count, "N14MobRaiderSkrimisher", "ReclamationExpeditionRaiderRifleman", "ReclamationExpeditionRaiderSmg", "ReclamationExpeditionRaiderShotgunner");
            }
        }
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
