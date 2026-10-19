using System.Linq;
using Content.Shared._ReclamationProject.GunAttachments;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Verbs;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture]
public sealed class GunAttachmentRemovalTests
{
    [TestCase("N14WeaponSMG45")]
    [TestCase("N14WeaponSMG9mm")]
    [TestCase("N14WeaponRifle556R91")]
    [TestCase("N14WeaponPistol10mm")]
    public async Task AltClickRemovesMagazineBeforeAttachments(string prototype)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.ResolveDependency<IEntityManager>();
            var user = entities.SpawnEntity("MobHuman", map.GridCoords);
            var gun = entities.SpawnEntity(prototype, map.GridCoords);
            var slots = entities.System<ItemSlotsSystem>();
            var holo = entities.SpawnEntity("ReclamationGunHoloSight", map.GridCoords);
            var light = entities.SpawnEntity("ReclamationGunLight", map.GridCoords);
            Assert.That(slots.TryInsert(gun, GunAttachmentsSystem.HoloSlotId, holo, null), Is.True);
            Assert.That(slots.TryInsert(gun, GunAttachmentsSystem.LightSlotId, light, null), Is.True);
            var magazine = slots.GetItemOrNull(gun, "gun_magazine");
            Assert.That(magazine, Is.Not.Null);
            var verbs = entities.System<SharedVerbSystem>();
            var first = verbs.GetLocalVerbs(gun, user, typeof(AlternativeVerb), force: true).First();
            Assert.That(first.IconEntity, Is.EqualTo(entities.GetNetEntity(magazine!.Value)));
            first.Act!.Invoke();
            Assert.That(slots.GetItemOrNull(gun, "gun_magazine"), Is.Null);
            Assert.That(slots.GetItemOrNull(gun, GunAttachmentsSystem.HoloSlotId), Is.EqualTo(holo));
            Assert.That(slots.GetItemOrNull(gun, GunAttachmentsSystem.LightSlotId), Is.EqualTo(light));
            // Leave the hand empty, matching another Alt-click after putting the magazine away.
            entities.DeleteEntity(magazine.Value);
            first = verbs.GetLocalVerbs(gun, user, typeof(AlternativeVerb), force: true).First();
            Assert.That(first.IconEntity, Is.AnyOf(entities.GetNetEntity(holo), entities.GetNetEntity(light)));
            first.Act!.Invoke();
            Assert.That(slots.GetItemOrNull(gun, GunAttachmentsSystem.HoloSlotId) == null
                || slots.GetItemOrNull(gun, GunAttachmentsSystem.LightSlotId) == null, Is.True);
            entities.DeleteEntity(user);
            entities.DeleteEntity(gun);
            if (entities.EntityExists(holo)) entities.DeleteEntity(holo);
            if (entities.EntityExists(light)) entities.DeleteEntity(light);
        });
        await pair.CleanReturnAsync();
    }
}
