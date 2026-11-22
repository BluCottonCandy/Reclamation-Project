using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._ReclamationProject;

// #Reclamation Project - FEV Psi-Serum brewing: Viral Base, then FEV with a Uranium catalyst that isn't used up.
[TestFixture]
public sealed class MagicFEVReactionTests
{
    [Test]
    public async Task FevBrewsAndKeepsCatalyst()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();
        var solutions = em.System<SharedSolutionContainerSystem>();

        Entity<SolutionComponent>? viralEnt = null;
        Solution? viral = null;
        Entity<SolutionComponent>? fevEnt = null;
        Solution? fev = null;

        await server.WaitAssertion(() =>
        {
            var beaker = em.SpawnEntity("LargeBeaker", map.GridCoords);
            Assert.That(solutions.TryGetSolution(beaker, "beaker", out viralEnt, out viral));
            solutions.TryAddReagent(viralEnt!.Value, "UnstableMutagen", 2, out _);
            solutions.TryAddReagent(viralEnt.Value, "Blood", 1, out _);
            solutions.TryAddReagent(viralEnt.Value, "ExtractFungusRad", 1, out _);
            solutions.SetTemperature(viralEnt.Value, 375);

            var beaker2 = em.SpawnEntity("LargeBeaker", map.GridCoords);
            Assert.That(solutions.TryGetSolution(beaker2, "beaker", out fevEnt, out fev));
            solutions.TryAddReagent(fevEnt!.Value, "Uranium", 5, out _);
            solutions.TryAddReagent(fevEnt.Value, "MagicViralBase", 15, out _);
            solutions.TryAddReagent(fevEnt.Value, "Mentats", 5, out _);
            solutions.TryAddReagent(fevEnt.Value, "NukaColaQuantum", 5, out _);
            solutions.SetTemperature(fevEnt.Value, 405);
        });

        await server.WaitIdleAsync();

        await server.WaitAssertion(() =>
        {
            Assert.That(viral!.GetTotalPrototypeQuantity("MagicViralBase"), Is.EqualTo(FixedPoint2.New(2)), "2 Viral Base made");
            Assert.That(fev!.GetTotalPrototypeQuantity("MagicFEVPsiSerum"), Is.EqualTo(FixedPoint2.New(15)), "one full 15u dose made");
            Assert.That(fev.GetTotalPrototypeQuantity("Uranium"), Is.EqualTo(FixedPoint2.New(5)), "uranium catalyst kept");
        });

        await pair.CleanReturnAsync();
    }
}
