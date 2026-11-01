using System;
using System.Collections.Generic;
using System.Linq;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.Client._Misfits.Holotape;

/// <summary>A transient, original text adventure. Closing the holotape discards the party.</summary>
public sealed partial class WastelandQuestControl : BoxContainer, IHolotapeGame
{
    public event Action? OnQuit;
    public event Action<string>? OnStatus;
    private sealed record Companion(string Name, string Role, string Description, int Health, int Focus, int Attack);
    private static readonly Companion[] Cast =
    {
        new("Mira", "Archer", "A mysterious woman who answers questions with a knowing smile. Her arrows rarely miss.", 32, 10, 8),
        new("Bram", "Barbarian", "An outgoing, blunt man. He laughs loudly, speaks plainly, and prefers problems he can hit.", 46, 8, 10),
        new("Vey", "Mage", "A snobby scholar who finds the rest of the world disappointingly uneducated. Their spells are formidable.", 28, 18, 5),
        new("Rook", "Thief", "A shady figure with quick hands, quiet footsteps, and an explanation for every missing coin.", 34, 12, 8),
        new("Elena", "Warrior", "A straightforward, kind woman. She believes a strong shield is worth little unless it protects someone.", 42, 12, 8),
    };
    private sealed class Hero(Companion companion)
    {
        public readonly Companion Companion = companion;
        public int Level = 1, Experience;
        public int Health = companion.Health, Focus = companion.Focus;
        public bool Guarding;
        public int MaxHealth => Companion.Health + (Level - 1) * 6;
        public int MaxFocus => Companion.Focus + (Level - 1) * 2;
        public int Attack => Companion.Attack + (Level - 1) * 2;
    }
    private sealed class Enemy(string name, int health, int attack, int reward)
    {
        public readonly string Name = name;
        public int Health = health;
        public readonly int Attack = attack, Reward = reward;
        public bool Weakened;
    }
    private readonly Random _random = new();
    private readonly List<Hero> _party = new();
    private readonly List<Enemy> _enemies = new();
    private readonly HashSet<int> _chosen = new();
    private readonly HashSet<string> _seals = new();
    private readonly HashSet<string> _searched = new();
    private readonly Queue<string> _history = new();
    private readonly RichTextLabel _partyLabel = new() { HorizontalExpand = true };
    private readonly RichTextLabel _story = new() { HorizontalExpand = true };
    private readonly ScrollContainer _scroll = new() { VerticalExpand = true, HScrollEnabled = false };
    private readonly BoxContainer _choices = new() { Orientation = LayoutOrientation.Vertical, SeparationOverride = 3 };
    private readonly Label _heading = new() { FontColorOverride = Color.FromHex("#8fff8f") };
    private string _place = "tavern", _battleReturn = "forest", _battleSeal = "";
    private int _day = 1, _gold = 15, _potions = 3, _turn, _target, _reward, _battles, _scrollFrames;
    private bool _started, _battle, _victory;

    public WastelandQuestControl()
    {
        Orientation = LayoutOrientation.Vertical;
        SeparationOverride = 6;
        CanKeyboardFocus = true;
        KeyboardFocusOnClick = true;
        MouseFilter = MouseFilterMode.Stop;
        AddChild(_heading);
        AddChild(_partyLabel);
        _scroll.AddChild(_story);
        AddChild(_scroll);
        AddChild(_choices);
    }

    public void Restart()
    {
        _party.Clear(); _enemies.Clear(); _chosen.Clear(); _seals.Clear(); _searched.Clear(); _history.Clear();
        _day = 1; _gold = 15; _potions = 3; _battles = 0; _started = _battle = _victory = false; _place = "tavern";
        Write("THE BELLS OF ASHVALE\nRain taps the windows of the Lantern Tavern. For three nights, a bell has rung from the abandoned watchtower. Since then the forest has grown restless and the lake has turned cold and black. The town needs two people brave enough to investigate.");
        Render();
    }
    private void Write(string text)
    {
        _history.Enqueue(text);
        while (_history.Count > 45) _history.Dequeue();
        _story.SetMessage(StoryText(string.Join("\n\n", _history)));
        _scrollFrames = 2;
    }
    private static FormattedMessage StoryText(string text)
    {
        var message = new FormattedMessage();
        message.PushColor(Color.FromHex("#71e871"));
        message.AddText(text);
        message.Pop();
        return message;
    }
    protected override void FrameUpdate(Robust.Shared.Timing.FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (_scrollFrames > 0) { _scrollFrames--; _scroll.VScrollTarget = float.MaxValue; }
    }
    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        if (args.Function == EngineKeyFunctions.CloseModals || args.Function == EngineKeyFunctions.TextReleaseFocus)
        { args.Handle(); OnQuit?.Invoke(); return; }
        base.KeyBindDown(args);
    }
    private void Choice(string text, Action action, bool enabled = true)
    {
        var button = new Button { Text = text, HorizontalExpand = true, MinHeight = 30, Disabled = !enabled };
        button.OnPressed += _ => action();
        _choices.AddChild(button);
    }
    private void Render()
    {
        _choices.RemoveAllChildren();
        _heading.Text = _battle ? "BATTLE" : !_started ? "CHOOSE TWO COMPANIONS" : _place.ToUpperInvariant();
        _partyLabel.SetMessage(StoryText(string.Join("\n", _party.Select(h =>
            $"{h.Companion.Name}, {h.Companion.Role} | Level {h.Level} | HP {h.Health}/{h.MaxHealth} | Focus {h.Focus}/{h.MaxFocus} | XP {h.Experience}/{h.Level * 25}"))));
        OnStatus?.Invoke($"WASTELAND QUEST | DAY {_day} | GOLD {_gold} | POTIONS {_potions} | SEALS {_seals.Count}/3");
        if (!_started) { RenderSelection(); return; }
        if (_battle) { RenderBattle(); return; }
        if (_victory)
        {
            Choice("Begin a new adventure", Restart);
            Choice("Close the game", () => OnQuit?.Invoke());
            return;
        }
        switch (_place)
        {
            case "tavern":
                Choice("Leave the tavern for the town square", () => Visit("town"));
                Choice("Go to bed for the night - fully heal and restore focus", Rest);
                Choice("Talk with your companions", Talk);
                break;
            case "town":
                Choice("Leave town", () => Visit("crossroads"));
                Choice("Return to the tavern", () => Visit("tavern"));
                Choice("Buy a healing potion - 10 gold", BuyPotion, _gold >= 10);
                Choice("Ask the elder about the bell", () => { Write("The elder explains: three warding seals once kept a shadow beneath the tower. Seek the forest shrine, the lake grotto, and the old quarry. Bring back their seals before entering the tower."); Render(); });
                break;
            case "crossroads":
                Choice("Enter the whispering forest", () => Visit("forest"));
                Choice("Follow the road to the lake", () => Visit("lake"));
                Choice("Climb into the hills", () => Visit("hills"));
                Choice("Approach the abandoned watchtower", () => Visit("tower"));
                Choice("Return to town", () => Visit("town"));
                break;
            case "forest":
                Choice("Explore the old hunting trail", () => Encounter("forest"));
                Choice("Search the herb glade", Search);
                Choice(_seals.Contains("forest") ? "The forest shrine is quiet" : "Challenge the thorn guardian at the shrine", () => Encounter("forest", true), !_seals.Contains("forest"));
                Choice("Return to the crossroads", () => Visit("crossroads"));
                break;
            case "lake":
                Choice("Explore the misty shoreline", () => Encounter("lake"));
                Choice("Search the abandoned fishing huts", Search);
                Choice(_seals.Contains("lake") ? "The lake seal is yours" : "Enter the flooded grotto", () => Encounter("lake", true), !_seals.Contains("lake"));
                Choice("Return to the crossroads", () => Visit("crossroads"));
                break;
            case "hills":
                Choice("Explore the ruined caravan camp", () => Encounter("hills"));
                Choice("Search the hillside ruins", Search);
                Choice(_seals.Contains("hills") ? "The quarry stands silent" : "Descend into the old quarry", () => Encounter("hills", true), !_seals.Contains("hills"));
                Choice("Return to the crossroads", () => Visit("crossroads"));
                break;
            case "tower":
                Choice(_seals.Count == 3 ? "Use the three seals and confront the bell keeper" : "The tower door needs all three seals", () => Encounter("tower", true), _seals.Count == 3);
                Choice("Return to the crossroads", () => Visit("crossroads"));
                break;
        }
        Choice("Read your journal", Journal);
        Choice("Close the game", () => OnQuit?.Invoke());
    }
    private void RenderSelection()
    {
        for (var i = 0; i < Cast.Length; i++)
        {
            var index = i; var person = Cast[i];
            Choice($"{(_chosen.Contains(i) ? "[Chosen] " : "")}{person.Name} - {person.Role}", () =>
            {
                if (!_chosen.Remove(index) && _chosen.Count < 2) _chosen.Add(index);
                Write(person.Name + ": " + person.Description);
                Render();
            });
        }
        Choice("Set out with these two companions", Begin, _chosen.Count == 2);
        Choice("Close the game", () => OnQuit?.Invoke());
    }
    private void Begin()
    {
        if (_started || _chosen.Count != 2) return;
        foreach (var i in _chosen.OrderBy(i => i)) _party.Add(new Hero(Cast[i]));
        _started = true;
        Write($"{_party[0].Companion.Name} and {_party[1].Companion.Name} agree to travel together. The innkeeper gives them three potions. 'Beds are free while you help the town,' she says. 'Come back alive.'");
        Visit("tavern");
    }
    private void Visit(string place)
    {
        _place = place;
        Write(place switch
        {
            "tavern" => "The Lantern Tavern smells of bread and woodsmoke. A warm room waits upstairs.",
            "town" => "Ashvale's square is nearly empty. A merchant packs supplies beside the elder's weathered bench.",
            "crossroads" => "Beyond the gate, three roads lead to the forest, the lake, and the hills. The watchtower rises above them all.",
            "forest" => "Pale threads hang between the trees. Far away, something answers the tower's bell.",
            "lake" => "Black water laps against broken boats. A path of wet footprints leads toward a grotto.",
            "hills" => "Old wagon wheels lie among the stones. Beneath the quarry, a heavy footstep echoes.",
            _ => _seals.Count == 3 ? "The three seals glow. The tower door opens, and the bell falls silent for the first time in days." : "Three empty sockets mark the sealed tower door. The wind carries a voice: 'Restore what was broken.'",
        });
        Render();
    }
    private void Rest()
    {
        if (_battle || !_started) return;
        foreach (var h in _party) { h.Health = h.MaxHealth; h.Focus = h.MaxFocus; h.Guarding = false; }
        _day++; _searched.Clear();
        Write("You sleep through the rain. By morning, both companions are fully healed and their focus is restored. Somewhere in the distance, the bell rings again.");
        Render();
    }
    private void BuyPotion()
    {
        if (_gold < 10) return;
        _gold -= 10; _potions++; Write("The merchant hands over a healing potion. It restores 24 health and 4 focus to one companion."); Render();
    }
    private void Talk()
    {
        var h = _party[_random.Next(_party.Count)];
        Write(h.Companion.Role switch
        {
            "Archer" => "Mira watches the rain. 'I have heard that bell before.' Asked where, she simply smiles.",
            "Barbarian" => "Bram raises his mug. 'Bad bell. Big tower. We go up, break the bad thing, come home. Excellent plan!'",
            "Mage" => "Vey sighs. 'An elementary binding ritual, apparently maintained by people who could not read the instructions.'",
            "Thief" => "Rook rolls a coin across their knuckles. 'If anyone asks, the abandoned tower was already missing its silver.'",
            _ => "Elena checks the straps of her shield. 'The frightened people here need us. We take it one road at a time.'",
        }); Render();
    }
    private void Search()
    {
        if (!_searched.Add(_place)) { Write("You have searched here today. Rest at the tavern before returning."); Render(); return; }
        if (_random.Next(2) == 0) { _potions++; Write("Among the abandoned supplies, you find a sealed healing potion."); }
        else { var coins = _random.Next(6, 15); _gold += coins; Write($"Careful searching reveals a forgotten purse holding {coins} gold."); }
        Render();
    }
    private void Journal()
    {
        Write("JOURNAL\nRecover the warding seals from the forest shrine, lake grotto, and hill quarry. Then silence the watchtower's bell.\n" +
            string.Join(" | ", new[] { "forest", "lake", "hills" }.Select(s => s + (_seals.Contains(s) ? ": recovered" : ": missing"))) +
            $"\nEnemy groups defeated: {_battles}. Each victory grants both companions experience. Resting restores health and focus; a fallen companion rises with 1 health after a victory. A defeated party wakes at the inn and loses half its gold."); Render();
    }
    private void Encounter(string region, bool guardian = false)
    {
        if (_battle || !_started || guardian && _seals.Contains(region)) return;
        if (region == "tower" && _seals.Count != 3) return;
        _battleReturn = region; _battleSeal = guardian ? region : ""; _enemies.Clear(); _turn = _target = 0;
        var level = Math.Max(1, (int)_party.Average(h => h.Level));
        var names = region switch
        {
            "forest" => new[] { "Hungry Wolf", "Briar Bandit", "Restless Root" },
            "lake" => new[] { "Marsh Stalker", "Drowned Marauder", "Giant Leech" },
            "hills" => new[] { "Road Brigand", "Cave Spider", "Stone Imp" },
            _ => new[] { "Hollow Sentry", "Bellbound Shade", "Silent Watcher" },
        };
        var count = guardian ? 2 : _random.Next(2, 4);
        for (var i = 0; i < count; i++) _enemies.Add(new Enemy(names[_random.Next(names.Length)], 8 + level * 4, 2 + level, 8 + level * 3));
        if (guardian) _enemies.Add(new Enemy(region switch { "forest" => "Thorn Guardian", "lake" => "Keeper of the Deep", "hills" => "Quarry Troll", _ => "The Bell Keeper" },
            (region == "tower" ? 42 : 24) + level * 6, 4 + level * 2, 25 + level * 8));
        _reward = _enemies.Sum(e => e.Reward); _battle = true;
        foreach (var h in _party) h.Guarding = false;
        Write("ENCOUNTER\n" + string.Join(", ", _enemies.Select(e => e.Name)) + " block the way. Choose a target, then an action for each companion.");
        Render();
    }
    private void RenderBattle()
    {
        var living = _enemies.Select((e, i) => (Enemy: e, Index: i)).Where(e => e.Enemy.Health > 0).ToArray();
        if (living.Length == 0) return;
        if (_enemies[_target].Health <= 0) _target = living[0].Index;
        _heading.Text = "BATTLE - " + _party[_turn].Companion.Name + "'S TURN";
        foreach (var entry in living)
        {
            var index = entry.Index;
            Choice($"{(_target == index ? "> " : "")}{entry.Enemy.Name} - HP {entry.Enemy.Health}", () => { _target = index; Render(); });
        }
        Choice("Strike the selected enemy", () => Act("strike"));
        Choice(SkillName(_party[_turn]) + " - 4 focus", () => Act("skill"), _party[_turn].Focus >= 4);
        Choice("Guard - reduce incoming damage and recover 2 focus", () => Act("guard"));
        Choice("Drink a potion - heal the acting companion", () => Act("potion"), _potions > 0);
        Choice("Try to retreat", Retreat);
    }
    private static string SkillName(Hero hero) => hero.Companion.Role switch
    { "Archer" => "Piercing Shot", "Barbarian" => "Sweeping Strike", "Mage" => "Arcane Storm", "Thief" => "Dirty Trick", _ => "Rallying Shield" };
    private void Act(string action)
    {
        if (!_battle || _party[_turn].Health <= 0) return;
        var hero = _party[_turn]; var target = _enemies[_target];
        if (target.Health <= 0) return;
        switch (action)
        {
            case "strike": Hit(target, hero.Attack + _random.Next(4), hero.Companion.Name); break;
            case "skill":
                if (hero.Focus < 4) return;
                hero.Focus -= 4;
                if (hero.Companion.Role is "Mage" or "Barbarian")
                    foreach (var enemy in _enemies.Where(e => e.Health > 0)) Hit(enemy, hero.Attack + 3 + hero.Level, SkillName(hero));
                else if (hero.Companion.Role == "Warrior")
                {
                    foreach (var ally in _party.Where(h => h.Health > 0)) { ally.Guarding = true; ally.Health = Math.Min(ally.MaxHealth, ally.Health + 4 + hero.Level); }
                    Write("Elena raises her shield. Both companions regain a little health and brace for the next enemy attack.");
                }
                else { Hit(target, hero.Attack + 7 + hero.Level, SkillName(hero)); if (hero.Companion.Role == "Thief") target.Weakened = true; }
                break;
            case "guard": hero.Guarding = true; hero.Focus = Math.Min(hero.MaxFocus, hero.Focus + 2); Write(hero.Companion.Name + " takes a defensive stance and regains focus."); break;
            case "potion":
                if (_potions <= 0) return;
                _potions--; hero.Health = Math.Min(hero.MaxHealth, hero.Health + 24); hero.Focus = Math.Min(hero.MaxFocus, hero.Focus + 4); Write(hero.Companion.Name + " drinks a healing potion."); break;
            default: return;
        }
        if (_enemies.All(e => e.Health <= 0)) { WinBattle(); return; }
        _turn++;
        while (_turn < _party.Count && _party[_turn].Health <= 0) _turn++;
        if (_turn >= _party.Count) EnemyTurn();
        Render();
    }
    private void Hit(Enemy enemy, int damage, string source)
    { enemy.Health = Math.Max(0, enemy.Health - damage); Write($"{source} hits {enemy.Name} for {damage}." + (enemy.Health == 0 ? " Defeated!" : "")); }
    private void EnemyTurn()
    {
        foreach (var enemy in _enemies.Where(e => e.Health > 0))
        {
            var alive = _party.Where(h => h.Health > 0).ToArray();
            if (alive.Length == 0) break;
            var target = alive[_random.Next(alive.Length)];
            var damage = enemy.Attack + _random.Next(3);
            if (target.Guarding) damage = Math.Max(1, damage / 2);
            if (enemy.Weakened) { damage = Math.Max(1, damage / 2); enemy.Weakened = false; }
            target.Health = Math.Max(0, target.Health - damage);
            Write($"{enemy.Name} hits {target.Companion.Name} for {damage}." + (target.Health == 0 ? " They fall unconscious." : ""));
        }
        foreach (var h in _party) h.Guarding = false;
        if (_party.All(h => h.Health <= 0))
        {
            _battle = false; _gold /= 2; _place = "tavern";
            foreach (var h in _party) h.Health = Math.Max(1, h.MaxHealth / 2);
            Write("A passing patrol brings you back to the inn. Half your gold is missing, but you are alive. Rest upstairs before trying again.");
        }
        _turn = Math.Max(0, _party.FindIndex(h => h.Health > 0));
    }
    private void Retreat()
    {
        if (!_battle) return;
        if (_random.Next(100) < 65)
        { _battle = false; Write("You escape along the road. No experience or treasure is gained."); Visit(_battleReturn); }
        else { Write("The enemies cut off your retreat!"); EnemyTurn(); Render(); }
    }
    private void WinBattle()
    {
        _battle = false; _battles++; var gold = _random.Next(7, 18) + (_battleSeal.Length > 0 ? 12 : 0); _gold += gold;
        Write($"VICTORY! Each companion gains {_reward} experience. You find {gold} gold.");
        foreach (var h in _party)
        {
            h.Experience += _reward;
            while (h.Level < 10 && h.Experience >= h.Level * 25)
            { h.Experience -= h.Level * 25; h.Level++; h.Health = Math.Min(h.MaxHealth, h.Health + 12); h.Focus = h.MaxFocus; Write($"{h.Companion.Name} reaches level {h.Level}! Health, focus, and attack improve."); }
            if (h.Level == 10) h.Experience = Math.Min(h.Experience, h.Level * 25);
            h.Health = Math.Max(1, h.Health);
        }
        if (_battleSeal == "tower")
        {
            _victory = true; _place = "epilogue";
            Write("The Bell Keeper dissolves into dawn light. The three seals mend the tower's ancient wards. Birds return to the forest, clear water reaches the lake, and Ashvale's doors open once more. At the Lantern Tavern, two chairs will always be waiting for its heroes.\nTHE END"); Render(); return;
        }
        if (_battleSeal.Length > 0) { _seals.Add(_battleSeal); Write("You recover the " + _battleSeal + " warding seal. The tower's bell sounds weaker."); }
        Visit(_battleReturn);
    }
}
