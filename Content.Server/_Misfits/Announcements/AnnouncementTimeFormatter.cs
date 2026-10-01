using System.Collections.Generic;

namespace Content.Server._Misfits.Announcements;

// #Misfits Change /Add/: Spell out countdown durations so wasteland announcements avoid numeric countdowns.
public static class AnnouncementTimeFormatter
{
    private static readonly string[] Ones =
    {
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine",
        "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen"
    };

    private static readonly string[] Tens =
    {
        string.Empty, string.Empty, "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety"
    };

    public static string FormatDurationWords(TimeSpan duration)
    {
        var totalSeconds = Math.Max(0, (int) Math.Ceiling(duration.TotalSeconds));
        var hours = totalSeconds / 3600;
        var minutes = totalSeconds % 3600 / 60;
        var seconds = totalSeconds % 60;

        var parts = new List<string>(3);

        if (hours > 0)
            parts.Add($"{FormatNumber(hours)} {(hours == 1 ? "hour" : "hours")}");

        if (minutes > 0)
            parts.Add($"{FormatNumber(minutes)} {(minutes == 1 ? "minute" : "minutes")}");

        if (seconds > 0 || parts.Count == 0)
            parts.Add($"{FormatNumber(seconds)} {(seconds == 1 ? "second" : "seconds")}");

        return parts.Count switch
        {
            1 => parts[0],
            2 => $"{parts[0]} and {parts[1]}",
            _ => $"{parts[0]}, {parts[1]}, and {parts[2]}"
        };
    }

    private static string FormatNumber(int value)
    {
        return value switch
        {
            < 20 => Ones[value],
            < 100 => value % 10 == 0
                ? Tens[value / 10]
                : $"{Tens[value / 10]}-{Ones[value % 10]}",
            < 1000 => value % 100 == 0
                ? $"{Ones[value / 100]} hundred"
                : $"{Ones[value / 100]} hundred {FormatNumber(value % 100)}",
            < 1_000_000 => value % 1000 == 0
                ? $"{FormatNumber(value / 1000)} thousand"
                : $"{FormatNumber(value / 1000)} thousand {FormatNumber(value % 1000)}",
            _ => value.ToString()
        };
    }
}