using System.Linq;
using Content.Server._ReclamationProject.Magic;
using Content.Server.Chat.Systems;
using Content.Server.Paper;
using Content.Shared.Language;
using Content.Shared._ReclamationProject.Magic;
using Content.Shared.Actions;
using Content.Shared.Damage;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._ReclamationProject;

// #Reclamation Project - Arcane magic stage 1: words, binding, readying, mana.
[TestFixture]
public sealed class MagicGrimoireTests
{
    [Test]
    public async Task GrimoireBindsReadiesAndCosts()
    {
        await using var pair = await PoolManager.GetServerClient();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        var proto = pair.Server.ResolveDependency<IPrototypeManager>();

        await pair.Server.WaitAssertion(() =>
        {
            var map = em.System<SharedMapSystem>().CreateMap();
            var coords = new EntityCoordinates(map, System.Numerics.Vector2.Zero);
            var magic = em.System<MagicSystem>();
            var words = em.System<MagicWordsSystem>();
            var grimoires = em.System<MagicGrimoireSystem>();
            var hands = em.System<SharedHandsSystem>();

            var user = em.SpawnEntity("MobHuman", coords);
            var other = em.SpawnEntity("MobHuman", coords);
            var pen = em.SpawnEntity("MagicQuill", coords);
            var bookUid = em.SpawnEntity("MagicGrimoire", coords);
            var book = (bookUid, em.GetComponent<MagicGrimoireComponent>(bookUid));
            var handComp = em.GetComponent<HandsComponent>(user);
            Assert.That(hands.TryPickup(user, pen, handComp.SortedHands[0]), "quill in hand");

            var fireball = proto.Index<MagicSpellPrototype>("Firebolt");
            var plasma = proto.Index<MagicSpellPrototype>("PlasmaBolt");
            var fireText = words.GetTrueText(fireball);

            // Words resolve; a misspelling does not.
            Assert.That(words.TryResolve(fireText, out var resolved, out _) && resolved == "Firebolt");
            var misspelled = fireText[..^1] + (fireText[^1] == 'A' ? 'B' : 'A');
            Assert.That(words.TryResolve(misspelled, out _, out _), Is.False, "misspelled word must not resolve");

            // True order = Empowered, the other order still works but isn't.
            var plasmaTrue = words.GetTrueText(plasma);
            var parts = plasmaTrue.Split(' ');
            Assert.That(words.TryResolve(plasmaTrue, out var p1, out var emp1) && p1 == "PlasmaBolt" && emp1, "true order empowered");
            Assert.That(words.TryResolve($"{parts[1]} {parts[0]}", out var p2, out var emp2) && p2 == "PlasmaBolt" && !emp2, "other order works, not empowered");

            // Without Arcane: the page never glows and the book doesn't bind.
            Assert.That(grimoires.WritePage(book, user, 0, fireText));
            Assert.That(book.Item2.MagicPages[0].Glow, Is.Null, "no glow without Arcane");
            Assert.That(book.Item2.MagicOwner, Is.Null, "no binding without Arcane");

            // With Arcane but Elemental still sealed (only Psychic and Dark are open): faint, sealed, no binding.
            magic.TeachArcane(user);
            Assert.That(magic.KnowsArcane(user), "knows Arcane");
            Assert.That(grimoires.WritePage(book, user, 0, fireText));
            Assert.That(book.Item2.MagicPages[0].Glow, Is.Null, "a sealed school does not wake");
            Assert.That(book.Item2.MagicPages[0].Sealed, Is.EqualTo(MagicClass.Elemental), "the page knows it is sealed Elemental");
            Assert.That(book.Item2.MagicOwner, Is.Null, "a sealed page does not bind the book");
            Assert.That(magic.CanUseSchool(user, MagicClass.Psychic) && magic.CanUseSchool(user, MagicClass.Dark), "Psychic and Dark are open");
            Assert.That(magic.CanUseSchool(user, MagicClass.Atomic), Is.False, "Atomic is sealed");

            // Unsealed (admin book / button): the same words now wake, glow and bind.
            magic.SetAllSchoolsUnlocked(user, true);
            Assert.That(grimoires.WritePage(book, user, 0, fireText));
            Assert.That(book.Item2.MagicPages[0].Glow, Is.EqualTo(MagicClass.Elemental));
            Assert.That(book.Item2.MagicOwner, Is.EqualTo(user), "bound to writer");

            // Someone else can't write in a bound book.
            magic.TeachArcane(other);
            Assert.That(grimoires.WritePage(book, other, 1, fireText), Is.False, "non-owner refused");

            // Misspelled page looks exactly like a wrong one: no glow.
            Assert.That(grimoires.WritePage(book, user, 1, misspelled));
            Assert.That(book.Item2.MagicPages[1].Glow, Is.Null);

            // Ready page 0 while holding the book: an action reaches the owner.
            Assert.That(hands.TryPickup(user, bookUid, handComp.SortedHands[1]), "book in hand");
            Assert.That(grimoires.TryToggleReady(book, user, 0), "readied");
            Assert.That(book.Item2.PageActions.TryGetValue(0, out var action), "action made");
            var actionComp = em.System<SharedActionsSystem>().GetActions(user).Select(a => a.Id).ToList();
            Assert.That(actionComp, Does.Contain(action), "owner has the spell on the hotbar");

            // The first press starts reading instead of firing; nothing is paid yet.
            var caster = em.GetComponent<MagicCasterComponent>(user);
            caster.MagicMana = 50;
            Assert.That(magic.TryPayForSpell(action, user), Is.False, "first press starts reading");
            Assert.That(caster.MagicMana, Is.EqualTo(50).Within(0.01f), "nothing paid while reading");
            Assert.That(caster.Reading, Is.Not.Null, "reading do-after running");

            // Dispel breaks the reading.
            Assert.That(magic.Dispel(user, 0f), "dispel breaks the reading");
            Assert.That(caster.Reading, Is.Null);
            Assert.That(magic.Dispel(user, 0f), Is.False, "nothing left to dispel");

            // Once reading has finished the spell fires and costs mana.
            caster.ReadyToFire = action;
            Assert.That(magic.TryPayForSpell(action, user));
            Assert.That(caster.MagicMana, Is.EqualTo(40).Within(0.01f), "fireball costs 10");

            // Overcasting hurts.
            caster.MagicMana = 0;
            var damageable = em.GetComponent<DamageableComponent>(user);
            var before = damageable.TotalDamage;
            caster.ReadyToFire = action;
            Assert.That(magic.TryPayForSpell(action, user));
            Assert.That(damageable.TotalDamage, Is.GreaterThan(before), "overcast does damage");

            // The 8 active spell limit.
            for (var i = 1; i < 8; i++)
            {
                Assert.That(grimoires.WritePage(book, user, i, fireText));
                Assert.That(grimoires.TryToggleReady(book, user, i), $"ready page {i}");
            }
            Assert.That(magic.CountActive(user), Is.EqualTo(8));
            Assert.That(grimoires.WritePage(book, user, 8, fireText));
            Assert.That(grimoires.TryToggleReady(book, user, 8), Is.False, "9th spell refused");

            // Typed casting: only after the spell was cast by hand this round, costs 25% more.
            var chatCast = em.System<MagicChatCastSystem>();
            var common = proto.EnumeratePrototypes<LanguagePrototype>().First();
            caster.MagicMana = 100;
            caster.CastThisRound.Clear();
            em.EventBus.RaiseLocalEvent(user, new EntitySpokeEvent(user, fireText + "!", null, false, common, null));
            Assert.That(caster.MagicMana, Is.EqualTo(100).Within(0.01f), "typed cast refused before a hand cast");
            caster.CastThisRound.Add("Firebolt");
            em.EventBus.RaiseLocalEvent(user, new EntitySpokeEvent(user, fireText + "!", null, false, common, null));
            Assert.That(caster.MagicMana, Is.EqualTo(87.5f).Within(0.01f), "typed cast costs 12.5 (10 + 25%)");

            // Torn page: tear out, it's bound to the owner, put it back.
            var tornSys = em.System<MagicTornPageSystem>();
            var torn = tornSys.TearOut(book, user, 0);
            Assert.That(torn, Is.Not.Null, "tore out page 0");
            Assert.That(book.Item2.MagicPages[0].Text, Is.Empty, "page left blank");
            var tornComp = em.GetComponent<MagicTornPageComponent>(torn!.Value);
            Assert.That(tornComp.MagicOwner, Is.EqualTo(user));
            Assert.That(tornComp.Page.Spell?.Id, Is.EqualTo("Firebolt"));
            Assert.That(tornSys.PutBack(book, (torn.Value, tornComp), user), "put back");
            Assert.That(book.Item2.MagicPages.Any(p => p.Spell?.Id == "Firebolt" && p.Text == fireText), "spell back in the book");

            // Mind Etching: the spell goes into the mind (named after the note) and uses a slot.
            var casting = em.System<MagicCastingSystem>();
            magic.RemoveAll(user);
            caster.MagicMana = 100;
            Assert.That(casting.Etch(user, "Blink", "hop"), "etched");
            Assert.That(caster.MagicKnownSpells, Does.Contain((ProtoId<MagicSpellPrototype>) "Blink"));
            Assert.That(caster.MagicKnownSpellNotes["Blink"], Is.EqualTo("hop"));
            Assert.That(caster.MagicMana, Is.EqualTo(100 - MagicCastingSystem.EtchMana).Within(0.01f), "etching costs mana");
            Assert.That(magic.CountActive(user), Is.EqualTo(1), "etched spell uses a slot");
            magic.ForgetSpell(user, "Blink");
            Assert.That(magic.CountActive(user), Is.EqualTo(0), "forgetting frees the slot");

            // Admin remove-all un-readies everything.
            magic.RemoveAll(user);
            Assert.That(magic.CountActive(user), Is.EqualTo(0), "remove all");

            // Admin grant-all puts every spell in the mind.
            magic.GrantAll(other);
            Assert.That(em.GetComponent<MagicCasterComponent>(other).MagicKnownSpells.Count,
                Is.EqualTo(proto.EnumeratePrototypes<MagicSpellPrototype>().Count()));
            Assert.That(em.GetComponent<MagicCasterComponent>(other).MagicSolvedWords.Count,
                Is.EqualTo(proto.EnumeratePrototypes<MagicWordPrototype>().Count()), "grant-all teaches every word to the Codex");

            // Dark magic adds Corruption; blood magic costs blood, not mana.
            var third = em.SpawnEntity("MobHuman", coords);
            magic.TeachArcane(third);
            var thirdCaster = em.GetComponent<MagicCasterComponent>(third);
            Assert.That(magic.LearnSpell(third, "ShadowBolt", ignoreLimit: true) && magic.LearnSpell(third, "BloodBolt", ignoreLimit: true));
            var shadowAction = thirdCaster.KnownSpellActions["ShadowBolt"];
            thirdCaster.ReadyToFire = shadowAction;
            Assert.That(magic.TryPayForSpell(shadowAction, third), "shadow bolt cast");
            Assert.That(thirdCaster.MagicStrain, Is.EqualTo(14).Within(0.01f), "shadow bolt adds 4 strain + 10 dark extra");

            var bloodAction = thirdCaster.KnownSpellActions["BloodBolt"];
            var blood = em.GetComponent<Content.Server.Body.Components.BloodstreamComponent>(third);
            var solutions = em.System<Content.Shared.Chemistry.EntitySystems.SharedSolutionContainerSystem>();
            Assert.That(solutions.ResolveSolution(third, blood.BloodSolutionName, ref blood.BloodSolution, out var bloodSol), "has blood");
            var bloodBefore = bloodSol!.Volume;
            var manaBefore = thirdCaster.MagicMana;
            thirdCaster.ReadyToFire = bloodAction;
            Assert.That(magic.TryPayForSpell(bloodAction, third), "blood bolt cast");
            Assert.That((float) (bloodBefore - blood.BloodSolution!.Value.Comp.Solution.Volume), Is.EqualTo(15).Within(0.5f), "blood bolt costs 15u blood");
            Assert.That(thirdCaster.MagicMana, Is.EqualTo(manaBefore).Within(0.01f), "blood bolt costs no mana");

            // FEV: someone not on the whitelist only gets sick; an awakening gives Arcane, Telepathy,
            // a Psychic/Dark spell in the mind, the admin marker and the one-time Manifest Grimoire.
            var fevSys = em.System<MagicFEVSystem>();
            var patient = em.SpawnEntity("MobHuman", coords);
            var exposure = em.EnsureComponent<MagicFEVExposureComponent>(patient);
            var patientDamage = em.GetComponent<DamageableComponent>(patient);
            var hurtBefore = patientDamage.TotalDamage;
            Assert.That(fevSys.ResolveDose(patient, exposure), Is.False, "not whitelisted = never awakened");
            Assert.That(patientDamage.TotalDamage, Is.GreaterThan(hurtBefore), "dose sickness hurts");
            Assert.That(magic.KnowsArcane(patient), Is.False, "no Arcane from sickness");

            fevSys.Awaken(patient, exposure);
            Assert.That(magic.KnowsArcane(patient), "FEV teaches Arcane");
            Assert.That(em.HasComponent<Content.Shared.Abilities.Psionics.TelepathyComponent>(patient), "FEV gives Telepathy");
            Assert.That(em.HasComponent<MagicFEVAwakenedComponent>(patient), "admin FEV marker");
            var patientCaster = em.GetComponent<MagicCasterComponent>(patient);
            Assert.That(patientCaster.MagicKnownSpells.Count, Is.EqualTo(1), "one random spell in the mind");
            var gained = proto.Index(patientCaster.MagicKnownSpells.First());
            Assert.That(gained.Class is MagicClass.Psychic or MagicClass.Dark, "only Psychic or Dark spells");
            var patientActions = em.System<SharedActionsSystem>().GetActions(patient).Select(x => em.GetComponent<MetaDataComponent>(x.Id).EntityPrototype?.ID).ToList();
            Assert.That(patientActions, Does.Contain("ActionMagicManifestGrimoire"), "Manifest Grimoire granted");

            // The Codex is filled with this round's coded words.
            // Codex: someone who never wrote it only gets a hint; the writer sees it named and spelled out.
            var codexSys = em.System<MagicItemsSystem>();
            var firePlain = words.GetWordText("Fire");
            var stranger = codexSys.CodexEntries(patient).First(e => e.Hint == "warmth");
            Assert.That(stranger.Solved, Is.False);
            Assert.That(stranger.Name, Is.Empty, "unsolved words are not named");
            Assert.That(stranger.Plain, Is.Empty, "unsolved words are not spelled out");
            Assert.That(stranger.Scrambled, Is.Not.EqualTo(firePlain), "codex runes are scrambled");
            Assert.That(string.Concat(stranger.Scrambled.OrderBy(c => c)), Is.EqualTo(string.Concat(firePlain.OrderBy(c => c))), "same runes, shuffled");
            Assert.That(stranger.First, Is.EqualTo(firePlain[0]), "first rune given as the glowing hint");
            var writer = codexSys.CodexEntries(user).First(e => e.Hint == "warmth");
            Assert.That(writer.Solved && writer.Name == "Fire" && writer.Plain == firePlain, "the writer sees Fire named and spelled out");

            // Blank vellum stitches in one page each, up to the book's limit.
            var pagesBefore = book.Item2.MagicPages.Count;
            Assert.That(grimoires.AddPages(book, user, em.SpawnEntity("MagicVellum", coords), 1));
            Assert.That(book.Item2.MagicPages.Count, Is.EqualTo(pagesBefore + 1), "vellum adds a page");
            book.Item2.MagicMaxPages = book.Item2.MagicPages.Count;
            Assert.That(grimoires.AddPages(book, user, em.SpawnEntity("MagicVellum", coords), 1), Is.False, "full book refuses vellum");
            Assert.That(book.Item2.MagicPages.Count, Is.EqualTo(pagesBefore + 1), "no page past the limit");
            book.Item2.MagicMaxPages = 40;

            // Codex leaves bind into the book once each; the window only gets words for bound classes.
            Assert.That(grimoires.BuildState(book, user).Codex, Is.Empty, "no leaves, no words");
            Assert.That(grimoires.BindLeaf(book, user, em.SpawnEntity("MagicCodexKey", coords), null));
            Assert.That(book.Item2.MagicHasKey, "key bound");
            Assert.That(grimoires.BindLeaf(book, user, em.SpawnEntity("MagicCodexKey", coords), null), Is.False, "second key refused");
            Assert.That(grimoires.BindLeaf(book, user, em.SpawnEntity("MagicCodexLeafElemental", coords), MagicClass.Elemental));
            Assert.That(grimoires.BindLeaf(book, user, em.SpawnEntity("MagicCodexLeafElemental", coords), MagicClass.Elemental), Is.False, "duplicate class leaf refused");
            var state = grimoires.BuildState(book, user);
            Assert.That(state.HasKey && state.CodexClasses.SequenceEqual(new[] { MagicClass.Elemental }));
            Assert.That(state.Codex, Is.Not.Empty, "elemental words shown");
            Assert.That(state.Codex.All(e => e.Class == MagicClass.Elemental), "only the bound class's words");
            Assert.That(state.Codex.Any(e => e.Hint == "warmth" && e.Solved), "the owner's solved word is spelled out");
            Assert.That(state.KnownWords.Any(e => e.Name == "Fire" && e.Plain == firePlain), "Words tab lists Fire, spelled out");
            Assert.That(state.KnownWords.All(e => e.Solved), "Words tab only shows solved words");

            // Spell Index: binds once and lists the spells the owner knows, with what they do.
            Assert.That(state.HasSpellIndex, Is.False, "no index, no Spells tab");
            Assert.That(grimoires.BindSpellIndex(book, user, em.SpawnEntity("MagicSpellIndex", coords)));
            Assert.That(grimoires.BindSpellIndex(book, user, em.SpawnEntity("MagicSpellIndex", coords)), Is.False, "second index refused");
            em.System<MagicSystem>().LearnSpell(user, "Fireball", ignoreLimit: true);
            var indexed = grimoires.BuildState(book, user);
            Assert.That(indexed.HasSpellIndex, "Spells tab shown");
            Assert.That(indexed.KnownSpells.Any(e => e.Summary.Length > 0 && e.Where.Length > 0 && e.ManaCost > 0), "known spells listed with what they do");

            // Memorising: refused before 10 casts, then the spell lives in the mind; forgetting resets the count.
            var userCaster = em.GetComponent<MagicCasterComponent>(user);
            userCaster.MagicIgnoreActiveLimit = true;
            magic.ForgetSpell(user, "Ignite");
            userCaster.MagicCastCounts["Ignite"] = MagicCasterComponent.MemoriseCasts - 1;
            Assert.That(magic.Memorise(user, "Ignite"), Is.False, "9 casts is not enough");
            userCaster.MagicCastCounts["Ignite"] = MagicCasterComponent.MemoriseCasts;
            Assert.That(magic.Memorise(user, "Ignite"), "10 casts: memorised");
            Assert.That(userCaster.MagicKnownSpells, Does.Contain(new ProtoId<MagicSpellPrototype>("Ignite")), "in the mind");
            Assert.That(userCaster.MagicMemorisedSpells, Does.Contain(new ProtoId<MagicSpellPrototype>("Ignite")));
            magic.ForgetSpell(user, "Ignite");
            Assert.That(userCaster.MagicMemorisedSpells, Is.Empty, "forgotten");
            Assert.That(userCaster.MagicCastCounts.ContainsKey("Ignite"), Is.False, "needs 10 more casts again");
        });

        await pair.CleanReturnAsync();
    }
}
