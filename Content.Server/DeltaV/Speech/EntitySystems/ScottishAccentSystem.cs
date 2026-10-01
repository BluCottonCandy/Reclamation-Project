using Content.Server.DeltaV.Speech.Components;
using Content.Server.Speech;
using Content.Server.Speech.EntitySystems;
using System.Text.RegularExpressions;
using System.Linq;

namespace Content.Server.DeltaV.Speech.EntitySystems;

public sealed class ScottishAccentSystem : EntitySystem
{
    [Dependency]
    private readonly ReplacementAccentSystem _replacement = default!;

    private static readonly Regex RegexCh = new(@"ch", RegexOptions.IgnoreCase);
    private static readonly Regex RegexShch = new(@"shch", RegexOptions.IgnoreCase);
    private static readonly Regex RegexZh = new(@"zh", RegexOptions.IgnoreCase);
    private static readonly Regex RegexE = new(@"e", RegexOptions.IgnoreCase);
    private static readonly Regex RegexY = new(@"y", RegexOptions.IgnoreCase);
    private static readonly Regex RegexA = new(@"a", RegexOptions.IgnoreCase);

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ScottishAccentComponent, AccentGetEvent>(OnAccentGet);
    }

    public string Accentuate(string message, ScottishAccentComponent component)
    {
        var words = message.Split(' ');
        var accentuatedWords = new List<string>();

        foreach (var word in words)
        {
            // Apply dictionary replacements.
            var accentuatedWord = _replacement.ApplyReplacements(word, "scottish");

            // If the word was not replaced by the dictionary, apply regex replacements.
            if (accentuatedWord == word)
            {
                accentuatedWord = ApplyRegexReplacements(accentuatedWord);
            }

            // Add random Americanisms.
            if (Random.Shared.NextDouble() < 0.01)
            {
                accentuatedWord += " yo";
            }
            else if (Random.Shared.NextDouble() < 0.01)
            {
                accentuatedWord += " man";
            }

            accentuatedWords.Add(accentuatedWord);
        }

        return string.Join(" ", accentuatedWords);
    }

    private string ApplyRegexReplacements(string word)
    {
        word = RegexCh.Replace(word, "tsh");
        word = RegexShch.Replace(word, "sh");
        word = RegexZh.Replace(word, "dzh");
        word = RegexE.Replace(word, "'e");
        word = RegexY.Replace(word, "i");
        word = RegexA.Replace(word, "e");
        return word;
    }

    private void OnAccentGet(EntityUid uid, ScottishAccentComponent component, AccentGetEvent args)
    {
        args.Message = Accentuate(args.Message, component);
    }
}
