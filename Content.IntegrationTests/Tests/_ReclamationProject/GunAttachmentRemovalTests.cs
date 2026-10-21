using System.Linq;
using Content.Server.Power.EntitySystems;
using Content.Shared._ReclamationProject.GunAttachments;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Verbs;
using Content.Shared.Hands.EntitySystems;
using Content.Shared._ReclamationProject.Weapons.Ranged;
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
    [TestCase("MinutemenWeaponLaserMusket")]
    [TestCase("MinutemenWeaponLaserShotgun")]
    public async Task AltClickCranksBeforeEjectingCellOrAttachments(string prototype)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid gun = default, user = default, cell = default;
        await pair.Server.WaitAssertion(() =>
        {
            user = entities.SpawnEntity("MobHuman", map.GridCoords);
            gun = entities.SpawnEntity(prototype, map.GridCoords);
            Assert.That(entities.System<SharedHandsSystem>().TryPickup(user, gun), Is.True);
            var slots = entities.System<ItemSlotsSystem>();
            cell = slots.GetItemOrNull(gun, "gun_magazine")!.Value;
            var light = entities.SpawnEntity("ReclamationGunLight", map.GridCoords);
            Assert.That(slots.TryInsert(gun, GunAttachmentsSystem.LightSlotId, light, null), Is.True);
            var crank = entities.GetComponent<CrankChargeComponent>(gun);
            crank.CrankDelay = TimeSpan.Zero;
            var verbs = entities.System<SharedVerbSystem>().GetLocalVerbs(gun, user, typeof(AlternativeVerb), force: true);
            Assert.That(verbs.First().Priority, Is.GreaterThan(20));
            Assert.That(verbs.Any(verb => verb.IconEntity == entities.GetNetEntity(cell)), Is.True,
                "Cell ejection must remain available in the menu.");
            verbs.First().Act!.Invoke();
            Assert.That(slots.GetItemOrNull(gun, "gun_magazine"), Is.EqualTo(cell));
        });
        await pair.RunTicksSync(5);
        await pair.Server.WaitAssertion(() =>
        {
            var crank = entities.GetComponent<CrankChargeComponent>(gun);
            Assert.That(crank.Charges, Is.EqualTo(1));
            var slots = entities.System<ItemSlotsSystem>();
            Assert.That(slots.GetItemOrNull(gun, "gun_magazine"), Is.EqualTo(cell));
            crank.Charges = crank.MaxCharges;
            var first = entities.System<SharedVerbSystem>().GetLocalVerbs(gun, user, typeof(AlternativeVerb), force: true).First();
            first.Act!.Invoke();
            Assert.That(slots.GetItemOrNull(gun, "gun_magazine"), Is.EqualTo(cell), "Full charge must not eject the cell.");
            // A fractional remainder cannot fire a shot and should be treated as empty.
            entities.System<BatterySystem>().SetCharge(cell, 1);
            Assert.That(crank.CellEmpty, Is.True);
            var residualVerb = entities.System<SharedVerbSystem>().GetLocalVerbs(gun, user, typeof(AlternativeVerb), force: true).First();
            Assert.That(residualVerb.IconEntity, Is.EqualTo(entities.GetNetEntity(cell)));
            entities.System<BatterySystem>().SetCharge(cell, 0);
            Assert.That(crank.CellEmpty, Is.True);
            var emptyVerb = entities.System<SharedVerbSystem>().GetLocalVerbs(gun, user, typeof(AlternativeVerb), force: true).First();
            Assert.That(emptyVerb.IconEntity, Is.EqualTo(entities.GetNetEntity(cell)));
            emptyVerb.Act!.Invoke();
            Assert.That(slots.GetItemOrNull(gun, "gun_magazine"), Is.Null);
            entities.DeleteEntity(user);
        });
        await pair.CleanReturnAsync();
    }
}
