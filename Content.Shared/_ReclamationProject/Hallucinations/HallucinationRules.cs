namespace Content.Shared._ReclamationProject.Hallucinations;

public enum HallucinationKind : byte { Enemy, Food, Drink }

/// <summary>Encounter policy shared with regression tests; rolls are uniformly distributed in [0, 1).</summary>
public static class HallucinationRules
{
    public const int MinimumMinutes = 5;
    public const int MaximumMinutes = 20;
    public static bool Occurs(bool high, float roll) => high || roll < 0.8f;
    public static readonly TimeSpan WeatherDuration = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan AppetiteCooldown = TimeSpan.FromMinutes(10);
}
