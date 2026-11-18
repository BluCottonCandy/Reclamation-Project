using System;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server._Misfits.Hallucinations;
using Content.Server._ReclamationProject.Hallucinations;
using Content.Server.EntityEffects.Effects.StatusEffects;
using Content.Shared._Misfits.Hallucinations;
using Content.Shared._Misfits.Special.Components;
using Content.Shared._Misfits.StealthBoy;
using Content.Shared._ReclamationProject.Hallucinations;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage;
using Content.Client.Guidebook;
using Content.Client.Guidebook.Richtext;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server.Nutrition.Components;
using Content.Server.Nutrition.EntitySystems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Drugs;
using Robust.Shared.ContentPack;
using Content.Shared.Drunk;
using Content.Shared.EntityEffects;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Systems;
using Content.Shared.StatusEffect;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._ReclamationProject;

public sealed class StillmindPowderTest : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman";

    private const string Powder = "ReclamationStillmindPowder";
    private const string Neurostatic = "MisfitsNeurostatic";
    private const string Protection = "HallucinationSuppression";

    [Test]
    public async Task CraftFromThreeWildPlantsAtIntelligenceEight()
    {
        var coords = SEntMan.GetCoordinates(PlayerCoords);
        var flower = await SpawnEntity("N14FloraProduceWildBrocFlower", coords);
        var root = await SpawnEntity("N14FloraProduceWildXander", coords);
        var pear = await SpawnEntity("N14FloraProduceWildPricklyPearFruit", coords);
        await Server.WaitPost(() => SEntMan.EnsureComponent<SpecialComponent>(SPlayer).BaseIntelligence = 7);
        await CraftItem(Powder, shouldSucceed: false);
        await FindEntity(Powder, shouldSucceed: false);
        await Server.WaitAssertion(() =>
        {
            Assert.That(new[] { flower, root, pear }.All(uid => SEntMan.EntityExists(uid)), Is.True);
            SEntMan.GetComponent<SpecialComponent>(SPlayer).BaseIntelligence = 8;
        });
        await CraftItem(Powder);
        var packet = await FindEntity(Powder);
        await Server.WaitAssertion(() =>
        {
            Assert.That(new[] { flower, root, pear }.All(uid => !SEntMan.EntityExists(uid)), Is.True);
            var solutions = SEntMan.System<SharedSolutionContainerSystem>();
            Assert.That(solutions.TryGetSolution(packet, "food", out _, out var contents), Is.True);
            Assert.That(contents!.Volume, Is.EqualTo((FixedPoint2)5));
            Assert.That(contents.GetTotalPrototypeQuantity(Powder), Is.EqualTo(contents.Volume));
        });
    }

    [Test]
    public async Task SwallowingPacketCausesThreeBoutsAndSparesBothMedicines()
    {
        var item = await PlaceInHands(Powder);
        await Server.WaitAssertion(() =>
        {
            var uid = SEntMan.GetEntity(item);
            var food = SEntMan.System<FoodSystem>();
            Assert.That(food.TryFeed(SPlayer, SPlayer, uid, SEntMan.GetComponent<FoodComponent>(uid)).Success, Is.True);
        });
        await RunTicks(5);
        await AwaitDoAfters();
        await Server.WaitAssertion(() =>
        {
            var purge = SEntMan.GetComponent<StillmindPurgeComponent>(SPlayer);
            var solutions = SEntMan.System<SharedSolutionContainerSystem>();
            var stomach = SEntMan.System<BodySystem>().GetBodyOrganComponents<StomachComponent>(SPlayer).Single().Comp.Owner;
            Assert.That(solutions.TryGetSolution(stomach, StomachSystem.DefaultSolutionName, out var gastric, out var contents), Is.True);
            Assert.That(solutions.TryGetSolution(SPlayer, "chemicals", out var stream, out var blood), Is.True);
            solutions.TryAddReagent(gastric!.Value, Neurostatic, 4);
            solutions.TryAddReagent(gastric.Value, "Water", 4);
            solutions.TryAddReagent(stream!.Value, Powder, 3);
            solutions.TryAddReagent(stream.Value, Neurostatic, 2);

            var system = SEntMan.System<ChemPurgeSystem>();
            var hunger = SEntMan.GetComponent<HungerComponent>(SPlayer);
            var initialHunger = hunger.CurrentHunger;
            for (var remaining = 3; remaining > 0; remaining--)
            {
                Assert.That(purge.BoutsRemaining, Is.EqualTo(remaining));
                purge.NextBout = TimeSpan.Zero;
                system.Update(0);
                system.Update(0); // A second update in the same tick must not produce another bout.
            }
            Assert.That(SEntMan.HasComponent<StillmindPurgeComponent>(SPlayer), Is.False);
            Assert.That(hunger.CurrentHunger, Is.EqualTo(initialHunger - 24f).Within(0.01f));
            Assert.That(contents!.GetTotalPrototypeQuantity("Water"), Is.EqualTo(FixedPoint2.Zero));
            Assert.That(contents.GetTotalPrototypeQuantity(Powder), Is.EqualTo((FixedPoint2)5));
            Assert.That(contents.GetTotalPrototypeQuantity(Neurostatic), Is.EqualTo((FixedPoint2)4));
            Assert.That(blood!.GetTotalPrototypeQuantity(Powder), Is.EqualTo((FixedPoint2)3));
            Assert.That(blood.GetTotalPrototypeQuantity(Neurostatic), Is.EqualTo((FixedPoint2)2));
        });
    }

    [Test]
    public async Task FieldMedicineGuideLinksAndRenders()
    {
        await Client.WaitAssertion(() =>
        {
            var prototypes = Client.ResolveDependency<IPrototypeManager>();
            var field = prototypes.Index<GuideEntryPrototype>("ReclamationMedicineField");
            Assert.That(field.Children, Does.Contain("ReclamationStillmindMedicine"));
            var entry = prototypes.Index<GuideEntryPrototype>("ReclamationStillmindMedicine");
            using var reader = Client.ResolveDependency<IResourceManager>().ContentFileReadText(entry.Text);
            Assert.That(Client.ResolveDependency<DocumentParsingManager>().TryAddMarkup(new Document(), reader.ReadToEnd()), Is.True);
        });
    }

    [TestCase(Powder, 8)]
    [TestCase(Neurostatic, 30)]
    public async Task ClearsCurrentEpisodeAndPreventsRecurrence(string medicine, int seconds)
    {
        await Server.WaitAssertion(() =>
        {
            var pain = SEntMan.System<HallucinationPainSystem>();
            var status = SEntMan.System<StatusEffectsSystem>();
            var mobs = SEntMan.System<MobStateSystem>();
            SEntMan.EnsureComponent<UnstableComponent>(SPlayer);
            SEntMan.EnsureComponent<DrugHighComponent>(SPlayer);
            Assert.That(status.TryAddStatusEffect<SeeingRainbowsComponent>(SPlayer, "SeeingRainbows",
                TimeSpan.FromMinutes(4), true), Is.True);
            SEntMan.System<SharedDrunkSystem>().TryApplyDrunkenness(SPlayer, 300);
            Assert.That(status.HasStatusEffect(SPlayer, "Drunk"), Is.True);
            Assert.That(status.HasStatusEffect(SPlayer, "SlurredSpeech"), Is.True);
            SEntMan.EnsureComponent<MisfitsParacusiaComponent>(SPlayer);
            SEntMan.EnsureComponent<StealthBoyActiveComponent>(SPlayer);
            SEntMan.System<HallucinationsSystem>().RefreshIntensity(SPlayer);
            var episode = SEntMan.GetComponent<HallucinationsComponent>(SPlayer);
            Assert.That(episode.BreakdownDistortion, Is.Not.Null);

            SEntMan.System<DamageableSystem>().TryChangeDamage(SPlayer,
                new DamageSpecifier { DamageDict = new() { ["Blunt"] = 18 } }, ignoreResistances: true, doPartDamage: false);
            var injuries = SEntMan.GetComponent<DamageableComponent>(SPlayer);
            var before = injuries.TotalDamage;
            var encounter = Guid.NewGuid();
            Assert.That(pain.Begin(SPlayer, encounter), Is.True);
            var perceived = SEntMan.GetComponent<HallucinationPainComponent>(SPlayer);
            for (var i = 0; i < 50 && !perceived.Collapsing; i++)
            {
                perceived.NextHitAt = TimeSpan.Zero;
                Assert.That(pain.Hit(SPlayer, encounter, false), Is.True);
            }
            Assert.That(mobs.IsCritical(SPlayer), Is.True);

            void Metabolize(string reagent)
            {
                var effects = ProtoMan.Index<ReagentPrototype>(reagent).Metabolisms!["Medicine"].Effects;
                var args = new EntityEffectBaseArgs(SPlayer, SEntMan);
                effects.OfType<PurgeActiveChems>().Single().Effect(args);
                effects.OfType<GenericStatusEffect>().Single().Effect(args);
            }
            var solutions = SEntMan.System<SharedSolutionContainerSystem>();
            Assert.That(solutions.TryGetSolution(SPlayer, "chemicals", out var stream, out var chemicals), Is.True);
            foreach (var id in new[] { Powder, Neurostatic, "Ethanol", "RadAway", "DirtyStimpak" })
                Assert.That(solutions.TryAddReagent(stream!.Value, id, 5), Is.True);
            Metabolize(medicine);
            Assert.That(chemicals!.Volume, Is.EqualTo((FixedPoint2)10));
            foreach (var id in new[] { Powder, Neurostatic })
                Assert.That(chemicals.GetTotalPrototypeQuantity(id), Is.EqualTo((FixedPoint2)5));
            Assert.That(SEntMan.HasComponent<StillmindPurgeComponent>(SPlayer), Is.False);
            Assert.That(status.TryGetTime(SPlayer, Protection, out var time), Is.True);
            Assert.That(time!.Value.Item2 - time.Value.Item1, Is.EqualTo(TimeSpan.FromSeconds(seconds)));
            Assert.That(SEntMan.HasComponent<DrugHighComponent>(SPlayer), Is.False);
            Assert.That(SEntMan.HasComponent<SeeingRainbowsComponent>(SPlayer), Is.False);
            SEntMan.System<SharedDrunkSystem>().TryApplyDrunkenness(SPlayer, 300);
            Assert.That(status.HasStatusEffect(SPlayer, "Drunk"), Is.False);
            Assert.That(status.HasStatusEffect(SPlayer, "SlurredSpeech"), Is.False);
            Assert.That(episode.BreakdownDistortion, Is.Null);
            Assert.That(mobs.IsAlive(SPlayer), Is.True);
            Assert.That(injuries.TotalDamage, Is.EqualTo(before));
            Assert.That(perceived.Amount, Is.Zero);
            Assert.That(pain.Hit(SPlayer, encounter, true), Is.False);
            Assert.That(pain.Begin(SPlayer, Guid.NewGuid()), Is.False);
            Assert.That(status.TryAddStatusEffect<SeeingRainbowsComponent>(SPlayer, "SeeingRainbows",
                TimeSpan.FromMinutes(1), true), Is.False);
            Metabolize(Neurostatic);
            Metabolize(Powder);
            Assert.That(status.TryGetTime(SPlayer, Protection, out time), Is.True);
            Assert.That(time!.Value.Item2 - time.Value.Item1, Is.EqualTo(TimeSpan.FromSeconds(30)));
        });
        await RunTicks(5);
        await Client.WaitAssertion(() =>
        {
            Assert.That(CEntMan.HasComponent<HallucinationSuppressionComponent>(CPlayer), Is.True);
            var visuals = CEntMan.System<Content.Client._ReclamationProject.Hallucinations.DrugHallucinationSystem>();
            Assert.That(visuals.SpawnEncounter(CPlayer, false), Is.False);
            Assert.That(visuals.SpawnWeather(CPlayer), Is.False);
            Assert.That(visuals.SpawnTemptations(CPlayer, HallucinationKind.Food), Is.False);
        });
        await Server.WaitAssertion(() =>
        {
            var status = SEntMan.System<StatusEffectsSystem>();
            Assert.That(status.TryRemoveStatusEffect(SPlayer, Protection), Is.True);
            Assert.That(SEntMan.HasComponent<UnstableComponent>(SPlayer), Is.True);
            Assert.That(SEntMan.GetComponent<HallucinationsComponent>(SPlayer).BreakdownDistortion, Is.Not.Null);
            Assert.That(status.TryAddStatusEffect<SeeingRainbowsComponent>(SPlayer, "SeeingRainbows",
                TimeSpan.FromMinutes(1), true), Is.True);
        });
    }
}
