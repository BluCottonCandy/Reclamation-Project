using Content.Server.Construction.Components;
using Content.Server.Stack;
using Content.Shared._Misfits.Special;
using Content.Shared._Misfits.Special.Components;
using Content.Shared.Construction;
using Content.Shared.DoAfter;
using JetBrains.Annotations;
using Robust.Server.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using SharedToolSystem = Content.Shared.Tools.Systems.SharedToolSystem;

namespace Content.Server.Construction
{
    /// <summary>
    /// The server-side implementation of the construction system, which is used for constructing entities in game.
    /// </summary>
    [UsedImplicitly]
    public sealed partial class ConstructionSystem : SharedConstructionSystem
    {
        [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
        [Dependency] private readonly IRobustRandom _robustRandom = default!;
        [Dependency] private readonly SharedDoAfterSystem _doAfterSystem = default!;
        [Dependency] private readonly ContainerSystem _container = default!;
        [Dependency] private readonly StackSystem _stackSystem = default!;
        [Dependency] private readonly SharedToolSystem _toolSystem = default!;
        [Dependency] private readonly SharedSpecialSystem _special = default!;

        public override void Initialize()
        {
            base.Initialize();

            InitializeComputer();
            InitializeGraphs();
            InitializeGuided();
            InitializeInteractions();
            InitializeInitial();
            InitializeMachines();

            SubscribeLocalEvent<ConstructionComponent, ComponentInit>(OnConstructionInit);
            SubscribeLocalEvent<ConstructionComponent, ComponentStartup>(OnConstructionStartup);
        }

        private void OnConstructionInit(Entity<ConstructionComponent> ent, ref ComponentInit args)
        {
            var construction = ent.Comp;
            if (GetCurrentGraph(ent, construction) is not {} graph)
            {
                Log.Warning($"Prototype {EntityManager.GetComponent<MetaDataComponent>(ent).EntityPrototype?.ID}'s construction component has an invalid graph specified.");
                return;
            }

            if (GetNodeFromGraph(graph, construction.Node) is not {} node)
            {
                Log.Warning($"Prototype {EntityManager.GetComponent<MetaDataComponent>(ent).EntityPrototype?.ID}'s construction component has an invalid node specified.");
                return;
            }

            ConstructionGraphEdge? edge = null;
            if (construction.EdgeIndex is {} edgeIndex)
            {
                if (GetEdgeFromNode(node, edgeIndex) is not {} currentEdge)
                {
                    Log.Warning($"Prototype {EntityManager.GetComponent<MetaDataComponent>(ent).EntityPrototype?.ID}'s construction component has an invalid edge index specified.");
                    return;
                }

                edge = currentEdge;
            }

            if (construction.TargetNode is {} targetNodeId)
            {
                if (GetNodeFromGraph(graph, targetNodeId) is not { } targetNode)
                {
                    Log.Warning($"Prototype {EntityManager.GetComponent<MetaDataComponent>(ent).EntityPrototype?.ID}'s construction component has an invalid target node specified.");
                    return;
                }

                UpdatePathfinding(ent, graph, node, targetNode, edge, construction);
            }
        }

        private void OnConstructionStartup(EntityUid uid, ConstructionComponent construction, ComponentStartup args)
        {
            if (GetCurrentNode(uid, construction) is not {} node)
                return;

            PerformActions(uid, null, node.Actions);
        }

        public override void Update(float frameTime)
        {
            base.Update(frameTime);

            UpdateInteractions();
        }

        private float GetIntelligenceConstructionDelay(EntityUid user, float baseDelay)
        {
            if (baseDelay <= 0f)
                return baseDelay;

            var intelligence = _special.GetEffective(user, SpecialStat.Intelligence);
            if (intelligence <= SpecialProfile.Minimum)
                return baseDelay;

            var multiplier = intelligence >= SpecialProfile.DefaultValue
                ? 1f - (intelligence - SpecialProfile.DefaultValue) * 0.1f
                : 1f + (SpecialProfile.DefaultValue - intelligence) * 0.15f;

            return baseDelay * MathF.Max(0.1f, multiplier);
        }

        private bool CanCraftWithIntelligence(EntityUid user, bool showPopup = false)
        {
            if (TryComp<SpecialComponent>(user, out var special) &&
                _special.GetEffective(user, SpecialStat.Intelligence, special) > SpecialProfile.Minimum)
                return true;

            if (showPopup)
                _popup.PopupEntity(Loc.GetString("construction-system-construct-too-low-intelligence"), user, user);

            return false;
        }
    }
}
