using System.Linq;
using Content.Shared._ReclamationProject.Forged;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Preferences;
using NUnit.Framework;

namespace Content.Tests._ReclamationProject;

[TestFixture]
public sealed class ForgedBrandingProfileTests
{
    [Test]
    public void BrandingIsPerCharacterAndIdempotent()
    {
        var original = new HumanoidCharacterProfile();
        var branded = ForgedBrandingProfiles.WithBrand(original, ForgedBrandingProfiles.Forged);
        branded = ForgedBrandingProfiles.WithBrand(branded, ForgedBrandingProfiles.Forged);
        Assert.That(original.Appearance.Markings, Is.Empty);
        Assert.That(branded.Appearance.Markings.Count, Is.EqualTo(1));
        Assert.That(branded.Appearance.Markings.Single().MarkingId, Is.EqualTo(ForgedBrandingProfiles.Forged));
    }

    [Test]
    public void EditorCannotRemoveHideRecolorOrInventBrands()
    {
        var empty = new HumanoidCharacterProfile();
        var earned = ForgedBrandingProfiles.WithBrand(empty, ForgedBrandingProfiles.Forged);
        var forged = ForgedBrandingProfiles.WithBrand(empty, ForgedBrandingProfiles.Captive);
        var result = ForgedBrandingProfiles.PreserveEarned(forged, earned);
        Assert.That(result.Appearance.Markings.Select(m => m.MarkingId), Is.EqualTo(new[] { ForgedBrandingProfiles.Forged }));
        var mark = result.Appearance.Markings.Single();
        Assert.Multiple(() =>
        {
            Assert.That(mark.Visible, Is.True);
            Assert.That(mark.Forced, Is.True);
            Assert.That(mark.MarkingColors.Single(), Is.EqualTo(ForgedBrandingProfiles.BrandColor));
            Assert.That(ForgedBrandingProfiles.PreserveEarned(forged, null).Appearance.Markings, Is.Empty);
        });
    }

    [Test]
    public void DatabaseRoundTripRestoresPermanentFlags()
    {
        var branded = ForgedBrandingProfiles.WithBrand(new HumanoidCharacterProfile(), ForgedBrandingProfiles.Captive);
        var databaseString = branded.Appearance.Markings.Single().ToString();
        var loaded = Marking.ParseFromDbString(databaseString)!;
        var prepared = ForgedBrandingProfiles.Prepare(new[] { loaded }).Single();
        Assert.That(prepared.MarkingId, Is.EqualTo(ForgedBrandingProfiles.Captive));
        Assert.That(prepared.Forced, Is.True);
        Assert.That(prepared.Visible, Is.True);
    }

    [Test]
    public void UnbranderRemovesBothBrandsAndPreservesOtherMarkings()
    {
        var profile = new HumanoidCharacterProfile();
        profile.Appearance.Markings.Add(new Marking("OtherMarking", 1));
        profile = ForgedBrandingProfiles.WithBrand(profile, ForgedBrandingProfiles.Forged);
        profile = ForgedBrandingProfiles.WithBrand(profile, ForgedBrandingProfiles.Captive);
        var result = ForgedBrandingProfiles.WithoutBrands(profile);
        Assert.That(result.Appearance.Markings.Select(m => m.MarkingId), Is.EqualTo(new[] { "OtherMarking" }));
        Assert.That(profile.Appearance.Markings.Count, Is.EqualTo(3));
    }
    [Test]
    public void CuttingCaptiveBrandPreservesAnvilAndOtherMarkings()
    {
        var profile = new HumanoidCharacterProfile();
        profile.Appearance.Markings.Add(new Marking("OtherMarking", 1));
        profile = ForgedBrandingProfiles.WithBrand(profile, ForgedBrandingProfiles.Forged);
        profile = ForgedBrandingProfiles.WithBrand(profile, ForgedBrandingProfiles.Captive);
        var result = ForgedBrandingProfiles.WithoutCaptiveBrand(profile);
        Assert.That(result.Appearance.Markings.Select(m => m.MarkingId),
            Is.EqualTo(new[] { "OtherMarking", ForgedBrandingProfiles.Forged }));
        Assert.That(profile.Appearance.Markings.Count, Is.EqualTo(3));
    }
    [Test]
    public void ChestOverlayCannotBeInventedByEditorAndLoadsAsCanonicalCaptiveBrand()
    {
        var profile = new HumanoidCharacterProfile();
        profile.Appearance.Markings.Add(new Marking(ForgedBrandingProfiles.CaptiveChest, 1));
        Assert.That(ForgedBrandingProfiles.PreserveEarned(profile, null).Appearance.Markings, Is.Empty);
        Assert.That(ForgedBrandingProfiles.Prepare(profile.Appearance.Markings).Single().MarkingId,
            Is.EqualTo(ForgedBrandingProfiles.Captive));
        Assert.That(ForgedBrandingProfiles.WithoutCaptiveBrand(profile).Appearance.Markings, Is.Empty);
    }
}
