using Robust.Shared.Map;
using Robust.Shared.Utility;

namespace Content.Server._ReclamationProject.Expeditions;

[RegisterComponent]
public sealed partial class FactoryDepartureComponent : Component
{
    [DataField] public ResPath MapPath = new("/Maps/Reclamation/Expeditions/Factory.yml");
    [DataField] public List<ResPath>? MapPool;
    [DataField] public List<ResPath>? MediumMapPool;
    [DataField] public List<ResPath>? HardMapPool;
    [DataField] public float Duration = 1200;
    [DataField] public float GatherRadius = 3;
}

[RegisterComponent]
public sealed partial class FactoryGearComponent : Component
{
    public EntityUid? ExpeditionMap;
    public MapCoordinates? ThrowOrigin;
}

[RegisterComponent]
public sealed partial class FactoryGeneratorComponent : Component
{
    [DataField] public bool Powered;
}

[RegisterComponent]
public sealed partial class FactoryVaultDoorComponent : Component
{
    [DataField] public bool Unlocked;
}

[RegisterComponent]
public sealed partial class FactoryArrivalComponent : Component;

[RegisterComponent]
public sealed partial class FactoryExitComponent : Component;

[RegisterComponent]
public sealed partial class FactoryRunComponent : Component
{
    public TimeSpan Deadline;
    public EntityCoordinates Return;
    public readonly HashSet<EntityUid> Bodies = new();
    public readonly HashSet<int> Warnings = new();
    public TimeSpan RetryAt;
    public bool ExpiryAnnounced;
}
