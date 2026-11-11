using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._ReclamationProject.Lockpick;

// #Reclamation Project - lockpicking minigame for locked doors without power.
// The server owns the lock (pin order, lengths, types) and checks every pin the client says it set.
// The client only does the feel: the pick, tension, springs and sounds.

/// <summary>A lockpick set. Each snapped pick uses one up; the set is gone when it's empty.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class LockpickComponent : Component
{
    [DataField, AutoNetworkedField]
    public int Picks = 5;

    /// <summary>How far from the door the picker can stand.</summary>
    [DataField]
    public float Range = 1.5f;
}

/// <summary>Optional on a door: overrides how hard its lock is. Without it, wooden doors are 3 plain pins, others 5 pins with 1 spool and 1 serrated.</summary>
[RegisterComponent]
public sealed partial class LockpickDifficultyComponent : Component
{
    [DataField]
    public int Pins = 5;

    [DataField]
    public int Spools = 1;

    [DataField]
    public int Serrated = 1;
}

[Serializable, NetSerializable]
public enum LockpickUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public enum LockPinType : byte
{
    Plain,
    /// <summary>Thin-waisted driver: the first set is a false set; needs light tension and a second lift.</summary>
    Spool,
    /// <summary>Ridged driver: clicks on every ridge, stays where it's left, narrower set band.</summary>
    Serrated,
}

[Serializable, NetSerializable]
public enum LockpickTension : byte
{
    Light,
    Right,
    Heavy,
}

[Serializable, NetSerializable]
public sealed class LockPinState
{
    /// <summary>Key pin length in lock pixels.</summary>
    public float Length;
    public LockPinType Type;
    public bool Set;
    public bool Jammed;
    /// <summary>Spool pin caught in a false set.</summary>
    public bool FalseSet;
}

[Serializable, NetSerializable]
public sealed class LockpickBuiState : BoundUserInterfaceState
{
    public List<LockPinState> Pins = new();
    public int Picks;
    public int Agility;
    /// <summary>Perception 8+: the binding pin's index, outlined. Null otherwise.</summary>
    public int? Binding;
    /// <summary>The binding pin's index, only used for how it feels under the pick (stiff). Not shown.</summary>
    public int Stiff = -1;
    public bool Done;
    public string? Message;
    /// <summary>Bumped on every server answer, so the client knows to play a click.</summary>
    public int Clicks;
}

/// <summary>Client -> server: let go of a pin at this lift (pixels above rest).</summary>
[Serializable, NetSerializable]
public sealed class LockpickReleaseMessage : BoundUserInterfaceMessage
{
    public int Pin;
    public float Lift;
    public LockpickTension Tension;
}

/// <summary>Client -> server: eased off the wrench, every pin drops.</summary>
[Serializable, NetSerializable]
public sealed class LockpickResetMessage : BoundUserInterfaceMessage;

/// <summary>Client -> server: too much tension snapped a pick.</summary>
[Serializable, NetSerializable]
public sealed class LockpickSnapMessage : BoundUserInterfaceMessage;

/// <summary>Client -> server: tension was too light for too long, the last set pin slipped.</summary>
[Serializable, NetSerializable]
public sealed class LockpickSlipMessage : BoundUserInterfaceMessage;

/// <summary>Lock geometry shared by the client drawing and the server check, in lock pixels (640 x 250).</summary>
public static class LockpickGeometry
{
    public const float Width = 640f;
    public const float Height = 250f;
    public const float Shear = 100f;
    public const float Rest = 190f;
    public const float Keyway = 205f;
    public const float PinGap = 88f;
    public const float PivotX = 70f;
    public const int PerceptionHint = 8;

    /// <summary>Height of the green set band. Agility widens it; serrated pins are narrower.</summary>
    public static float Band(int agility, LockPinType type)
    {
        var band = 22f + Math.Clamp(agility, 1, 10) * 2.6f;
        return type == LockPinType.Serrated ? band * 0.7f : band;
    }

    /// <summary>Top of the key pin for a given lift.</summary>
    public static float Top(float length, float lift) => Rest - length - lift;

    /// <summary>The lift that puts the key pin top exactly on the shear line.</summary>
    public static float SetLift(float length) => Rest - length - Shear;
}
