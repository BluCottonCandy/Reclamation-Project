// #Reclamation Project - Pip-Boy bio-monitor network messages
using Robust.Shared.Serialization;

namespace Content.Shared._ReclamationProject.Medical.BioMonitor;

[Serializable, NetSerializable]
public enum BioMonitorSource : byte
{
    PipBoy,
    Intelligence,
    Scanner,
}

[Serializable, NetSerializable]
public sealed class BioMonitorEmbedState
{
    public NetEntity Item;
    public string Name = string.Empty;
    public string? Prototype;
    public bool Deep;
    /// <summary>Seconds it takes the viewer to pull it out right now.</summary>
    public float Seconds;
    /// <summary>The viewer can't remove this one (deep and no hemostat in hand).</summary>
    public bool NeedsTool;
    /// <summary>0..1 chance pulling it out makes the wound bleed, for this viewer.</summary>
    public float BleedRisk;
    public bool HeavyBleed;
}

[Serializable, NetSerializable]
public enum BioMonitorChemKind : byte
{
    Other,
    Medicine,
    Drug,
    Poison,
}

[Serializable, NetSerializable]
public sealed class BioMonitorChem
{
    public string Name = string.Empty;
    public float Quantity;
    public BioMonitorChemKind Kind;
}

[Serializable, NetSerializable]
public sealed class BioMonitorPartState
{
    /// <summary>A Shitmed TargetBodyPart value (sent as a number).</summary>
    public ushort Part;
    public bool Missing;
    /// <summary>0..100</summary>
    public float Health = 100f;
    public bool Fracture;
    public bool Splinted;
    /// <summary>0..100 how far the broken bone has knitted.</summary>
    public float FractureHealing;
    /// <summary>Rough minutes until it heals on its own at the current rate (bed rest is faster).</summary>
    public int FractureMinutesLeft;
    public Dictionary<string, float> Damage = new();
    public List<BioMonitorEmbedState> Embedded = new();
}

/// <summary>Server to client: open or refresh the bio-monitor window.</summary>
[Serializable, NetSerializable]
public sealed class BioMonitorStateMessage : EntityEventArgs
{
    public NetEntity Target;
    public string TargetName = string.Empty;
    public BioMonitorSource Source;
    public bool Self;
    public bool Dead;
    public bool Critical;
    /// <summary>0..100</summary>
    public float Overall = 100f;
    /// <summary>0..100</summary>
    public float Blood = 100f;
    public float BleedRate;
    public bool InternalBleeding;
    public float? TemperatureC;
    public string Hunger = string.Empty;
    public string Thirst = string.Empty;
    public float Radiation;
    /// <summary>Radiation hitting them right now, rads per second.</summary>
    public float RadExposure;
    public bool RadAway;
    public bool RadX;
    public float Suffocation;
    public int Pulse;
    public bool ViewerHasTool;
    public bool ViewerSkilled;
    /// <summary>The viewer is Institute staff or a synth: use the Institute-styled screen.</summary>
    public bool InstituteStyle;
    /// <summary>The patient is a Gen 3 synth / Courser: stuck fragments barely hurt them.</summary>
    public bool SyntheticPatient;
    public List<BioMonitorChem> Chemicals = new();
    /// <summary>Poison damage on the body right now.</summary>
    public float PoisonDamage;
    public List<BioMonitorPartState> Parts = new();
}

/// <summary>Server to client: close the window. Client to server: I closed it.</summary>
[Serializable, NetSerializable]
public sealed class BioMonitorCloseMessage : EntityEventArgs;

/// <summary>Client to server: pull this embedded item out of the monitored body.</summary>
[Serializable, NetSerializable]
public sealed class BioMonitorRemoveRequest : EntityEventArgs
{
    public NetEntity Target;
    public NetEntity Item;
}
