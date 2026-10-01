
namespace Content.Shared._NC.Sponsor;

public sealed class SponsorData
{
    public static readonly Dictionary<string, SponsorLevel> RolesMap = new()
    {
        { "1228412355705307148", SponsorLevel.Level1 }, // Booster
        { "1388838190009290932", SponsorLevel.Level1 }, // Forge Apprentice
        { "1388839804375924736", SponsorLevel.Level2 }, // Weaponsmith
        { "1388839967475634176", SponsorLevel.Level3 }, // Master of the Forge
        { "1388840103966933003", SponsorLevel.Level4 }, // Grand Smith
        { "1388840314860736512", SponsorLevel.Level5 }, // Forge Architect
        { "1388840456921550942", SponsorLevel.Level6 }, // Forge Demiurge
        { "1228303275833425992", SponsorLevel.Level6 }, // Project Lead
        { "1381007703425679522", SponsorLevel.Level6 }, // Hand's Assistant
        { "1228659342668988416", SponsorLevel.Level4 }, // Senior Moderator
        { "1351127483432570910", SponsorLevel.Level4 }, // GGM
        { "1227934528442728498", SponsorLevel.Level4 }, // Chief of Security
        { "1229422799362195577", SponsorLevel.Level4 }, // Senior Mentor
        { "1257628115988119562", SponsorLevel.Level3 }, // Server Overseer
        { "1226554881398280272", SponsorLevel.Level2 } // Moderator
    };

    public static readonly Dictionary<SponsorLevel, string> SponsorColor = new()
    {
        { SponsorLevel.Level1, "#6bb9f0" },
        { SponsorLevel.Level2, "#8a9eff" },
        { SponsorLevel.Level3, "#6b8e23" },
        { SponsorLevel.Level4, "#bdbe6b" },
        { SponsorLevel.Level5, "#ff9e2c" },
        { SponsorLevel.Level6, "#ffd700" }
    };

    public static readonly Dictionary<SponsorLevel, string> SponsorGhost = new()
    {
        { SponsorLevel.Level3, "SponsorGhost1" },
        { SponsorLevel.Level4, "SponsorGhost2" },
        { SponsorLevel.Level5, "SponsorGhost3" },
        { SponsorLevel.Level6, "SponsorGhost3" }
    };

    public static SponsorLevel ParseRoles(List<string> roles)
    {
        var highestRole = SponsorLevel.None;
        foreach (var role in roles)
        {
            if (RolesMap.ContainsKey(role))
                if ((byte) RolesMap[role] > (byte) highestRole)
                    highestRole = RolesMap[role];
        }

        return highestRole;
    }
}

public enum SponsorLevel : byte
{
    None = 0,
    Level1 = 1,
    Level2 = 2,
    Level3 = 3,
    Level4 = 4,
    Level5 = 5,
    Level6 = 6
}
