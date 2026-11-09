using Robust.Shared.GameStates;
namespace Content.Shared._ReclamationProject.Hallucinations;

/// <summary>Temporary perceived damage for health feedback; real Damageable values are never overwritten.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class HallucinationPainComponent : Component
{
    [AutoNetworkedField] public float Amount;
    [AutoNetworkedField] public bool Collapsing;
    public Guid Encounter;
    public TimeSpan Expires;
    public TimeSpan NextBeginAt;
    public TimeSpan NextHitAt;
    public bool InducingCollapse;
    public TimeSpan CollapseUntil;
}
