using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server._ReclamationProject.Pack;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server.Jobs;
using Content.Server.Nutrition.Components;
using Content.Server.Nutrition.EntitySystems;
using Content.Shared._ReclamationProject.Pack;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Roles;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._ReclamationProject;

public sealed class PackRawMeatTest : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman";
    private const string Brahdo = "MisfitFoodMeatBrahdoRaw";
    private const string RadProtein = "N14UncookedAnimalProteinsRad";

    [TestCase(true, true)]
    [TestCase(true, false)]
    [TestCase(false, false)]
    public async Task BrahdoSafetyFollowsTheEater(bool packEater, bool selfFeed)
    {
        var eater = selfFeed ? SPlayer : await SpawnEntity("MobHuman", SEntMan.GetCoordinates(PlayerCoords));
        var meat = SEntMan.GetEntity(await PlaceInHands(Brahdo));
        await Server.WaitAssertion(() =>
        {
            SEntMan.EnsureComponent<PackMemberComponent>(packEater ? eater : SPlayer);
            var food = SEntMan.System<FoodSystem>();
            Assert.That(food.TryFeed(SPlayer, eater, meat, SEntMan.GetComponent<FoodComponent>(meat)).Success, Is.True);
        });
        await RunTicks(5);
        await AwaitDoAfters();
        await Server.WaitAssertion(() =>
        {
            var solutions = SEntMan.System<SharedSolutionContainerSystem>();
            var stomach = SEntMan.System<BodySystem>().GetBodyOrganComponents<StomachComponent>(eater).Single().Comp.Owner;
            Assert.That(solutions.TryGetSolution(stomach, StomachSystem.DefaultSolutionName, out _, out var contents), Is.True);
            Assert.That(contents!.Volume, Is.GreaterThan(FixedPoint2.Zero));
            Assert.That(contents.GetTotalPrototypeQuantity(packEater ? "Nutriment" : RadProtein), Is.EqualTo(contents.Volume));
            if (selfFeed)
            {
                Assert.That(SEntMan.EntityExists(meat), Is.False);
                Assert.That(contents.Volume, Is.EqualTo((FixedPoint2) 20));
            }
        });

        // Wait past the stomach's digestion delay so the human poison metabolism gets a turn.
        await RunSeconds(32);
        await Server.WaitAssertion(() =>
        {
            var damage = SEntMan.GetComponent<DamageableComponent>(eater).Damage.DamageDict;
            foreach (var type in new[] { "Poison", "Radiation" })
            {
                var amount = damage.GetValueOrDefault(type);
                if (packEater)
                    Assert.That(amount, Is.EqualTo(FixedPoint2.Zero), type);
                else
                    Assert.That(amount, Is.GreaterThan(FixedPoint2.Zero), type);
            }
        });
    }

    [Test]
    public async Task EveryPackJobGrantsTheDiet()
    {
        await Server.WaitAssertion(() =>
        {
            foreach (var job in ProtoMan.Index<DepartmentPrototype>("Pack").Roles)
            {
                Assert.That(ProtoMan.Index<JobPrototype>(job).Special.OfType<AddComponentSpecial>()
                    .Any(special => special.Components.ContainsKey("PackMember")), Is.True, job.Id);
            }
        });
    }

    [Test]
    public async Task OnlyIntrinsicRawMeatHazardsAreDigested()
    {
        await Server.WaitAssertion(() =>
        {
            SEntMan.EnsureComponent<PackMemberComponent>(SPlayer);
            var digestion = SEntMan.System<PackDigestionSystem>();
            var foodSystem = SEntMan.System<FoodSystem>();
            foreach (var (prototype, hazard) in new[]
            {
                (Brahdo, RadProtein), ("N14FoodMeatRadCutlet", "N14RadParticleFood"),
                ("N14FoodMeatRadscorpionLeg", RadProtein), ("FoodMeatSpiderLeg", "UncookedAnimalProteins"), ("FoodMeatFish", "CarpoToxin"),
                ("FoodMeatXenoCutlet", "SulfuricAcid"), ("FoodMeatSnake", "Toxin"), ("OrganHumanLiver", "UncookedAnimalProteins"),
            })
            {
                var uid = SEntMan.SpawnEntity(prototype, SEntMan.GetCoordinates(PlayerCoords));
                Assert.That(foodSystem.IsDigestibleBy(SPlayer, uid), Is.True, prototype);
                var food = SEntMan.GetComponent<FoodComponent>(uid);
                Assert.That(digestion.TryGetRawMeatRecipe(SPlayer, (uid, food), out var recipe), Is.True, prototype);
                var original = recipe!.GetTotalPrototypeQuantity(hazard);
                Assert.That(original, Is.GreaterThan(FixedPoint2.Zero), prototype);
                var bite = new Solution(recipe);
                bite.AddReagent("Lexorin", 1); // An unrelated poison added after the animal was butchered.
                digestion.DigestRawMeat(bite, recipe);
                Assert.That(bite.Volume, Is.EqualTo(recipe.Volume + 1), prototype);
                Assert.That(bite.GetTotalPrototypeQuantity(hazard), Is.EqualTo(FixedPoint2.Zero), prototype);
                Assert.That(bite.GetTotalPrototypeQuantity("Lexorin"), Is.EqualTo((FixedPoint2) 1), prototype);
                Assert.That(recipe.GetTotalPrototypeQuantity(hazard), Is.EqualTo(original), prototype);
            }

            var cooked = SEntMan.SpawnEntity("MisfitsFoodMeatBrahdoCooked", SEntMan.GetCoordinates(PlayerCoords));
            Assert.That(digestion.TryGetRawMeatRecipe(SPlayer, (cooked, SEntMan.GetComponent<FoodComponent>(cooked)), out _), Is.False);
        });
    }
}
