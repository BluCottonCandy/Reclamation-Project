using Content.Shared.DoAfter;
using Content.Shared.Eui;
using Content.Shared.Humanoid;
using Robust.Shared.Serialization;

namespace Content.Shared._Reclamation.Reconstruction;

[Serializable, NetSerializable]
public enum ReconstructionPodVisuals : byte
{
    Running,
}

/// <summary>Raised on the pod when the operator finishes running a reconstruction.</summary>
[Serializable, NetSerializable]
public sealed partial class ReconstructionDoAfterEvent : SimpleDoAfterEvent;

/// <summary>The patient's current look, sent to the patient so they can edit it.</summary>
[Serializable, NetSerializable]
public sealed class ReconstructionEuiState : EuiStateBase
{
    public string Species = string.Empty;
    public string Name = string.Empty;
    public Sex Sex;
    public float Height;
    public float Width;
    public HumanoidCharacterAppearance Appearance = default!;
}

/// <summary>The patient's answer to the "change your appearance?" question.</summary>
[Serializable, NetSerializable]
public sealed class ReconstructionConsentMessage : EuiMessageBase
{
    public readonly bool Accepted;

    public ReconstructionConsentMessage(bool accepted)
    {
        Accepted = accepted;
    }
}

/// <summary>The new look the patient picked. The server validates every field.</summary>
[Serializable, NetSerializable]
public sealed class ReconstructionSubmitMessage : EuiMessageBase
{
    public readonly string Name;
    public readonly Sex Sex;
    public readonly float Height;
    public readonly float Width;
    public readonly HumanoidCharacterAppearance Appearance;

    public ReconstructionSubmitMessage(string name, Sex sex, float height, float width, HumanoidCharacterAppearance appearance)
    {
        Name = name;
        Sex = sex;
        Height = height;
        Width = width;
        Appearance = appearance;
    }
}
