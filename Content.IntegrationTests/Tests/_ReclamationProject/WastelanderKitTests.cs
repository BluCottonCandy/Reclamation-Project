using System.Collections.Generic;
using System.Linq;
using Content.Server.Medical.CPR;
using Content.Server.UndecidedLoadout.Components;
using Content.Shared.Clothing.Loadouts.Prototypes;
using Content.Shared.Customization.Systems;
using Content.Shared.Interaction.Events;
using Content.Shared.UndecidedLoadout;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture, NonParallelizable]
public sealed class WastelanderKitTests
{
    [Test]
    public async Task SurplusRequiresTwoValidDistinctChoicesAndConsumesBox()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        var kit = EntityUid.Invalid;
        await pair.Server.WaitAssertion(() =>
        {
            kit = em.SpawnEntity("ReclamationMilitarySurplusSelector", map.GridCoords);
            var component = em.GetComponent<UndecidedLoadoutBackpackComponent>(kit);
            Assert.That(component.MaxSelectedSets, Is.EqualTo(2));
            foreach (var index in new[] { -1, component.PossibleSets.Count })
                em.EventBus.RaiseLocalEvent(kit, new UndecidedLoadoutBackpackChangeSetMessage(index));
            Assert.That(component.SelectedSets, Is.Empty);
            em.EventBus.RaiseLocalEvent(kit, new UndecidedLoadoutBackpackChangeSetMessage(0));
            em.EventBus.RaiseLocalEvent(kit, new UndecidedLoadoutBackpackApproveMessage());
            Assert.That(em.HasComponent<UndecidedLoadoutBackpackComponent>(kit), Is.True);
            em.EventBus.RaiseLocalEvent(kit, new UndecidedLoadoutBackpackChangeSetMessage(1));
            em.EventBus.RaiseLocalEvent(kit, new UndecidedLoadoutBackpackChangeSetMessage(2));
            Assert.That(component.SelectedSets, Is.EqualTo(new[] { 0, 1 }));
            em.EventBus.RaiseLocalEvent(kit, new UndecidedLoadoutBackpackApproveMessage());
            Assert.That(em.HasComponent<UndecidedLoadoutBackpackComponent>(kit), Is.False);
        });
        await pair.RunTicksSync(2);
        await pair.Server.WaitAssertion(() => Assert.That(em.EntityExists(kit), Is.False));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SurplusAmmunitionMatchesEveryBallisticGun()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        var proto = pair.Server.ResolveDependency<IPrototypeManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var kit = em.SpawnEntity("ReclamationMilitarySurplusSelector", map.GridCoords);
            foreach (var setId in em.GetComponent<UndecidedLoadoutBackpackComponent>(kit).PossibleSets)
            {
                var set = proto.Index(setId);
                Assert.That(set.Content, Has.Count.EqualTo(2));
                var gun = em.SpawnEntity(set.Content[0], map.GridCoords);
                var box = em.SpawnEntity(set.Content[1], map.GridCoords);
                if (em.TryGetComponent<BallisticAmmoProviderComponent>(box, out var ammo))
                {
                    var providers = new HashSet<string>();
                    void Inspect(EntityUid uid)
                    {
                        if (em.TryGetComponent<BallisticAmmoProviderComponent>(uid, out var ballistic) && ballistic.Proto is {} cartridge)
                            providers.Add(cartridge.Id);
                        if (em.TryGetComponent<RevolverAmmoProviderComponent>(uid, out var revolver) && revolver.FillPrototype is {} round)
                            providers.Add(round.Id);
                        if (!em.HasComponent<ContainerManagerComponent>(uid)) return;
                        foreach (var container in em.System<SharedContainerSystem>().GetAllContainers(uid))
                            foreach (var child in container.ContainedEntities) Inspect(child);
                    }
                    Inspect(gun);
                    Assert.That(providers, Does.Contain(ammo.Proto!.Value.Id), setId.ToString());
                }
                em.DeleteEntity(gun);
                em.DeleteEntity(box);
            }
            em.DeleteEntity(kit);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CprBookTeachesOnceAndVanishes()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        var book = EntityUid.Invalid;
        await pair.Server.WaitAssertion(() =>
        {
            var user = em.SpawnEntity("MobHuman", map.GridCoords);
            book = em.SpawnEntity("ReclamationTrainingManualCPR", map.GridCoords);
            Assert.That(em.HasComponent<CPRTrainingComponent>(user), Is.False);
            em.EventBus.RaiseLocalEvent(book, new UseInHandEvent(user));
            Assert.That(em.HasComponent<CPRTrainingComponent>(user), Is.True);
        });
        await pair.RunTicksSync(2);
        await pair.Server.WaitAssertion(() => Assert.That(em.EntityExists(book), Is.False));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FactionLoadoutsRetainChoicesButWastelanderGunsAreBlocked()
    {
        await using var pair = await PoolManager.GetServerClient();
        var proto = pair.Server.ResolveDependency<IPrototypeManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var gun = proto.Index<LoadoutPrototype>("N14WeaponPistol9mm");
            var restriction = gun.Requirements.OfType<CharacterDepartmentRequirement>()
                .Single(r => r.Departments.Count == 1 && r.Departments[0].Id == "Wastelander");
            Assert.That(restriction.Inverted, Is.True);
            Assert.That(gun.Items.Single().Id, Is.EqualTo("N14WeaponPistol9mmMakeshift"));
        });
        await pair.CleanReturnAsync();
    }
}
