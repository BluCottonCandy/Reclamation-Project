using Robust.Shared.Prototypes;

namespace Content.Shared.Random;

[Prototype("RandomPlantMutationList")]
public sealed partial class RandomPlantMutationListPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("mutations", required: true, serverOnly: true)]
    public List<RandomPlantMutation> Mutations = new();
}
