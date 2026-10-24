using Content.Shared.Weapons.Reflect;

namespace Content.Shared._ReclamationProject.Institute.Augment;

/// <summary>
/// On an Institute MOD suit control unit: its <see cref="ReflectComponent"/> only deflects shots
/// while the suit is sealed and its power cell still has charge.
/// </summary>
[RegisterComponent]
public sealed partial class InstituteSuitDeflectComponent : Component
{
    /// <summary>
    /// What the suit deflects while it's on. Copied from the Reflect component at map init.
    /// </summary>
    [ViewVariables]
    public ReflectType ActiveReflects;

    [ViewVariables]
    public bool Active;
}
