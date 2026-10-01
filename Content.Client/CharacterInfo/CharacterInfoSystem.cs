using Content.Shared._Misfits.PlayerData;
using Content.Shared.CharacterInfo;
using Content.Shared.Objectives;
using Robust.Client.Player;
using Robust.Client.UserInterface;

namespace Content.Client.CharacterInfo;

public sealed class CharacterInfoSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _players = default!;

    public event Action<CharacterData>? OnCharacterUpdate;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<CharacterInfoEvent>(OnCharacterInfoEvent);
    }

    public void RequestCharacterInfo()
    {
        var entity = _players.LocalEntity;
        if (entity == null)
        {
            return;
        }

        RaiseNetworkEvent(new RequestCharacterInfoEvent(GetNetEntity(entity.Value)));
    }

    private void OnCharacterInfoEvent(CharacterInfoEvent msg, EntitySessionEventArgs args)
    {
        var entity = GetEntity(msg.NetEntity);
        // #Misfits Add - include persistent stats in the data bundle sent to the UI controller
        var data = new CharacterData(entity, msg.JobTitle, msg.Objectives, msg.Briefing, Name(entity), msg.Special, msg.PersistentStats);

        OnCharacterUpdate?.Invoke(data);
    }

    public List<Control> GetCharacterInfoControls(EntityUid uid)
    {
        var ev = new GetCharacterInfoControlsEvent(uid);
        RaiseLocalEvent(uid, ref ev, true);
        return ev.Controls;
    }

    // #Misfits Add - sends SPECIAL allocation confirmation to the server
    public void SendConfirmSpecialAllocation(int s, int p, int e, int c, int i, int a, int l)
    {
        RaiseNetworkEvent(new ConfirmSpecialAllocationEvent
        {
            Strength = s, Perception = p, Endurance = e, Charisma = c,
            Intelligence = i, Agility = a, Luck = l,
        });
    }

    public readonly record struct CharacterData(
        EntityUid Entity,
        string Job,
        Dictionary<string, List<ObjectiveInfo>> Objectives,
        string? Briefing,
        string EntityName,
        List<string> Special,
        // #Misfits Add - persistent SPECIAL / stats / history from server
        CharacterPersistentStats? PersistentStats = null
    );

    /// <summary>
    /// Event raised to get additional controls to display in the character info menu.
    /// </summary>
    [ByRefEvent]
    public readonly record struct GetCharacterInfoControlsEvent(EntityUid Entity)
    {
        public readonly List<Control> Controls = new();

        public readonly EntityUid Entity = Entity;
    }
}
