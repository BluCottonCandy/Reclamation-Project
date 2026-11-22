using System.Collections.Generic;
using Robust.Shared.Maths;
using System.Linq;
using Content.Server._ReclamationProject.Magic;
using Content.Shared._ReclamationProject.Magic;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._ReclamationProject;

// #Reclamation Project - the spell kit: every recipe works, everything spells spawn exists, hits and wards behave.
[TestFixture]
public sealed class MagicSpellKitTests
{
    [Test]
    public async Task EverySpellResolvesAndSpawns()
    {
        await using var pair = await PoolManager.GetServerClient();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        var proto = pair.Server.ResolveDependency<IPrototypeManager>();

        await pair.Server.WaitAssertion(() =>
        {
            var map = em.System<SharedMapSystem>().CreateMap();
            var coords = new EntityCoordinates(map, System.Numerics.Vector2.Zero);
            var words = em.System<MagicWordsSystem>();

            var spells = proto.EnumeratePrototypes<MagicSpellPrototype>().ToList();
            Assert.That(spells.Count, Is.GreaterThan(180), "the full spell list is loaded");

            foreach (var spell in spells)
            {
                // The true word order always casts this exact spell (no two spells share a recipe).
                Assert.That(words.TryResolve(words.GetTrueText(spell), out var resolved, out _) && resolved == spell.ID,
                    $"{spell.ID} must resolve from its own words");

                // Its action entity spawns cleanly.
                var action = em.SpawnEntity(spell.Action, coords);
                em.DeleteEntity(action);
            }

            // Everything the spells spawn exists and spawns.
            foreach (var ent in proto.EnumeratePrototypes<EntityPrototype>())
            {
                if (ent.Abstract || !(ent.ID.StartsWith("MagicZone") || ent.ID.StartsWith("MagicConjured")
                                      || ent.ID is "MagicEarthWall" or "MagicIceWall" or "MagicMirrorImage" or "MagicShadowClone"
                                          or "MagicShadowServant" or "MagicShacklesCuffs" or "MagicDoomVortex"
                                          or "MagicSmokeCloud" or "MagicRainEffect" or "MagicHailEffect" or "MagicEarthSpikeEffect"))
                    continue;
                var spawned = em.SpawnEntity(ent.ID, coords);
                em.DeleteEntity(spawned);
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task HitsWardsAndShields()
    {
        await using var pair = await PoolManager.GetServerClient();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        var proto = pair.Server.ResolveDependency<IPrototypeManager>();

        await pair.Server.WaitAssertion(() =>
        {
            var map = em.System<SharedMapSystem>().CreateMap();
            var coords = new EntityCoordinates(map, System.Numerics.Vector2.Zero);
            var kit = em.System<MagicSpellKitSystem>();

            var caster = em.SpawnEntity("MobHuman", coords);
            var target = em.SpawnEntity("MobHuman", coords);
            var heat = new DamageSpecifier(proto.Index<DamageTypePrototype>("Heat"), 10);
            var hit = new MagicHit { Damage = heat };

            // A plain hit lands, and Empowered (1.5) hits harder.
            kit.ApplyHit(target, hit, 1f, caster);
            Assert.That(em.GetComponent<DamageableComponent>(target).TotalDamage.Float(), Is.EqualTo(10f).Within(0.5f));
            kit.ApplyHit(target, hit, 1.5f, caster);
            Assert.That(em.GetComponent<DamageableComponent>(target).TotalDamage.Float(), Is.EqualTo(25f).Within(0.5f));

            // Healing takes it back off.
            kit.ApplyHit(target, new MagicHit { Heal = heat }, 1f, caster);
            Assert.That(em.GetComponent<DamageableComponent>(target).TotalDamage.Float(), Is.EqualTo(15f).Within(0.5f));

            // Mind Shield: the next hostile spell is swallowed, the one after lands.
            kit.ApplyHit(target, new MagicHit { Condition = MagicCondition.Ward, ConditionTime = 60, ConditionPower = 1 }, 1f, target);
            kit.ApplyHit(target, hit, 1f, caster);
            Assert.That(em.GetComponent<DamageableComponent>(target).TotalDamage.Float(), Is.EqualTo(15f).Within(0.5f), "ward blocked it");
            kit.ApplyHit(target, hit, 1f, caster);
            Assert.That(em.GetComponent<DamageableComponent>(target).TotalDamage.Float(), Is.EqualTo(25f).Within(0.5f), "ward used up");

            // Blood Shield: soaks up to its strength.
            kit.ApplyHit(target, new MagicHit { Condition = MagicCondition.Absorb, ConditionTime = 60, ConditionPower = 15 }, 1f, target);
            kit.ApplyHit(target, hit, 1f, caster);
            Assert.That(em.GetComponent<DamageableComponent>(target).TotalDamage.Float(), Is.EqualTo(25f).Within(0.5f), "fully soaked");
            kit.ApplyHit(target, hit, 1f, caster);
            Assert.That(em.GetComponent<DamageableComponent>(target).TotalDamage.Float(), Is.EqualTo(30f).Within(0.5f), "5 left to soak");

            // Precognition: the next hit misses completely.
            kit.ApplyHit(target, new MagicHit { Condition = MagicCondition.Dodge, ConditionTime = 5 }, 1f, target);
            kit.ApplyHit(target, hit, 1f, caster);
            Assert.That(em.GetComponent<DamageableComponent>(target).TotalDamage.Float(), Is.EqualTo(30f).Within(0.5f), "dodged");

            // Hex: blocks the caster's next spells (checked by TryUseCondition).
            kit.AddCondition(caster, MagicCondition.Hex, 60, 2, target);
            Assert.That(kit.TryUseCondition(caster, MagicCondition.Hex));
            Assert.That(kit.TryUseCondition(caster, MagicCondition.Hex));
            Assert.That(kit.TryUseCondition(caster, MagicCondition.Hex), Is.False, "two charges only");

            // Teleporting with no mana at all can leave limbs or organs behind (never the brain, heart or lungs).
            var body = em.System<Content.Shared.Body.Systems.SharedBodySystem>();
            var magic = em.System<MagicSystem>();
            var jumper = em.SpawnEntity("MobHuman", coords);
            var casterComp = em.EnsureComponent<MagicCasterComponent>(jumper);
            int Parts() => body.GetBodyChildren(jumper).Count() + body.GetBodyOrgans(jumper).Count();
            var before = Parts();
            for (var i = 0; i < 30; i++)
            {
                casterComp.LastOvercast = 1f;
                magic.TeleportMishap(jumper, coords);
            }
            Assert.That(Parts(), Is.LessThan(before), "overcast teleports should have left something behind");
            Assert.That(body.GetBodyOrgans(jumper).Any(o => em.GetComponent<MetaDataComponent>(o.Id).EntityPrototype!.ID.Contains("Brain")), "the brain always stays");
            casterComp.LastOvercast = 0f;
            var after = Parts();
            magic.TeleportMishap(jumper, coords);
            Assert.That(Parts(), Is.EqualTo(after), "paying in full never loses anything");

            // Mana drain moves mana between casters.
            em.EnsureComponent<MagicCasterComponent>(target).MagicMana = 50;
            kit.ApplyHit(target, new MagicHit { Mana = -20 }, 1f, caster);
            Assert.That(em.GetComponent<MagicCasterComponent>(target).MagicMana, Is.EqualTo(30f).Within(0.01f));

            // Shitmed: healing spells heal a real human body AND its body parts (same path as medkits).
            var dmgSys = em.System<DamageableSystem>();
            var patient = em.SpawnEntity("MobHuman", coords);
            var blunt = new DamageSpecifier(proto.Index<DamageTypePrototype>("Blunt"), 40);
            dmgSys.TryChangeDamage(patient, blunt, true);
            float PartDamage() => body.GetBodyChildren(patient)
                .Sum(bp => em.TryGetComponent<DamageableComponent>(bp.Id, out var d) ? d.TotalDamage.Float() : 0f);
            var bodyBefore = em.GetComponent<DamageableComponent>(patient).TotalDamage.Float();
            var partsBefore = PartDamage();
            Assert.That(bodyBefore, Is.GreaterThan(0f), "the patient got hurt");
            kit.ApplyHit(patient, new MagicHit { Heal = new DamageSpecifier(proto.Index<DamageTypePrototype>("Blunt"), 30) }, 1f, caster);
            Assert.That(em.GetComponent<DamageableComponent>(patient).TotalDamage.Float(), Is.LessThan(bodyBefore), "healing spell heals the body");
            Assert.That(PartDamage(), Is.LessThanOrEqualTo(partsBefore), "and never adds damage to body parts");
            Assert.That(body.GetBodyChildren(patient).Count(), Is.GreaterThan(4), "healing never severs a limb");

            // Rock Bullet: stored rocks all fly out on the caster's next attack.
            var thrower = em.SpawnEntity("MobHuman", coords);
            var rocks = em.EnsureComponent<MagicStoredRocksComponent>(thrower);
            rocks.Projectile = "ProjectileMagicRockBullet";
            for (var r = 0; r < 3; r++)
            {
                rocks.Rocks.Add(em.SpawnEntity("MagicOrbitingRock", coords));
                rocks.Expires.Add(TimeSpan.FromHours(1));
            }
            int RockShots() => em.EntityQuery<MetaDataComponent>().Count(m => m.EntityPrototype?.ID == "ProjectileMagicRockBullet");
            var shotsBefore = RockShots();
            em.System<Content.Server._ReclamationProject.Magic.MagicRockBulletSystem>().Fire((thrower, rocks), System.Numerics.Vector2.UnitX);
            Assert.That(RockShots() - shotsBefore, Is.EqualTo(3), "all three stored rocks fly out on the left click");
            Assert.That(rocks.Rocks, Is.Empty, "the stored rocks are used up");

            // Telepathy: after Grant all spells the caster can use telepathic chat (nothing blocks it).
            var telepath = em.SpawnEntity("MobHuman", coords);
            em.System<MagicSystem>().GrantAll(telepath);
            Assert.That(em.HasComponent<Content.Shared.Abilities.Psionics.TelepathyComponent>(telepath), "granted mage has telepathy");
            Assert.That(em.HasComponent<Content.Shared.Abilities.Psionics.PsionicsDisabledComponent>(telepath), Is.False, "telepathy not disabled");
            Assert.That(em.HasComponent<Content.Shared.Abilities.Psionics.PsionicInsulationComponent>(telepath), Is.False, "telepathy not insulated");

            // Mirror Image: the placeholder becomes a copy of the caster that pops when hurt.
            var mirrors = em.System<Content.Server._ReclamationProject.Magic.MagicMirrorImageSystem>();
            var placeholder = em.SpawnEntity("MagicMirrorImage", coords);
            int Copies() => em.EntityQuery<MagicMirrorImageComponent>().Count(c => em.HasComponent<Content.Shared.Humanoid.HumanoidAppearanceComponent>(c.Owner));
            var copiesBefore = Copies();
            mirrors.MakeLookalike(caster, placeholder);
            Assert.That(Copies(), Is.EqualTo(copiesBefore + 1), "a humanoid copy of the caster appears");
            var copy = em.EntityQuery<MagicMirrorImageComponent>().First(c => em.HasComponent<Content.Shared.Humanoid.HumanoidAppearanceComponent>(c.Owner)).Owner;
            Assert.That(em.GetComponent<MetaDataComponent>(copy).EntityName, Is.EqualTo(em.GetComponent<MetaDataComponent>(caster).EntityName), "it has the caster's name");
            Assert.That(em.HasComponent<Content.Shared.Strip.Components.StrippableComponent>(copy), Is.False, "it can't be stripped");
            dmgSys.TryChangeDamage(copy, new DamageSpecifier(proto.Index<DamageTypePrototype>("Blunt"), 5), true);
            Assert.That(em.Deleted(copy) || em.IsQueuedForDeletion(copy), "one hit pops it");
        });
        await pair.CleanReturnAsync();
    }
    /// <summary>Fire spells really leave the floor burning: in front of the caster, never on their own tile.</summary>
    [Test]
    public async Task FireSpellsBurnTheFloor()
    {
        await using var pair = await PoolManager.GetServerClient();
        var testMap = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();

        await pair.Server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            var xforms = em.System<SharedTransformSystem>();
            for (var x = -5; x <= 5; x++)
            {
                for (var y = -5; y <= 5; y++)
                    maps.SetTile(testMap.Grid.Owner, testMap.Grid.Comp, new Vector2i(x, y), testMap.Tile.Tile);
            }

            var caster = em.SpawnEntity("MobHuman", new EntityCoordinates(testMap.Grid.Owner, 0.5f, 0.5f));
            var origin = xforms.GetMapCoordinates(caster);
            var click = new MapCoordinates(origin.Position + new System.Numerics.Vector2(3f, 0f), origin.MapId);
            List<EntityUid> Fires() => em.EntityQuery<MetaDataComponent>()
                .Where(m => m.EntityPrototype?.ID == MagicFloorFireSystem.FloorFire).Select(m => m.Owner).ToList();

            em.System<MagicFloorFireSystem>().FireShape(origin, click, MagicShape.Cone, 0.5f, 3f);
            var fires = Fires();
            Assert.That(fires.Count, Is.GreaterThan(3), "a cone of fire burns the floor in front of the caster");
            Assert.That(fires.All(f => (xforms.GetMapCoordinates(f).Position - origin.Position).Length() > 0.6f),
                "never on the caster's own tile");
        });
        await pair.CleanReturnAsync();
    }
    /// <summary>Psy-Forge +25% and Greater Psy-Forge +50% stack to +75% of the armour's NORMAL protection.</summary>
    [Test]
    public async Task PsyForgeStacksOnArmour()
    {
        await using var pair = await PoolManager.GetServerClient();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        var proto = pair.Server.ResolveDependency<IPrototypeManager>();
        var factory = pair.Server.ResolveDependency<IComponentFactory>();

        await pair.Server.WaitAssertion(() =>
        {
            var map = em.System<SharedMapSystem>().CreateMap();
            var coords = new EntityCoordinates(map, System.Numerics.Vector2.Zero);

            // Any armour that protects against Blunt.
            var armourProto = proto.EnumeratePrototypes<EntityPrototype>().First(p => !p.Abstract
                && p.TryGetComponent<Content.Shared.Armor.ArmorComponent>(out var a, factory)
                && a.Modifiers.Coefficients.TryGetValue("Blunt", out var c) && c < 0.95f);
            var item = em.SpawnEntity(armourProto.ID, coords);
            var armour = em.GetComponent<Content.Shared.Armor.ArmorComponent>(item);
            var baseProtection = 1f - armour.Modifiers.Coefficients["Blunt"];

            var forge = em.System<MagicPsyForgeSystem>();
            Assert.That(forge.Forge(item, 0.25f, false), Is.EqualTo(0.25f).Within(0.001f));
            Assert.That(1f - armour.Modifiers.Coefficients["Blunt"], Is.EqualTo(MathF.Min(0.9f, baseProtection * 1.25f)).Within(0.001f), "+25% of normal");
            Assert.That(forge.Forge(item, 0.5f, true), Is.EqualTo(0.75f).Within(0.001f));
            Assert.That(1f - armour.Modifiers.Coefficients["Blunt"], Is.EqualTo(MathF.Min(0.9f, baseProtection * 1.75f)).Within(0.001f), "both stack to +75% of normal");
            Assert.That(em.GetComponent<MetaDataComponent>(item).EntityName, Does.StartWith("twice psy-forged"), "the name shows it");

            // Weapons: a knife hits 25% harder.
            var knife = em.SpawnEntity("KitchenKnife", coords);
            var melee = em.GetComponent<Content.Shared.Weapons.Melee.MeleeWeaponComponent>(knife);
            var before = melee.Damage.GetTotal().Float();
            forge.Forge(knife, 0.25f, false);
            Assert.That(melee.Damage.GetTotal().Float(), Is.EqualTo(before * 1.25f).Within(0.01f), "a forged weapon hits 25% harder");

            // Power cells hold 50% more.
            var cell = em.SpawnEntity("PowerCellSmall", coords);
            var battery = em.GetComponent<Content.Server.Power.Components.BatteryComponent>(cell);
            var cap = battery.MaxCharge;
            forge.Forge(cell, 0.5f, true);
            Assert.That(battery.MaxCharge, Is.EqualTo(cap * 1.5f).Within(0.01f), "a forged cell holds 50% more");

            // Bullets: a round can be forged (its bullet is boosted when fired).
            var round = em.SpawnEntity("CartridgePistol", coords);
            Assert.That(forge.CanForge(round), "bullets can be forged");
            Assert.That(forge.Forge(round, 0.25f, false), Is.EqualTo(0.25f).Within(0.001f));
        });
        await pair.CleanReturnAsync();
    }
    /// <summary>The psy katana starts sheathed (scabbard bash), and using it in hand draws the blade.</summary>
    [Test]
    public async Task PsyKatanaDrawsAndSheathes()
    {
        await using var pair = await PoolManager.GetServerClient();
        var em = pair.Server.ResolveDependency<IEntityManager>();

        await pair.Server.WaitAssertion(() =>
        {
            var map = em.System<SharedMapSystem>().CreateMap();
            var coords = new EntityCoordinates(map, System.Numerics.Vector2.Zero);
            var user = em.SpawnEntity("MobHuman", coords);
            var katana = em.SpawnEntity("MagicPsyKatana", coords);
            var blade = em.GetComponent<MagicPsyBladeComponent>(katana);
            var melee = em.GetComponent<Content.Shared.Weapons.Melee.MeleeWeaponComponent>(katana);
            Assert.That(blade.Drawn, Is.False, "starts sheathed");
            Assert.That(melee.Damage.DamageDict.ContainsKey("Blunt"), "sheathed it bashes");
            Assert.That(em.GetComponent<Content.Shared.Item.ItemComponent>(katana).HeldPrefix, Is.EqualTo("sheathed"));

            Assert.That(em.System<Content.Shared.Hands.EntitySystems.SharedHandsSystem>().TryPickupAnyHand(user, katana), "picked up");
            em.System<Content.Shared.Interaction.SharedInteractionSystem>().UseInHandInteraction(user, katana);
            Assert.That(blade.Drawn, "using it draws the blade");
            Assert.That(melee.Damage.DamageDict.ContainsKey("Slash"), "drawn it cuts");
            Assert.That(em.GetComponent<Content.Shared.Item.ItemComponent>(katana).HeldPrefix, Is.Null, "drawn look in hand");
            Assert.That(em.HasComponent<MagicSwordGuardComponent>(katana), "has the sword guard");
        });
        await pair.CleanReturnAsync();
    }
    /// <summary>Admin rejuvenate fills mana back up and takes out every bullet fragment.</summary>
    [Test]
    public async Task RejuvenateRefillsManaAndRemovesBullets()
    {
        await using var pair = await PoolManager.GetServerClient();
        var em = pair.Server.ResolveDependency<IEntityManager>();

        await pair.Server.WaitAssertion(() =>
        {
            var map = em.System<SharedMapSystem>().CreateMap();
            var coords = new EntityCoordinates(map, System.Numerics.Vector2.Zero);
            var user = em.SpawnEntity("MobHuman", coords);
            var caster = em.EnsureComponent<MagicCasterComponent>(user);
            caster.MagicMana = 5f;

            var bio = em.System<Content.Server._ReclamationProject.Medical.BioMonitor.BioMonitorSystem>();
            var frag = em.SpawnEntity("ReclamationShrapnelFragment", coords);
            Assert.That(bio.EmbedFragment(frag, user, Content.Shared._Shitmed.Targeting.TargetBodyPart.Torso), "fragment goes in");
            Assert.That(bio.HasStuckFragments(user), "it is inside");

            em.EventBus.RaiseLocalEvent(user, new Content.Shared.Rejuvenate.RejuvenateEvent());
            Assert.That(caster.MagicMana, Is.EqualTo(caster.MagicMaxMana).Within(0.01f), "mana is full again");
            Assert.That(bio.HasStuckFragments(user), Is.False, "the fragment is gone");
        });
        await pair.CleanReturnAsync();
    }
    /// <summary>Undying Will brings a dead body back (hurt but alive); Astral Projection sends the mind out and back.</summary>
    [Test]
    public async Task SoulSpellsReviveAndProject()
    {
        await using var pair = await PoolManager.GetServerClient();
        var em = pair.Server.ResolveDependency<IEntityManager>();

        await pair.Server.WaitAssertion(() =>
        {
            var map = em.System<SharedMapSystem>().CreateMap();
            var coords = new EntityCoordinates(map, System.Numerics.Vector2.Zero);
            var mobState = em.System<Content.Shared.Mobs.Systems.MobStateSystem>();
            var soul = em.System<MagicSoulSystem>();

            // Undying Will: killed, then back on their feet below the crit point.
            var dead = em.SpawnEntity("MobHuman", coords);
            var damage = em.System<DamageableSystem>();
            var proto = pair.Server.ResolveDependency<IPrototypeManager>();
            damage.TryChangeDamage(dead, new DamageSpecifier(proto.Index<DamageTypePrototype>("Blunt"), 250), ignoreResistances: true);
            Assert.That(mobState.IsDead(dead), "killed first");
            var bio = em.System<Content.Server._ReclamationProject.Medical.BioMonitor.BioMonitorSystem>();
            Assert.That(bio.EmbedFragment(em.SpawnEntity("ReclamationShrapnelFragment", coords), dead,
                Content.Shared._Shitmed.Targeting.TargetBodyPart.LeftArm), "shrapnel goes in");
            soul.Revive(dead, 0.75f);
            Assert.That(mobState.IsAlive(dead), "Undying Will brings them back");
            Assert.That(bio.HasStuckFragments(dead), Is.False, "coming back pulls the shrapnel out");

            // Astral Projection: the mind visits a spirit form, then returns and the form is gone.
            var user = em.SpawnEntity("MobHuman", coords);
            var minds = em.System<Content.Shared.Mind.SharedMindSystem>();
            var mind = minds.CreateMind(null);
            minds.TransferTo(mind, user);
            em.EventBus.RaiseEvent(EventSource.Local, new MagicAstralProjectionEvent { Performer = user });
            var body = em.GetComponent<MagicAstralBodyComponent>(user);
            Assert.That(body.Form, Is.Not.Null, "a spirit form is made");
            var form = body.Form!.Value;
            Assert.That(em.GetComponent<Content.Shared.Mind.MindComponent>(mind).VisitingEntity, Is.EqualTo(form), "the mind rides the form");

            soul.EndProjection((user, body), null);
            Assert.That(em.GetComponent<Content.Shared.Mind.MindComponent>(mind).VisitingEntity, Is.Null, "back in the body");
            Assert.That(em.HasComponent<MagicAstralBodyComponent>(user), Is.False);
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>The Augmented Human: shows as human, Director-whitelisted, tougher, Courser nanites and strength, Institute.</summary>
    [Test]
    public async Task AugmentedHumanRace()
    {
        await using var pair = await PoolManager.GetServerClient();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        var proto = pair.Server.ResolveDependency<IPrototypeManager>();

        await pair.Server.WaitAssertion(() =>
        {
            var species = proto.Index<Content.Shared.Humanoid.Prototypes.SpeciesPrototype>("AugmentedHuman");
            Assert.That(species.DisplaySpecies?.Id, Is.EqualTo("Human"), "shows as a normal human");
            Assert.That(species.JobWhitelistUnlock?.Id, Is.EqualTo("InstituteDirector"), "the Director whitelist unlocks it");
            Assert.That(species.CountsAs?.Id, Is.EqualTo("Human"), "counts as Human for job lists");

            // A Human-only job list lets them in without naming them.
            var humansOnly = new Content.Shared.Customization.Systems.CharacterSpeciesRequirement { Species = new() { "Human" } };
            var job = proto.EnumeratePrototypes<Content.Shared.Roles.JobPrototype>().First();
            var profile = Content.Shared.Preferences.HumanoidCharacterProfile.DefaultWithSpecies("AugmentedHuman");
            Assert.That(humansOnly.IsValid(job, profile, new(), true, job, em, proto,
                pair.Server.ResolveDependency<Robust.Shared.Configuration.IConfigurationManager>(),
                pair.Server.ResolveDependency<Content.Shared._NC.Sponsor.ISharedSponsorManager>(), out _), "passes a Human-only job");

            var map = em.System<SharedMapSystem>().CreateMap();
            var uid = em.SpawnEntity("MobInstituteDirector", new EntityCoordinates(map, System.Numerics.Vector2.Zero));
            var thresholds = em.System<Content.Shared.Mobs.Systems.MobThresholdSystem>();
            Assert.That(thresholds.TryGetThresholdForState(uid, Content.Shared.Mobs.MobState.Critical, out var crit) && crit == 200, "crit at 200");
            Assert.That(thresholds.TryGetThresholdForState(uid, Content.Shared.Mobs.MobState.Dead, out var dead) && dead == 300, "dead at 300");
            Assert.That(em.HasComponent<Content.Shared._ReclamationProject.Institute.Nanites.NaniteRepairComponent>(uid), "Courser nanites");
            Assert.That(em.System<Content.Shared.NPC.Systems.NpcFactionSystem>().IsMember(uid, "Institute"), "counts as Institute");
        });
        await pair.CleanReturnAsync();
    }
    /// <summary>A forged gun really fires harder-hitting bullets, its loaded shells are forged too, and the bonuses never stack.</summary>
    [Test]
    public async Task PsyForgedGunFiresHarder()
    {
        await using var pair = await PoolManager.GetServerClient();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        float plain = 0f, forged = 0f;
        EntityUid plainGun = default, forgedGun = default;

        await pair.Server.WaitAssertion(() =>
        {
            var map = em.System<SharedMapSystem>().CreateMap();
            var coords = new EntityCoordinates(map, System.Numerics.Vector2.Zero);
            var guns = em.System<Content.Shared.Weapons.Ranged.Systems.SharedGunSystem>();
            plainGun = em.SpawnEntity("WeaponShotgunKammerer", coords);
            forgedGun = em.SpawnEntity("WeaponShotgunKammerer", new EntityCoordinates(map, new System.Numerics.Vector2(0, 5)));
            em.System<MagicPsyForgeSystem>().Forge(forgedGun, 0.25f, false);

            // The shells loaded in the forged gun are forged too.
            var loaded = em.GetComponent<Content.Shared.Weapons.Ranged.Components.BallisticAmmoProviderComponent>(forgedGun).Container.ContainedEntities;
            Assert.That(loaded.Count, Is.GreaterThan(0), "the shotgun is loaded");
            Assert.That(loaded.All(r => em.HasComponent<MagicPsyForgedComponent>(r)), "every loaded shell is forged");

            // Each gun "fires" one real shotgun pellet: the same event the game raises on the gun for every shot.
            foreach (var gun in new[] { plainGun, forgedGun })
            {
                var pellet = em.SpawnEntity("PelletShotgun", em.GetComponent<TransformComponent>(gun).Coordinates);
                em.GetComponent<Content.Shared.Projectiles.ProjectileComponent>(pellet).Weapon = gun;
                em.EventBus.RaiseLocalEvent(gun, new Content.Shared.Weapons.Ranged.Events.AmmoShotEvent { FiredProjectiles = new() { pellet } });
            }

            var query = em.EntityQueryEnumerator<Content.Shared.Projectiles.ProjectileComponent>();
            while (query.MoveNext(out _, out var projectile))
            {
                if (projectile.Weapon == plainGun)
                    plain += projectile.Damage.GetTotal().Float();
                else if (projectile.Weapon == forgedGun)
                    forged += projectile.Damage.GetTotal().Float();
            }
            Assert.That(plain, Is.GreaterThan(0f), "the plain shotgun fired");
            // Laser pistol: its beam comes from the cell. A forged cell loaded in it makes its beams 50% stronger.
            var laser = em.SpawnEntity("N14WeaponLaserMagnetoPistol", new EntityCoordinates(map, new System.Numerics.Vector2(0, 10)));
            var forge = em.System<MagicPsyForgeSystem>();
            Assert.That(forge.BeamFactor(laser), Is.EqualTo(1f).Within(0.001f), "an unforged laser is normal");
            var cell = em.System<Robust.Shared.Containers.SharedContainerSystem>().GetAllContainers(laser)
                .SelectMany(c => c.ContainedEntities).FirstOrDefault(e => forge.CanForge(e));
            if (cell != default)
            {
                forge.Forge(cell, 0.5f, true);
                Assert.That(forge.BeamFactor(laser), Is.EqualTo(1.5f).Within(0.001f), "a forged cell makes the beams 50% stronger");
            }

            // Forged gun + forged shell = the bigger bonus (+25%), not +56%.
            Assert.That(forged, Is.EqualTo(plain * 1.25f).Within(0.5f), "a forged gun's shot hits 25% harder");
        });
        await pair.CleanReturnAsync();
    }
}
