using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Server.Access.Components;

[RegisterComponent]
public sealed partial class PresetIdCardComponent : Component
{
    [DataField("job")]
    public ProtoId<JobPrototype>? JobName;

    /// <summary>Keep this preset's cover identity when spawning; access still comes from the real job.</summary>
    [DataField]
    public ProtoId<JobPrototype>? IdentityJob;

    [DataField("name")]
    public string? IdName;
}
