using Robust.Shared.Audio;

namespace Content.Server._Misfits.Ghoul;

[RegisterComponent]
public sealed partial class FeralGhoulifyOverTimeComponent: Component
{
    /// <summary>
    /// Threshold at which every two minutes the entity may transform into a feral ghoul
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite),]
    public float FeralThreshold = 100f;
    
    /// <summary>
    /// Threshold at which every 2 minutes the entity will have severe danger warnings.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite),]
    public float DangerThreshold = 80f;
    
    /// <summary>
    /// Threshold at which the entity's examine text is modified.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite),]
    public float ExamineThreshold = 50f;
    
    /// <summary>
    /// Threshold at which the entity begins receiving warning signs every two minutes.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite),]
    public float WarningThreshold = 40f;

    /// <summary>
    /// The amount that CurrentFeral is incremented per second.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite),]
    public float FeralPerSecond = 0.06f;

    /// <summary>
    /// The chance to become a feral ghoul every two minutes after reaching FeralThreshold.
    ///
    /// This increases by 0.1 each time it fails to trigger.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite),]
    public float FeralChance = 0f;

    /// <summary>
    /// Tracker for the entity's feralization.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public float CurrentFeral = 0f;
    
    /// <summary>
    /// Used by <see cref="FeralGhoulifyOverTimeSystem"/> to track when this component should next
    /// run a warning routine for this entity.
    /// </summary>
    public TimeSpan NextWarning;

    /// <summary>
    /// Sounds to play during certain DangerThreshold notifications.
    ///
    /// Defaults to feral ghoul idle sounds.
    /// </summary>
    [DataField]
    public SoundSpecifier DangerSounds = new SoundCollectionSpecifier("N14GhoulIdle", 
        new AudioParams(-13, 1, 10, 1, false, 0));
    
    /// <summary>
    /// Sounds to play during FeralThreshold notifications.
    ///
    /// Defaults to feral ghoul aggro sounds.
    /// </summary>
    [DataField]
    public SoundSpecifier FeralSounds = new SoundCollectionSpecifier("N14GhoulAggro", 
        new AudioParams(-13, 1, 10, 1, false, 0));
}
