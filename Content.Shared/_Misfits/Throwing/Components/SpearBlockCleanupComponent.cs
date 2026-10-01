// #Misfits Change Add: Internal server-side marker. Added alongside ThrownItemImmuneComponent to
// signal SpearBlockSystem.Update() to clean up the temporary immunity on the next tick.
namespace Content.Shared._Misfits.Throwing.Components;

[RegisterComponent]
public sealed partial class SpearBlockCleanupComponent : Component
{
}
