using System.Linq;
using System.Text;
using Content.Shared._ReclamationProject.Magic;
using Content.Shared.GameTicking;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._ReclamationProject.Magic;

/// <summary>
/// #Reclamation Project - makes this round's secret Arcane words and turns written text into spells.
/// Every base word gets a new random written form each round, and every merge gets one random
/// "true" word order (writing it in that order makes the spell Empowered).
/// The words are never sent to clients.
/// </summary>
public sealed class MagicWordsSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    private static readonly string[] Syllables =
    {
        "VOR", "AKH", "TEL", "UN", "MIR", "EN", "SKA", "RRA", "ORR", "HAL", "KOR", "VEN", "UMB", "RAX",
        "THA", "ZUL", "NEH", "ISK", "ARU", "GOR", "DRA", "MOL", "QUE", "SAR", "ITH", "BEL", "OKU", "ZEN",
        "FAR", "LUM", "NOX", "VEX", "KAL", "RUN", "SOL", "TAU", "YRR", "ESH", "OVA", "GRI",
    };

    private readonly Dictionary<string, string> _wordText = new();      // word id -> written form
    private readonly Dictionary<string, string> _textWord = new();      // written form -> word id
    private readonly Dictionary<string, List<string>> _trueOrder = new(); // spell id -> true word order
    private readonly Dictionary<string, List<string>> _spellsByKey = new(); // sorted word key -> spell ids
    private readonly Dictionary<string, string> _scrambled = new();     // word id -> its letters shuffled (Codex)

    public int CipherShift { get; private set; }

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => Clear());
        _proto.PrototypesReloaded += _ => Clear();
    }

    private void Clear()
    {
        _wordText.Clear();
        _textWord.Clear();
        _trueOrder.Clear();
        _spellsByKey.Clear();
        _scrambled.Clear();
    }

    private void EnsureGenerated()
    {
        if (_wordText.Count > 0)
            return;

        foreach (var word in _proto.EnumeratePrototypes<MagicWordPrototype>().OrderBy(w => w.ID))
        {
            string text;
            var tries = 0;
            do
            {
                var sb = new StringBuilder();
                var count = _random.Next(2, 4);
                for (var i = 0; i < count; i++)
                    sb.Append(_random.Pick(Syllables));
                text = sb.ToString();
                tries++;
            } while ((_textWord.ContainsKey(text) || text.Length > 9) && tries < 200);

            _wordText[word.ID] = text;
            _textWord[text] = word.ID;
        }

        foreach (var spell in _proto.EnumeratePrototypes<MagicSpellPrototype>())
        {
            var order = spell.Words.Select(w => w.Id).ToList();
            _random.Shuffle(order);
            _trueOrder[spell.ID] = order;

            var key = Key(spell.Words.Select(w => w.Id));
            if (!_spellsByKey.TryGetValue(key, out var list))
                _spellsByKey[key] = list = new List<string>();
            list.Add(spell.ID);
        }

        CipherShift = _random.Next(1, 26);

        // The Codex shows every word with its runes shuffled (never in the right order).
        foreach (var (id, text) in _wordText)
        {
            var letters = text.ToCharArray();
            for (var tries = 0; tries < 20; tries++)
            {
                _random.Shuffle(letters);
                if (new string(letters) != text)
                    break;
            }
            _scrambled[id] = new string(letters);
        }
    }

    private static string Key(IEnumerable<string> words) => string.Join("|", words.OrderBy(w => w));

    private static List<string> Tokens(string text)
    {
        var upper = text.ToUpperInvariant();
        var sb = new StringBuilder();
        foreach (var c in upper)
            sb.Append(char.IsLetter(c) ? c : ' ');
        return sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    /// <summary>Turns written text into a spell. False if any word is wrong (misspelled counts as wrong).</summary>
    public bool TryResolve(string text, out ProtoId<MagicSpellPrototype> spell, out bool empowered)
    {
        EnsureGenerated();
        spell = default;
        empowered = false;

        var tokens = Tokens(text);
        if (tokens.Count is 0 or > 4)
            return false;

        var ids = new List<string>();
        foreach (var token in tokens)
        {
            if (!_textWord.TryGetValue(token, out var id))
                return false;
            ids.Add(id);
        }

        if (!_spellsByKey.TryGetValue(Key(ids), out var spells) || spells.Count == 0)
            return false;

        spell = spells[0];
        empowered = ids.Count > 1 && _trueOrder.TryGetValue(spell.Id, out var order) && order.SequenceEqual(ids);
        return true;
    }

    /// <summary>
    /// Like <see cref="TryResolve"/> but forgiving: each word may be up to 2 letters off.
    /// <paramref name="exact"/> is false when any word was misspelled (used for typed-casting misfires).
    /// </summary>
    public bool TryResolveFuzzy(string text, out ProtoId<MagicSpellPrototype> spell, out bool exact)
    {
        EnsureGenerated();
        spell = default;
        exact = true;

        var tokens = Tokens(text);
        if (tokens.Count is 0 or > 4)
            return false;

        var ids = new List<string>();
        foreach (var token in tokens)
        {
            if (_textWord.TryGetValue(token, out var id))
            {
                ids.Add(id);
                continue;
            }

            string? best = null;
            var bestDist = int.MaxValue;
            var tie = false;
            foreach (var (written, wordId) in _textWord)
            {
                var d = Distance(token, written);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = wordId;
                    tie = false;
                }
                else if (d == bestDist)
                    tie = true;
            }

            if (best == null || tie || bestDist > 2 || token.Length < 3)
                return false;
            ids.Add(best);
            exact = false;
        }

        if (!_spellsByKey.TryGetValue(Key(ids), out var spells) || spells.Count == 0)
            return false;
        spell = spells[0];
        return true;
    }

    private static int Distance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
        for (var j = 1; j <= b.Length; j++)
            d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return d[a.Length, b.Length];
    }

    public string GetWordText(ProtoId<MagicWordPrototype> word)
    {
        EnsureGenerated();
        return _wordText.GetValueOrDefault(word.Id, "?");
    }

    /// <summary>The written form of a spell in its true (Empowered) order.</summary>
    public string GetTrueText(MagicSpellPrototype spell)
    {
        EnsureGenerated();
        var order = _trueOrder.GetValueOrDefault(spell.ID) ?? spell.Words.Select(w => w.Id).ToList();
        return string.Join(" ", order.Select(w => _wordText.GetValueOrDefault(w, "?")));
    }

    /// <summary>Codex entries for one reader: hints for words they haven't solved, names and plain words for ones they have.</summary>
    public List<MagicCodexEntry> CodexEntries(EntityUid? reader)
    {
        TryComp<MagicCasterComponent>(reader, out var caster);
        var list = new List<MagicCodexEntry>();
        foreach (var word in _proto.EnumeratePrototypes<MagicWordPrototype>().OrderBy(w => w.ID))
        {
            var plain = GetWordText(word.ID);
            var solved = caster != null && caster.MagicSolvedWords.Contains(word.ID);
            list.Add(new MagicCodexEntry
            {
                Solved = solved,
                Name = solved ? Loc.GetString(word.Name) : string.Empty,
                Plain = solved ? plain : string.Empty,
                Hint = word.Hint is { } hint ? Loc.GetString(hint) : "...",
                Scrambled = GetScrambled(word.ID),
                First = plain.Length > 0 ? plain[0] : '?',
                Class = word.Class,
            });
        }
        return list;
    }

    /// <summary>The word's runes in this round's shuffled order, as shown in the Codex.</summary>
    public string GetScrambled(ProtoId<MagicWordPrototype> word)
    {
        EnsureGenerated();
        return _scrambled.GetValueOrDefault(word.Id, "?");
    }

    /// <summary>Letter-shift cipher (kept for admin tools).</summary>
    public string Encode(string plain)
    {
        EnsureGenerated();
        var sb = new StringBuilder();
        foreach (var c in plain)
            sb.Append(c is >= 'A' and <= 'Z' ? (char) ('A' + (c - 'A' + CipherShift) % 26) : c);
        return sb.ToString();
    }
}
