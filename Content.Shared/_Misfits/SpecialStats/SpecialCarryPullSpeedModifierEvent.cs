namespace Content.Shared._Misfits.SpecialStats;

[ByRefEvent]
public record struct SpecialCarryPullSpeedModifierEvent(EntityUid User, float Multiplier = 1f)
{
    public void ModifySpeed(float multiplier)
    {
        Multiplier *= multiplier;
    }
}
