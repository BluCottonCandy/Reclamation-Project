using Robust.Shared.Serialization;

namespace Content.Shared._ReclamationProject.Triggermen;

[Serializable, NetSerializable]
public enum RouletteKey { Key }

[Serializable, NetSerializable]
public enum RouletteBetKind : byte
{
    Red, Black, Odd, Even, Low, High, FirstDozen, SecondDozen, ThirdDozen, Number
}

[Serializable, NetSerializable]
public sealed class RouletteBet(RouletteBetKind kind, int number, int amount) : BoundUserInterfaceMessage
{
    public RouletteBetKind Kind => kind;
    public int Number => number;
    public int Amount => amount;
}

[Serializable, NetSerializable]
public sealed class RouletteState(int spinId, bool spinning, int result, string status, int[] history) : BoundUserInterfaceState
{
    public int SpinId => spinId;
    public bool Spinning => spinning;
    public int Result => result;
    public string Status => status;
    public int[] History => history;
}

/// <summary>
/// Fixed, public paytable. Every pocket is equally likely. Returns include the
/// stake: each legal bet returns 36/38 on average, leaving a 2/38 house edge.
/// Pocket 37 represents 00; no result is adjusted based on the player's wager.
/// </summary>
public static class RouletteRules
{
    public const int PocketCount = 38;
    public const int DoubleZero = 37;
    public const float SpinSeconds = 4;
    public static readonly int[] WheelOrder =
        [0, 28, 9, 26, 30, 11, 7, 20, 32, 17, 5, 22, 34, 15, 3, 24, 36, 13, 1,
         37, 27, 10, 25, 29, 12, 8, 19, 31, 18, 6, 21, 33, 16, 4, 23, 35, 14, 2];

    public static bool IsRed(int number)
        => number is 1 or 3 or 5 or 7 or 9 or 12 or 14 or 16 or 18 or
            19 or 21 or 23 or 25 or 27 or 30 or 32 or 34 or 36;

    public static string FormatNumber(int number) => number == DoubleZero ? "00" : number.ToString();

    public static int MaxBet(RouletteBetKind kind) => kind switch
    {
        RouletteBetKind.Red or RouletteBetKind.Black or RouletteBetKind.Odd or
            RouletteBetKind.Even or RouletteBetKind.Low or RouletteBetKind.High => 10,
        RouletteBetKind.FirstDozen or RouletteBetKind.SecondDozen or RouletteBetKind.ThirdDozen => 5,
        RouletteBetKind.Number => 1,
        _ => 0
    };

    public static int Multiplier(RouletteBetKind kind) => kind switch
    {
        RouletteBetKind.Red or RouletteBetKind.Black or RouletteBetKind.Odd or
            RouletteBetKind.Even or RouletteBetKind.Low or RouletteBetKind.High => 2,
        RouletteBetKind.FirstDozen or RouletteBetKind.SecondDozen or RouletteBetKind.ThirdDozen => 3,
        RouletteBetKind.Number => 36,
        _ => 0
    };

    public static bool IsValid(RouletteBetKind kind, int number, int amount)
        => amount >= 1 && amount <= MaxBet(kind) &&
           (kind != RouletteBetKind.Number || number is >= 0 and < PocketCount);

    public static bool Wins(RouletteBetKind kind, int choice, int result)
    {
        if (result is < 0 or >= PocketCount)
            return false;
        if (kind == RouletteBetKind.Number)
            return choice == result;
        // Neither green pocket is even, low, black, or part of a dozen.
        if (result is 0 or DoubleZero)
            return false;
        return kind switch
        {
            RouletteBetKind.Red => IsRed(result),
            RouletteBetKind.Black => !IsRed(result),
            RouletteBetKind.Odd => result % 2 == 1,
            RouletteBetKind.Even => result % 2 == 0,
            RouletteBetKind.Low => result <= 18,
            RouletteBetKind.High => result >= 19,
            RouletteBetKind.FirstDozen => result <= 12,
            RouletteBetKind.SecondDozen => result is >= 13 and <= 24,
            RouletteBetKind.ThirdDozen => result >= 25,
            _ => false
        };
    }

    public static string BetName(RouletteBetKind kind, int number) => kind switch
    {
        RouletteBetKind.Red => "Red",
        RouletteBetKind.Black => "Black",
        RouletteBetKind.Odd => "Odd",
        RouletteBetKind.Even => "Even",
        RouletteBetKind.Low => "1–18",
        RouletteBetKind.High => "19–36",
        RouletteBetKind.FirstDozen => "1–12",
        RouletteBetKind.SecondDozen => "13–24",
        RouletteBetKind.ThirdDozen => "25–36",
        RouletteBetKind.Number => FormatNumber(number),
        _ => "Invalid bet"
    };
}
