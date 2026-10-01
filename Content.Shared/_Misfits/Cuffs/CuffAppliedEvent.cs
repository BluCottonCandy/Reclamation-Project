// #Misfits Add: public event raised after a successful cuff is applied, so server systems can react (e.g. chat logging).
namespace Content.Shared._Misfits.Cuffs;

/// <summary>
/// Raised on the target entity after they are successfully restrained via handcuffs.
/// </summary>
[ByRefEvent]
public readonly record struct CuffAppliedEvent(EntityUid User, EntityUid Target);
