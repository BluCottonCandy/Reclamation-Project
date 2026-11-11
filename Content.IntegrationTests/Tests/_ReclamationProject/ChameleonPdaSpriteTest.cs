using System.Collections.Generic;
using System.Linq;
using Content.Server.Clothing.Systems;
using Content.Shared.Clothing.Components;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._ReclamationProject;

/// <summary>
/// #Reclamation Project - every disguise in the Institute tablet's chameleon list must look like the real item
/// on the client (same sprite file and picture). Pip-Boys and PDAs used to turn invisible because their base
/// picture is only filled in when the real item spawns.
/// </summary>
[TestFixture]
public sealed class ChameleonPdaSpriteTest
{
    [Test]
    public async Task EveryTabletDisguiseLooksLikeTheRealItem()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var client = pair.Client;
        var sEntities = server.ResolveDependency<IEntityManager>();
        var cEntities = client.ResolveDependency<IEntityManager>();
        var protos = server.ResolveDependency<IPrototypeManager>();
        var factory = server.ResolveDependency<IComponentFactory>();

        var targets = protos.Index<EntityPrototype>("InstituteScientistTablet")
            .TryGetComponent<ChameleonClothingComponent>(out var chameleon, factory)
            ? chameleon.AdditionalTargets.Select(t => t.Id).ToList()
            : new List<string>();
        Assert.That(targets, Is.Not.Empty, "the tablet has no disguise list");

        var pairs = new List<(string Target, EntityUid Disguised, EntityUid Real)>();
        await server.WaitPost(() =>
        {
            foreach (var target in targets)
            {
                Assert.That(protos.HasIndex<EntityPrototype>(target), $"disguise target {target} doesn't exist");
                var tablet = sEntities.SpawnEntity("InstituteScientistTablet", map.GridCoords);
                sEntities.System<ChameleonClothingSystem>().SetSelectedPrototype(tablet, target, forceUpdate: true);
                var real = sEntities.SpawnEntity(target, map.GridCoords);
                pairs.Add((target, tablet, real));
            }
        });
        await pair.RunTicksSync(30);

        await client.WaitAssertion(() =>
        {
            var problems = new List<string>();
            foreach (var (target, disguisedUid, realUid) in pairs)
            {
                var disguised = cEntities.GetComponent<SpriteComponent>(cEntities.GetEntity(sEntities.GetNetEntity(disguisedUid)));
                var real = cEntities.GetComponent<SpriteComponent>(cEntities.GetEntity(sEntities.GetNetEntity(realUid)));

                // The first visible layer is the item's main look (the ID/PDA lights sit on top of it).
                static string Main(SpriteComponent sprite)
                {
                    foreach (var layer in sprite.AllLayers)
                    {
                        if (layer.Visible && layer.RsiState.Name != null)
                            return $"{layer.ActualRsi?.Path}:{layer.RsiState.Name}";
                    }
                    return "nothing";
                }

                var want = Main(real);
                var got = Main(disguised);
                if (got != want)
                    problems.Add($"{target}: disguise draws {got} but the real item draws {want}");
            }

            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        });

        await pair.CleanReturnAsync();
    }
}
