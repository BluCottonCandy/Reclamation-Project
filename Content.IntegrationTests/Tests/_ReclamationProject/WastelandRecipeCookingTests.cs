using System.Collections.Generic;
using System.Linq;
using Content.Server.Kitchen.Components;
using Content.Server.Kitchen.EntitySystems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Kitchen;
using Content.Server.Power.Components;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._ReclamationProject;

[TestFixture]
public sealed class WastelandRecipeCookingTests
{
    [TestCase("N14RecipeBrainBurger")]
    [TestCase("N14RecipeCheeseburger")]
    [TestCase("N14RecipeCarpBurger")]
    [TestCase("N14RecipeBurgerMcguffin")]
    [TestCase("N14RecipeBurgerMcrib")]
    [TestCase("N14RecipeCornbreadMuffin")]
    [TestCase("N14RecipeMeatBread")]
    [TestCase("N14RecipeCreamCheeseBread")]
    [TestCase("N14RecipeSpiderMeatBread")]
    [TestCase("N14TofuBread")]
    [TestCase("N14RecipeTwoBreadSlice")]
    [TestCase("N14RecipeMargheritaPizza")]
    [TestCase("N14RecipeMeatPizza")]
    [TestCase("N14RecipeRiceRoadkill")]
    [TestCase("N14RecipeRiceGumbo")]
    [TestCase("N14RecipeEggRice")]
    [TestCase("N14RecipeEyeballSoup")]
    [TestCase("N14RecipeOnionSoup")]
    [TestCase("N14RecipeVegetableSoup")]
    [TestCase("N14RecipeBoiledSpaghetti")]
    [TestCase("N14RecipeCannabisBrownies")]
    [TestCase("N14RecipeRadscorpionLeg")]
    [TestCase("N14RecipeFrenchToast")]
    [TestCase("N14RecipeBurgerMcguffin", true)]
    [TestCase("N14RecipeEggRice", true)]
    [TestCase("N14RecipeCheeseburger", true)]
    [TestCase("N14RecipeCreamCheeseBread", true)]
    [TestCase("N14TofuBread", true)]
    [TestCase("N14RecipeBoiledSpaghetti", true)]
    [TestCase("N14RecipeCannabisBrownies", true)]
    public async Task RealN14IngredientsCookTheIntendedDish(string recipeId, bool ordinaryVariants = false)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.ResolveDependency<IEntityManager>();
            var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
            var recipe = prototypes.Index<FoodRecipePrototype>(recipeId);
            var microwave = em.SpawnEntity("KitchenMicrowave", map.GridCoords);
            em.GetComponent<ApcPowerReceiverComponent>(microwave).Powered = true;
            var component = em.GetComponent<MicrowaveComponent>(microwave);
            component.CurrentCookTimerTime = recipe.CookTime;
            var container = em.System<SharedContainerSystem>();
            var replacements = new Dictionary<string, string>
            {
                ["N14FoodMeatRadRaw"] = "N14FoodMeatBrahmin",
                ["N14FoodMeatRadCutlet"] = "N14FoodMeatBrahminCutlet",
                ["OrganHumanBrain"] = "OrganAnimalBrain",
                ["OrganHumanEyes"] = "OrganAnimalEyes",
                ["FoodDoughFlat"] = "N14FoodDoughFlat",
                ["FoodMeatFish"] = "FoodMeatWhitefishRaw",
            };
            if (ordinaryVariants)
            {
                replacements["N14FoodEggChicken"] = "FoodEgg";
                replacements["N14FoodCheeseSlice"] = "FoodCheeseSlice";
                replacements["N14FoodDough"] = "FoodDough";
                replacements["N14FoodTofu"] = "FoodTofu";
            }
            var solidItems = new List<EntityUid>();
            foreach (var (ingredient, amount) in recipe.IngredientsSolids)
            for (var i = 0; i < amount.Int(); i++)
            {
                var item = em.SpawnEntity(replacements.GetValueOrDefault(ingredient, ingredient), map.GridCoords);
                solidItems.Add(item);
                Assert.That(container.Insert(item, component.Storage), Is.True);
            }
            if (recipe.IngredientsReagents.Count > 0)
            {
                var beaker = em.SpawnEntity("LargeBeaker", map.GridCoords);
                var solutions = em.System<SharedSolutionContainerSystem>();
                Assert.That(solutions.TryGetSolution(beaker, "beaker", out var solution, out _), Is.True);
                solutions.SetCapacity(solution.Value, 1000);
                foreach (var (reagent, amount) in recipe.IngredientsReagents)
                    Assert.That(solutions.TryAddReagent(solution.Value, ordinaryVariants && reagent == "ExtractRazorgrainFlour" ? "Flour" : reagent, amount), Is.True);
                Assert.That(container.Insert(beaker, component.Storage), Is.True);
            }
            var system = em.System<MicrowaveSystem>();
            system.Wzhzhzh(microwave, component, null);
            Assert.That(em.TryGetComponent<ActiveMicrowaveComponent>(microwave, out var active), Is.True);
            Assert.That(active!.PortionedRecipe.Item1?.Result, Is.EqualTo(recipe.Result),
                $"Selected {active.PortionedRecipe.Item1?.ID} instead of {recipeId}.");
            system.Update(recipe.CookTime + 1);
            Assert.That(solidItems.All(item => !em.EntityExists(item)), Is.True, "All matching solid ingredients must be consumed.");
            Assert.That(component.Storage.ContainedEntities.All(item => em.GetComponent<MetaDataComponent>(item).EntityPrototype?.ID == "LargeBeaker"), Is.True, "All solid ingredients must be consumed, including aliases.");
            Assert.That(em.EntityQuery<MetaDataComponent>().Any(meta => meta.EntityPrototype?.ID == recipe.Result.Id), Is.True);
            em.DeleteEntity(microwave);
        });
        await pair.CleanReturnAsync();
    }
}
