using System.Linq;
using Robust.Shared.Prototypes;

namespace Content.Shared.Kitchen
{
    public sealed class RecipeManager
    {
        [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

        public List<FoodRecipePrototype> Recipes { get; private set; } = new();

        public void Initialize()
        {
            // #Reclamation Project - stable order: most ingredients first, then by ID. List.Sort is unstable,
            // so adding any recipe used to reshuffle equally-matched recipes (e.g. carp burger vs cheeseburger).
            Recipes = _prototypeManager.EnumeratePrototypes<FoodRecipePrototype>()
                .Where(item => !item.SecretRecipe)
                .OrderByDescending(item => item.IngredientCount())
                .ThenBy(item => item.ID, StringComparer.Ordinal)
                .ToList();
        }
        /// <summary>
        /// Check if a prototype ids appears in any of the recipes that exist.
        /// </summary>
        public bool SolidAppears(string solidId)
        {
            return Recipes.Any(recipe => recipe.IngredientsSolids.ContainsKey(solidId));
        }

        private sealed class RecipeComparer : Comparer<FoodRecipePrototype>
        {
            public override int Compare(FoodRecipePrototype? x, FoodRecipePrototype? y)
            {
                if (x == null || y == null)
                {
                    return 0;
                }

                var nx = x.IngredientCount();
                var ny = y.IngredientCount();
                return -nx.CompareTo(ny);
            }
        }
    }
}
