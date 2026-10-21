using System.Linq;
using Content.Shared._NC.Sponsor; // Forge-Change
using Content.Shared.CCVar;
using Content.Shared.Players.PlayTimeTracking;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using JetBrains.Annotations;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared.Customization.Systems;


/// <summary>
///     Requires the selected job to be one of the specified jobs
/// </summary>
[UsedImplicitly]
[Serializable, NetSerializable]
public sealed partial class CharacterJobRequirement : CharacterRequirement
{
    [DataField(required: true)]
    public List<ProtoId<JobPrototype>> Jobs;

    public override bool IsValid(JobPrototype job,
        HumanoidCharacterProfile profile,
        Dictionary<string, TimeSpan> playTimes,
        bool whitelisted,
        IPrototype prototype,
        IEntityManager entityManager,
        IPrototypeManager prototypeManager,
        IConfigurationManager configManager,
        ISharedSponsorManager sponsorManager, // Forge-Change
        out string? reason,
        int depth = 0)
    {
        var jobs = new List<string>();
        var depts = prototypeManager.EnumeratePrototypes<DepartmentPrototype>();

        // Get the job names and department colors
        foreach (var j in Jobs)
        {
            var jobProto = prototypeManager.Index(j);
            var color = Color.LightBlue;

            foreach (var dept in depts.ToList().OrderBy(d => Loc.GetString($"department-{d.ID}")))
            {
                if (!dept.Roles.Contains(j))
                    continue;

                color = dept.Color;
                break;
            }

            jobs.Add($"[color={color.ToHex()}]{Loc.GetString(jobProto.Name)}[/color]");
        }

        // Join the job names
        var jobsString = Loc.GetString("character-job-requirement",
            ("inverted", Inverted), ("jobs", string.Join(", ", jobs)));

        reason = jobsString;
        return Jobs.Contains(job.ID);
    }
}

/// <summary>
///     Requires the selected job to be in one of the specified departments
/// </summary>
[UsedImplicitly]
[Serializable, NetSerializable]
public sealed partial class CharacterDepartmentRequirement : CharacterRequirement
{
    [DataField(required: true)]
    public List<ProtoId<DepartmentPrototype>> Departments;

    public override bool IsValid(JobPrototype job,
        HumanoidCharacterProfile profile,
        Dictionary<string, TimeSpan> playTimes,
        bool whitelisted,
        IPrototype prototype,
        IEntityManager entityManager,
        IPrototypeManager prototypeManager,
        IConfigurationManager configManager,
        ISharedSponsorManager sponsorManager, // Forge-Change
        out string? reason,
        int depth = 0)
    {
        var departments = new List<string>();

        // Get the department names and colors
        foreach (var d in Departments)
        {
            var deptProto = prototypeManager.Index(d);
            var color = deptProto.Color;

            departments.Add($"[color={color.ToHex()}]{Loc.GetString($"department-{deptProto.ID}")}[/color]");
        }

        // Join the department names
        var departmentsString = Loc.GetString("character-department-requirement",
            ("inverted", Inverted), ("departments", string.Join(", ", departments)));

        reason = departmentsString;
        return Departments.Any(d => prototypeManager.Index(d).Roles.Contains(job.ID));
    }
}

/// <summary>
///     Requires the playtime for a department to be within a certain range
/// </summary>
[UsedImplicitly]
[Serializable, NetSerializable]
public sealed partial class CharacterDepartmentTimeRequirement : CharacterRequirement
{
    [DataField]
    public TimeSpan Min = TimeSpan.MinValue;

    [DataField]
    public TimeSpan Max = TimeSpan.MaxValue;

    [DataField(required: true)]
    public ProtoId<DepartmentPrototype> Department;

    /// <summary>Additional departments whose job time counts toward this requirement.</summary>
    [DataField]
    public List<ProtoId<DepartmentPrototype>> AdditionalDepartments = new();

    public override bool CanBeBypassedByJobWhitelist(JobPrototype job) // #Misfits Change
    {
        return job.JobWhitelistBypassesRoleTimers;
    }

    public override bool IsValid(JobPrototype job,
        HumanoidCharacterProfile profile,
        Dictionary<string, TimeSpan> playTimes,
        bool whitelisted,
        IPrototype prototype,
        IEntityManager entityManager,
        IPrototypeManager prototypeManager,
        IConfigurationManager configManager,
        ISharedSponsorManager sponsorManager, // Forge-Change
        out string? reason,
        int depth = 0)
    {
        // Disable the requirement if the role timers are disabled
        if (!configManager.GetCVar(CCVars.GameRoleTimers))
        {
            reason = null;
            return !Inverted;
        }

        var department = prototypeManager.Index(Department);

        // Count each tracker once even when departments share jobs or equivalent roles.
        var roles = department.Roles.Concat(AdditionalDepartments
            .SelectMany(id => prototypeManager.Index(id).Roles));
        var trackers = roles.Select(id => prototypeManager.Index(id).PlayTimeTracker).Distinct();
        var playtime = TimeSpan.Zero;
        foreach (var tracker in trackers)
            playtime += playTimes.GetValueOrDefault(tracker);

        if (playtime > Max)
        {
            // Show the reason if invalid
            reason = Inverted
                ? null
                : Loc.GetString("character-timer-department-too-high",
                    ("time", playtime.TotalMinutes - Max.TotalMinutes),
                    ("department", Loc.GetString($"department-{department.ID}")),
                    ("departmentColor", department.Color));
            return false;
        }

        if (playtime < Min)
        {
            // Show the reason if invalid
            reason = Inverted
                ? null
                : Loc.GetString("character-timer-department-insufficient",
                    ("time", Min.TotalMinutes - playtime.TotalMinutes),
                    ("department", Loc.GetString($"department-{department.ID}")),
                    ("departmentColor", department.Color));
            return false;
        }

        reason = null;
        return true;
    }
}

/// <summary>
///     Requires the player to have a certain amount of overall job time
/// </summary>
[UsedImplicitly]
[Serializable, NetSerializable]
public sealed partial class CharacterOverallTimeRequirement : CharacterRequirement
{
    /// <summary>Enforce this requirement even when ordinary job timers are disabled.</summary>
    [DataField]
    public bool AlwaysRequired;

    [DataField]
    public TimeSpan Min = TimeSpan.MinValue;

    [DataField]
    public TimeSpan Max = TimeSpan.MaxValue;

    public override bool CanBeBypassedByJobWhitelist(JobPrototype job) // #Misfits Change
    {
        return !AlwaysRequired && job.JobWhitelistBypassesRoleTimers;
    }

    public override bool IsValid(JobPrototype job,
        HumanoidCharacterProfile profile,
        Dictionary<string, TimeSpan> playTimes,
        bool whitelisted,
        IPrototype prototype,
        IEntityManager entityManager,
        IPrototypeManager prototypeManager,
        IConfigurationManager configManager,
        ISharedSponsorManager sponsorManager, // Forge-Change
        out string? reason,
        int depth = 0)
    {
        // Disable the requirement if the role timers are disabled
        if (!AlwaysRequired && !configManager.GetCVar(CCVars.GameRoleTimers))
        {
            reason = null;
            return !Inverted;
        }

        // Get the overall time
        var overallTime = playTimes.GetValueOrDefault(PlayTimeTrackingShared.TrackerOverall);

        if (overallTime > Max)
        {
            // Show the reason if invalid
            reason = Inverted
                ? null
                : Loc.GetString("character-timer-overall-too-high",
                    ("time", overallTime.TotalMinutes - Max.TotalMinutes));
            return false;
        }

        if (overallTime < Min)
        {
            // Show the reason if invalid
            reason = Inverted
                ? null
                : Loc.GetString("character-timer-overall-insufficient",
                    ("time", Min.TotalMinutes - overallTime.TotalMinutes));
            return false;
        }

        reason = null;
        return true;
    }
}

/// <summary>
///     Requires the playtime for a tracker to be within a certain range
/// </summary>
[UsedImplicitly]
[Serializable, NetSerializable]
public sealed partial class CharacterPlaytimeRequirement : CharacterRequirement
{
    [DataField]
    public TimeSpan Min = TimeSpan.MinValue;

    [DataField]
    public TimeSpan Max = TimeSpan.MaxValue;

    [DataField(required: true)]
    public ProtoId<PlayTimeTrackerPrototype> Tracker;

    /// <summary>Other role trackers whose time contributes to this same requirement.</summary>
    [DataField]
    public List<ProtoId<PlayTimeTrackerPrototype>> AdditionalTrackers = new();

    public override bool CanBeBypassedByJobWhitelist(JobPrototype job) // #Misfits Change
    {
        return job.JobWhitelistBypassesRoleTimers;
    }

    public override bool IsValid(JobPrototype job,
        HumanoidCharacterProfile profile,
        Dictionary<string, TimeSpan> playTimes,
        bool whitelisted,
        IPrototype prototype,
        IEntityManager entityManager,
        IPrototypeManager prototypeManager,
        IConfigurationManager configManager,
        ISharedSponsorManager sponsorManager, // Forge-Change
        out string? reason,
        int depth = 0)
    {
        // Disable the requirement if the role timers are disabled
        if (!configManager.GetCVar(CCVars.GameRoleTimers))
        {
            reason = null;
            return !Inverted;
        }

        // Get SharedJobSystem
        if (!entityManager.EntitySysManager.TryGetEntitySystem(out SharedJobSystem? jobSystem))
        {
            DebugTools.Assert("CharacterRequirements: SharedJobSystem not found");
            reason = null;
            return false;
        }

        // Get the JobPrototype of the Tracker
        var trackerJob = jobSystem.GetJobPrototype(Tracker);
        var trackers = new HashSet<ProtoId<PlayTimeTrackerPrototype>>(AdditionalTrackers) { Tracker };
        var jobStr = string.Join(" / ", trackers.Select(t =>
            prototypeManager.Index<JobPrototype>(jobSystem.GetJobPrototype(t)).LocalizedName));

        // Retired jobs can retain tracked time and loadout requirements without a department.
        // Department membership only supplies a display color; it must not prevent timer checks.
        var departmentColor = Color.LightBlue;
        if (jobSystem.TryGetPrimaryDepartment(trackerJob, out var department) ||
            jobSystem.TryGetDepartment(trackerJob, out department))
        {
            departmentColor = department.Color;
        }

        // Count each explicitly selected role once, even if it appears more than once.
        var time = TimeSpan.Zero;
        foreach (var tracker in trackers)
            time += playTimes.GetValueOrDefault(tracker);
        reason = null;

        if (time > Max)
        {
            // Show the reason if invalid
            reason = Inverted
                ? null
                : Loc.GetString("character-timer-role-too-high",
                    ("time", time.TotalMinutes - Max.TotalMinutes),
                    ("job", jobStr),
                    ("departmentColor", departmentColor));
            return false;
        }

        if (time < Min)
        {
            // Show the reason if invalid
            reason = Inverted
                ? null
                : Loc.GetString("character-timer-role-insufficient",
                    ("time", Min.TotalMinutes - time.TotalMinutes),
                    ("job", jobStr),
                    ("departmentColor", departmentColor));
            return false;
        }

        return true;
    }
}
