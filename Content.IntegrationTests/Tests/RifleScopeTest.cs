#nullable enable annotations

using System;
using Content.Server._Misfits.Scope;
using Content.Shared._Misfits.Scope;
using Content.Shared.Actions;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Systems;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests;

[TestFixture]
public sealed class RifleScopeTest
{
    [Test]
    public async Task ScopedRiflesHaveOpticsAndUnscopedLeverActionsDoNot()
    {
        await using var pair = await PoolManager.GetServerClient();
        var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
        await pair.Server.WaitAssertion(() =>
        {
            foreach (var id in new[]
                     {
                         "N14WeaponRifle762Marksman", "N14WeaponSniper50AntiMateriel", "N14WeaponSniperHunting",
                         "N14WeaponRifle308SniperRifle", "N14WeaponSniper308Canadian", "N14WeaponSniper50NCRRifle",
                         "N14WeaponSniperPaciencia", "N14WeaponSniperRatslayer", "N14WeaponRifle762MarksmanCanadian",
                         "N14WeaponRifle762MarksmanChinese", "N14WeaponRifleM1GarandRepublicsPride",
                         "N14WeaponRifleM1GarandRepublicsDemise", "N14WeaponSniperHuntingMakeshift",
                     })
            {
                var proto = prototypes.Index<EntityPrototype>(id);
                Assert.That(proto.TryGetComponent("Scope", out ScopeComponent? scope), Is.True, id);
                Assert.That(proto.Components.ContainsKey("Wieldable"), Is.True, id);
                Assert.That(scope!.RequireWielding && scope.ActionsOnlyWhenWielded, Is.True, id);
                Assert.That(scope.ScopedMovementMultiplier, Is.EqualTo(0.1f), id);
                Assert.That(scope.ZoomLevels[0].AllowMovement, Is.True, id);
            }
            foreach (var id in new[] { "N14WeaponSniper44LeverCarbine", "N14WeaponSniper308LeverRifle", "N14WeaponSniperCowboyRepeater", "N14WeaponRifle10mmM1Carbine" })
                Assert.That(prototypes.Index<EntityPrototype>(id).Components.ContainsKey("Scope"), Is.False, id);

            var binoculars = prototypes.Index<EntityPrototype>("N14Binoculars");
            Assert.That(binoculars.TryGetComponent("Scope", out ScopeComponent? binocularScope), Is.True);
            Assert.That(binocularScope!.ScopedMovementMultiplier, Is.EqualTo(1f));
            Assert.That(binocularScope.ActionsOnlyWhenWielded, Is.False);
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task WalkingKeepsZoomAndUnwieldOrDropRestoresSpeed(bool drop)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        var handsSystem = entities.System<SharedHandsSystem>();
        var wielding = entities.System<WieldableSystem>();
        var scopes = entities.System<ScopeSystem>();
        EntityUid human = default;
        EntityUid rifle = default;
        float walkSpeed = 0;
        float sprintSpeed = 0;
        ScopeComponent scope = default!;
        await pair.Server.WaitAssertion(() =>
        {
            human = entities.SpawnEntity("MobHuman", map.GridCoords);
            rifle = entities.SpawnEntity("N14WeaponSniperHunting", map.GridCoords);
            var hands = entities.GetComponent<HandsComponent>(human);
            Assert.That(handsSystem.TryPickup(human, rifle, hands.ActiveHand!, checkActionBlocker: false), Is.True);
            scope = entities.GetComponent<ScopeComponent>(rifle);
            Assert.That(HasAction(entities, human, scope.ScopingToggleActionEntity!.Value), Is.False);
            Assert.That(scopes.StartScoping((rifle, scope), human), Is.Null, "One-handed use must be rejected");
            Assert.That(wielding.TryWield(rifle, entities.GetComponent<WieldableComponent>(rifle), human), Is.True);
            Assert.That(HasAction(entities, human, scope.ScopingToggleActionEntity!.Value), Is.True);
            var movement = entities.GetComponent<MovementSpeedModifierComponent>(human);
            walkSpeed = movement.CurrentWalkSpeed;
            sprintSpeed = movement.CurrentSprintSpeed;
            Assert.That(scopes.StartScoping((rifle, scope), human), Is.Not.Null);
        });
        await pair.RunTicksSync(60);
        await pair.Server.WaitAssertion(() =>
        {
            var active = entities.GetComponent<ScopingComponent>(human);
            Assert.That(active.AllowMovement, Is.True);
            var offset = active.EyeOffset;
            Assert.That(offset.Length(), Is.GreaterThan(0));
            var mover = entities.GetComponent<InputMoverComponent>(human);
            mover.HeldMoveButtons = MoveButtons.Up | MoveButtons.Walk;
            var move = new MoveInputEvent((human, mover), MoveButtons.None);
            entities.EventBus.RaiseLocalEvent(human, ref move);
            mover.HeldMoveButtons = MoveButtons.None;
            Assert.That(entities.HasComponent<ScopingComponent>(human), Is.True, "Walking cancelled zoom");
            Assert.That(active.EyeOffset, Is.EqualTo(offset));
            var movement = entities.GetComponent<MovementSpeedModifierComponent>(human);
            Assert.That(movement.CurrentWalkSpeed, Is.EqualTo(walkSpeed * 0.1f).Within(0.001));
            Assert.That(movement.CurrentSprintSpeed, Is.EqualTo(sprintSpeed * 0.1f).Within(0.001));
        });
        await pair.RunTicksSync(5);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(entities.HasComponent<ScopingComponent>(human), Is.True, "Walking cancelled zoom after the tick");
            var currentOffset = entities.GetComponent<ScopingComponent>(human).EyeOffset;
            Assert.That(currentOffset.Length(), Is.GreaterThan(0));
            if (drop)
                Assert.That(handsSystem.TryDrop(human, rifle, checkActionBlocker: false), Is.True);
            else
                Assert.That(wielding.TryUnwield(rifle, entities.GetComponent<WieldableComponent>(rifle), human), Is.True);
        });
        await pair.RunTicksSync(5);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(entities.HasComponent<ScopingComponent>(human), Is.False);
            Assert.That(scope.User, Is.Null);
            Assert.That(HasAction(entities, human, scope.ScopingToggleActionEntity!.Value), Is.False);
            var movement = entities.GetComponent<MovementSpeedModifierComponent>(human);
            Assert.That(movement.CurrentWalkSpeed, Is.GreaterThan(walkSpeed * 0.1f));
            Assert.That(movement.CurrentSprintSpeed, Is.GreaterThan(sprintSpeed * 0.1f));
            entities.DeleteEntity(rifle);
            entities.DeleteEntity(human);
        });
        await pair.CleanReturnAsync();
    }
    private static bool HasAction(IEntityManager entities, EntityUid user, EntityUid action)
    {
        var actions = entities.GetComponent<ActionsComponent>(user).Actions;
        return actions.Contains(action);
    }

}
