using Content.Shared.NPC.Components;
using Robust.Shared.Prototypes;
using Content.Shared.NPC.Prototypes;


namespace Content.Shared.Nyanotrasen.NPC.Components.Faction
{
    [RegisterComponent]
    /// <summary>
    /// Allows clothing to add a faction to you when you wear it.
    /// </summary>
    public sealed partial class ClothingAddFactionComponent : Component
    {
        public bool IsActive = false;

        // Only undo the temporary grant on the faction component this item actually modified.
        // Job setup can replace that component with permanent faction membership after equipment is applied.
        public NpcFactionMemberComponent? AddedTo;


        /// <summary>
        /// Faction added
        /// </summary>
        [ViewVariables(VVAccess.ReadWrite),
         DataField("faction", required: true)]
        public ProtoId<NpcFactionPrototype> Faction = "";
    }
}
