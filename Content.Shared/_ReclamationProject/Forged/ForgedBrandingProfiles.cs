using System.Linq;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Preferences;

namespace Content.Shared._ReclamationProject.Forged;

/// <summary>Permanent brands use the existing per-character marking database, not account-wide state.</summary>
public static class ForgedBrandingProfiles
{
    public const string Forged = "ForgedAnvilBrand";
    public const string Captive = "ForgedCaptiveBrand";
    public const string CaptiveChest = "ForgedCaptiveChestBrand";
    public static readonly Color BrandColor = Color.FromHex("#754A40");

    public static bool IsBrand(string id) => id is Forged or Captive or CaptiveChest;

    public static Marking Brand(string id) => new(id == CaptiveChest ? Captive : id, new List<Color> { BrandColor }) { Forced = true, Visible = true };

    public static List<Marking> Prepare(IEnumerable<Marking> markings) => markings
        .Select(m => IsBrand(m.MarkingId) ? Brand(m.MarkingId) : new Marking(m)).ToList();

    // Client profile edits may neither invent, remove, hide, nor recolor earned brands.
    public static HumanoidCharacterProfile PreserveEarned(HumanoidCharacterProfile incoming, HumanoidCharacterProfile? existing)
    {
        var marks = incoming.Appearance.Markings.Where(m => !IsBrand(m.MarkingId)).Select(m => new Marking(m)).ToList();
        if (existing != null)
            marks.AddRange(existing.Appearance.Markings.Where(m => IsBrand(m.MarkingId))
                .Select(m => m.MarkingId).Distinct().Select(Brand));
        return incoming.WithCharacterAppearance(incoming.Appearance.WithMarkings(marks));
    }

    public static HumanoidCharacterProfile WithBrand(HumanoidCharacterProfile profile, string id)
    {
        var marks = Prepare(profile.Appearance.Markings);
        if (!marks.Any(m => m.MarkingId == id))
            marks.Add(Brand(id));
        return profile.WithCharacterAppearance(profile.Appearance.WithMarkings(marks));
    }

    public static HumanoidCharacterProfile WithoutCaptiveBrand(HumanoidCharacterProfile profile) =>
        profile.WithCharacterAppearance(profile.Appearance.WithMarkings(profile.Appearance.Markings
            .Where(m => m.MarkingId != Captive && m.MarkingId != CaptiveChest).Select(m => new Marking(m)).ToList()));

    public static HumanoidCharacterProfile WithoutBrands(HumanoidCharacterProfile profile) =>
        profile.WithCharacterAppearance(profile.Appearance.WithMarkings(profile.Appearance.Markings
            .Where(m => !IsBrand(m.MarkingId)).Select(m => new Marking(m)).ToList()));
}
